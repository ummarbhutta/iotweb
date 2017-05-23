using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IotWeb.Common.Util;

namespace IotWeb.Common.Interfaces
{
    public interface IMultiPartFileStorageHandler
    {
        Stream GetFile(string fileName);

        string GetTempFilePath(string fileName);

        bool DeleteFiles(List<string> filesName);

        void ClearTempFolder();
    }
}