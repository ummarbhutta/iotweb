using IotWeb.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IotWeb.Server.Helper
{
    public class FileDownloadProviderFactory : IFileDownloadProviderFactory
    {
        public IFileDownloadProvider GetFileDownloadProvider()
        {
            return new FileDownloadProvider();
        }
    }
}
