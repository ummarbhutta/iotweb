using IotWeb.Common.Util;
using System.IO;

namespace IotWeb.Common.Interfaces
{
    /// <summary>
    /// Provides methods for file downloading for specific platform
    /// Target platform should provide the implementation
    /// </summary>
    public interface IFileDownloadProvider
    {
        Stream GetFileStream(string path);
    }
}
