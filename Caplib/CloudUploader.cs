using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace Caplib
{
    internal static class CloudUploader
    {
        static CloudUploader()
        {
            // .NET Framework 4.5 기본값은 TLS 1.0이므로 TLS 1.2 활성화 필수
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // Tls12
        }

        public static string Login(string baseUrl, string username, string password)
        {
            string url = baseUrl.TrimEnd('/') + "/api/login";
            string json = string.Format(
                "{{\"username\":\"{0}\",\"password\":\"{1}\"}}",
                EscapeJson(username),
                EscapeJson(password));

            byte[] payload = Encoding.UTF8.GetBytes(json);

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.ContentLength = payload.Length;
            req.Timeout = 15000;

            using (Stream reqStream = req.GetRequestStream())
            {
                reqStream.Write(payload, 0, payload.Length);
            }

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string token = reader.ReadToEnd().Trim().Trim('"');
                if (string.IsNullOrEmpty(token))
                {
                    throw new InvalidOperationException("인증 토큰을 받지 못했습니다.");
                }
                return token;
            }
        }

        public static void UploadPath(string baseUrl, string username, string password, string localPath)
        {
            if (Directory.Exists(localPath))
            {
                string dirName = new DirectoryInfo(localPath).Name;
                string tempZip = Path.Combine(
                    Path.GetTempPath(),
                    string.Format("{0}_{1}.zip", dirName, DateTime.Now.ToString("yyyyMMdd_HHmmss")));

                try
                {
                    ZipFile.CreateFromDirectory(localPath, tempZip, CompressionLevel.Optimal, false);
                    UploadFile(baseUrl, username, password, tempZip, dirName + ".zip");
                }
                finally
                {
                    try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                }
            }
            else if (File.Exists(localPath))
            {
                UploadFile(baseUrl, username, password, localPath, Path.GetFileName(localPath));
            }
            else
            {
                throw new FileNotFoundException("업로드할 대상을 찾을 수 없습니다.", localPath);
            }
        }

        public static void UploadFile(string baseUrl, string username, string password, string localFilePath, string remoteFileName)
        {
            string token = Login(baseUrl, username, password);
            string encodedName = Uri.EscapeDataString(remoteFileName);
            string url = string.Format("{0}/api/resources/{1}?override=true", baseUrl.TrimEnd('/'), encodedName);

            FileInfo fi = new FileInfo(localFilePath);

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.Headers.Add("X-Auth", token);
            req.ContentType = "application/octet-stream";
            req.ContentLength = fi.Length;
            req.AllowWriteStreamBuffering = false;
            req.Timeout = 300000; // 대용량 파일 대비 5분
            req.ReadWriteTimeout = 300000;

            byte[] buffer = new byte[65536];
            using (FileStream fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Stream reqStream = req.GetRequestStream())
            {
                int bytesRead;
                while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    reqStream.Write(buffer, 0, bytesRead);
                }
            }

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            {
                if ((int)resp.StatusCode < 200 || (int)resp.StatusCode >= 300)
                {
                    throw new InvalidOperationException("업로드 실패: HTTP " + (int)resp.StatusCode);
                }
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t");
        }
    }
}
