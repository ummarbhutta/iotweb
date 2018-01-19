using IotWeb.Common;
using IotWeb.Common.Http;
using IotWeb.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IotWeb.Server
{
    public class HttpsSocketServer : ISocketServer
    {
        private ConnectionHandler connection_requested_handler;
        private X509Certificate serverCertificate;

        public int Port { get; private set; }

        public ConnectionHandler ConnectionRequested
        {
            get
            {
                return connection_requested_handler;
            }

            set
            {
                lock (this)
                {
                    if (Running)
                        throw new InvalidOperationException("Cannot change handler while server is running.");
                    connection_requested_handler = value;
                }
            }
        }

        public bool Running { get; private set; }

        public event ServerStoppedHandler ServerStopped;

        /// <summary>
        /// Constructor with a port to listen on
        /// </summary>
        /// <param name="port"></param>
        public HttpsSocketServer(int port, string serverCertificate, string password)
        {
            Port = port;
            this.serverCertificate = new X509Certificate2(serverCertificate,password);
        }

        public void Start()
        {
            // Make sure we are not already running
            lock (this)
            {
                if (Running)
                    throw new InvalidOperationException("Socket server is already running.");
                Running = true;
            }

            // Set up the listener and bind
            ThreadPool.QueueUserWorkItem((arg) =>
            {

                // Create a TCP/IP (IPv4) socket and listen for incoming connections.
                TcpListener listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();

                // Wait for incoming connections
                while (true)
                {
                    lock (this)
                    {
                        if (!Running)
                        {
                            listener.Stop();
                            break;
                        }
                    }
                    try
                    {
                        SslStream sslStream;
                        TcpClient client;
                        try
                        {
                            client = listener.AcceptTcpClient();

                            // A client has connected. Create the 
                            // SslStream using the client's network stream.
                            sslStream = new SslStream(client.GetStream(), false);
                        }
                        catch (TimeoutException)
                        {
                            // Allow recheck of running status
                            continue;
                        }
                        if (connection_requested_handler != null)
                        {
                            string hostname = "0.0.0.0";
                            IPEndPoint endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                            if (endpoint != null)
                                hostname = endpoint.Address.ToString();
                            ThreadPool.QueueUserWorkItem((e) =>
                            {
                                try
                                {
                                    // Authenticate the server but don't require the client to authenticate.
                                    sslStream.AuthenticateAsServer(serverCertificate, false, SslProtocols.Tls, false);
                                    client.ReceiveTimeout = 0;
                                    connection_requested_handler?.Invoke(this, hostname, sslStream, sslStream);
                                }
                                catch (Exception)
                                {
                                    // Quietly consume the exception
                                }
                                // Finally, we can close the stream
                                sslStream.Close();
                                client.Close();
                            });
                        }
                    }
                    catch (Exception)
                    {
                        // Quietly consume the exception
                    }
                }

                // Server was stopped. Fire the stopped events
                lock (this)
                {
                    ServerStopped?.Invoke(this);
                }
            });
        }

        public void Stop()
        {
            lock (this)
            {
                if (!Running)
                    return;
                // Shutdown the server
                Running = false;
            }
        }
        
    }

}
