using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IotWeb.Common.Interfaces
{
    public interface IFileDownloadProviderFactory
    {
        IFileDownloadProvider GetFileDownloadProvider();
    }
}
