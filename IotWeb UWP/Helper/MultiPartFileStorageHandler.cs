using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using IotWeb.Common.Interfaces;
using IotWeb.Common.Util;

namespace IotWeb.Server.Helper
{
    public class MultiPartFileStorageHandler : IMultiPartFileStorageHandler
    {
        private readonly string _storageFolder = "TempSessionData";
        private string StoragePath { get; set; }

        public MultiPartFileStorageHandler(string storageFolder = "")
        {
            if (!string.IsNullOrWhiteSpace(storageFolder))
                _storageFolder = storageFolder;
            
            SetStoragePath();
        }

        private void SetStoragePath()
        {
            string fullStorageFilePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, _storageFolder);

            if (!Directory.Exists(fullStorageFilePath))
                Directory.CreateDirectory(fullStorageFilePath);

            StoragePath = fullStorageFilePath;
        }

        public Stream GetFile(string fileName)
        {
            try
            {
                var filePath = Path.Combine(StoragePath, fileName);
                
                FileStream fileStream = File.Exists(filePath) ? new FileStream(filePath, FileMode.Append) : new FileStream(filePath, FileMode.CreateNew);
                return fileStream;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public string GetTempFilePath(string fileName)
        {
            var filePath = Path.Combine(StoragePath, fileName);
            var fName = Path.GetFileNameWithoutExtension(fileName);
            var fExtension = Path.GetExtension(fileName);
            int counter = 0;

            while (File.Exists(filePath))
            {
                filePath = Path.Combine(StoragePath, fName + " - Copy" + (counter == 0 ? "" : " (" + counter + ")") + fExtension);
                counter++;
            }

            return filePath;
        }

        public bool DeleteFiles(List<string> filesName)
        {
            try
            {
                foreach (var file in filesName)
                {
                    if (File.Exists(file))
                    {
                        File.Delete(Path.Combine(StoragePath, file));
                    }
                }
            }
            catch (Exception e)
            {
                return false;
            }
            
            return true;
        }
    }
}
