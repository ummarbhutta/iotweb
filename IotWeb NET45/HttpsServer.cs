using IotWeb.Common;
using IotWeb.Common.Http;
using IotWeb.Common.Util;
using IotWeb.Server.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IotWeb.Server
{
    public class HttpsServer : BaseHttpsServer
    {
        public HttpsServer(int port, string certificate, string password)
           : base(new HttpsSocketServer(port,certificate, password), new HybridSessionStorageHandler(new SessionConfiguration()), new FileDownloadProviderFactory())
        {
            // No configuration required
        }

        public HttpsServer(int port, SessionConfiguration sessionConfiguration, string certificate, string password)
            : base(new HttpsSocketServer(port,certificate, password), new HybridSessionStorageHandler(sessionConfiguration), new FileDownloadProviderFactory())
        {
            // No configuration required
        }
    }
}
