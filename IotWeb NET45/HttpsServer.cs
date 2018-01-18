using IotWeb.Common;
using IotWeb.Common.Util;
using IotWeb.Server.Helper;
using IotWeb.Server.Https;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace IotWeb.Server
{
    public class HttpsServer : BaseHttpsServer
    {
        public HttpsServer(int port, X509Certificate certificate)
           : base(new HttpsSocketServer(port,certificate), new HybridSessionStorageHandler(new SessionConfiguration()), new FileDownloadProviderFactory())
        {
            // No configuration required
        }

        public HttpsServer(int port, SessionConfiguration sessionConfiguration, X509Certificate certificate)
            : base(new HttpsSocketServer(port,certificate), new HybridSessionStorageHandler(sessionConfiguration), new FileDownloadProviderFactory())
        {
            // No configuration required
        }
    }
}
