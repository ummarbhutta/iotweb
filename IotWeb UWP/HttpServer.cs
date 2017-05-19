using IotWeb.Common.Http;
using IotWeb.Common.Util;
using IotWeb.Server.Helper;

namespace IotWeb.Server
{
    public class HttpServer : BaseHttpServer
    {
        public HttpServer(int port, MultiPartFileStorageHandler multiPartFileStorageHandler)
            : base(new SocketServer(port), multiPartFileStorageHandler)
        {
            // No configuration required
        }
    }
}
