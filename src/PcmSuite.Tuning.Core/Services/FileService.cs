using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace PcmSuite.Tuning.Core.Services
{
    public class FileService
    {
        private readonly string _basePath;

        public FileService(string basePath)
        {
            _basePath = basePath;
        }

        public IEnumerable<string> GetBinFiles() => 
            Directory.GetFiles(Path.Combine(_basePath, "Bin Files"), "*.bin");

        public IEnumerable<string> GetXdfFiles() => 
            Directory.GetFiles(Path.Combine(_basePath, "XDF Files"), "*.xdf");
    }
}