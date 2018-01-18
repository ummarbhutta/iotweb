using IotWeb.Common.Http;
using IotWeb.Common.Interfaces;

namespace IotWeb.Common
{
	public delegate void ServerStoppedHandler(IServer server);

	public interface IServer
	{
		event ServerStoppedHandler ServerStopped;

		bool Running { get; }

		void Start();

		void Stop();

        bool ApplyBeforeFilters(HttpRequest request, HttpResponse response, HttpContext context);

        void ApplyAfterFilters(HttpRequest request, HttpResponse response, HttpContext context);

        IHttpRequestHandler GetHandlerForUri(string uri, out string partialUri);

        IWebSocketRequestHandler GetHandlerForWebSocket(string uRI, out string partial);

        ISessionStorageHandler SessionStorageHandler { get; }

        IFileDownloadProviderFactory DownloadProviderFactoryInstance { get; }

        void AddHttpRequestHandler(string uri, HttpRequestHandlerBase handler);

        void AddWebSocketRequestHandler(string uri, IWebSocketRequestHandler handler);
    }
}
