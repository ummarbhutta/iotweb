using IotWeb.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace IotWeb.Server.Helper
{
    public class FileDownloadProvider : IFileDownloadProvider
    {
        /// <summary>
        /// Returns the stream of file
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public Stream GetFileStream(string path)
        {
            try
            {
                FileStream fs = File.Open(path, FileMode.Open);
                return fs;
            }
            catch
            {
                return null;
            }
        }
    }
}
