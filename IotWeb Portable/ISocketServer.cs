using System.IO;

namespace IotWeb.Common
{
	public delegate void ConnectionHandler(ISocketServer sender, string hostname, Stream input, Stream output);

	public interface ISocketServer 
	{
		/// <summary>
		/// The port to listen on for connections
		/// </summary>
		int Port { get; }

        /// <summary>
        /// Delegate invoked when a new connection is accepted
        /// </summary>
		ConnectionHandler ConnectionRequested { get; set; }

        event ServerStoppedHandler ServerStopped;

        bool Running { get; }

        void Start();

        void Stop();
    }
}
