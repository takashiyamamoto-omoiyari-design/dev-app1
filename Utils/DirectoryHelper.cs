using System.IO;

namespace AzureRag.Utils
{
    public static class DirectoryHelper
    {
        /// <summary>
        /// ディレクトリが存在しない場合は作成します
        /// </summary>
        public static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}