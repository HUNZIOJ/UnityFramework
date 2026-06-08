using System.IO;

namespace Frame.Utilities
{
    public static class FramePathUtility
    {
        public static string NormalizeResourcesPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            path = path.Replace('\\', '/').Trim();
            string extension = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(extension))
            {
                path = path.Substring(0, path.Length - extension.Length);
            }

            const string resourcesToken = "/Resources/";
            int resourcesIndex = path.IndexOf(resourcesToken, System.StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex >= 0)
            {
                path = path.Substring(resourcesIndex + resourcesToken.Length);
            }

            return path;
        }

        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "default";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                fileName = fileName.Replace(invalid[i], '_');
            }

            return fileName;
        }
    }
}
