using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IotWeb.Common.Http
{
    public abstract class HttpRequestHandlerBase : IHttpRequestHandler
    {
        /// <summary>
        /// Handle a request
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="request"></param>
        /// <param name="response"></param>
        /// <param name="context"></param>
        public abstract void HandleRequest(string uri, HttpRequest request, HttpResponse response, HttpContext context);

        /// <summary>
        /// This method is called when all the contents are written to output stream,
        /// If a cleanup is required regarding a specific request, child class should override this method
        /// </summary>
        /// <param name="context">HttpContext passed to request completed</param>
        public virtual void RequestCompleted(HttpContext context)
        {

        }
    }
}
