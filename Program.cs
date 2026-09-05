using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace KidCut
{
    static class Program
    {
        private static HttpListener _listener;
        private static string _baseDir;
        private static string _distDir;
        private static int _port = 5173;

        [STAThread]
        static void Main()
        {
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _distDir = Path.Combine(_baseDir, "dist");

            // 1. Start Local HTTP Server for production build if dist exists
            bool hasDist = Directory.Exists(_distDir) && File.Exists(Path.Combine(_distDir, "index.html"));
            if (hasDist)
            {
                StartHttpServer();
            }

            // 2. Determine target URL
            string targetUrl = string.Format("http://localhost:{0}", _port);

            // 3. Launch App-Mode Window (no URL bar, standalone desktop feel)
            Process appProcess = LaunchAppWindow(targetUrl);

            if (appProcess != null)
            {
                appProcess.WaitForExit();
            }
            else
            {
                // Fallback: open default system browser
                Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true });
            }

            // Clean up server
            if (_listener != null && _listener.IsListening)
            {
                try { _listener.Stop(); } catch { }
            }
        }

        static void StartHttpServer()
        {
            // Find an open port starting from 5173
            for (int p = 5173; p <= 5190; p++)
            {
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(string.Format("http://localhost:{0}/", p));
                    _listener.Start();
                    _port = p;
                    break;
                }
                catch
                {
                    _listener = null;
                }
            }

            if (_listener != null)
            {
                Thread serverThread = new Thread(ListenLoop);
                serverThread.IsBackground = true;
                serverThread.Start();
            }
        }

        static void ListenLoop()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem((state) => HandleRequest(context));
                }
                catch
                {
                    break;
                }
            }
        }

        static void HandleRequest(HttpListenerContext context)
        {
            try
            {
                string rawUrl = context.Request.Url.AbsolutePath;
                if (rawUrl == "/" || string.IsNullOrEmpty(rawUrl))
                {
                    rawUrl = "/index.html";
                }

                // Security check
                string relPath = rawUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                string filePath = Path.Combine(_distDir, relPath);

                if (!File.Exists(filePath))
                {
                    filePath = Path.Combine(_distDir, "index.html");
                }

                byte[] fileBytes = File.ReadAllBytes(filePath);
                context.Response.ContentType = GetMimeType(filePath);
                context.Response.ContentLength64 = fileBytes.Length;
                context.Response.AddHeader("Cache-Control", "public, max-age=31536000");
                context.Response.OutputStream.Write(fileBytes, 0, fileBytes.Length);
                context.Response.OutputStream.Close();
            }
            catch
            {
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.OutputStream.Close();
                }
                catch { }
            }
        }

        static string GetMimeType(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".svg": return "image/svg+xml";
                case ".ico": return "image/x-icon";
                case ".wav": return "audio/wav";
                case ".mp3": return "audio/mpeg";
                case ".mp4": return "video/mp4";
                case ".webm": return "video/webm";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                case ".ttf": return "font/ttf";
                default: return "application/octet-stream";
            }
        }

        static Process LaunchAppWindow(string url)
        {
            // Try Microsoft Edge App Mode (installed by default on Windows 10/11)
            string edgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe");
            if (!File.Exists(edgePath))
            {
                edgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe");
            }

            string chromePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe");
            if (!File.Exists(chromePath))
            {
                chromePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe");
            }

            string browserPath = File.Exists(edgePath) ? edgePath : (File.Exists(chromePath) ? chromePath : null);

            if (!string.IsNullOrEmpty(browserPath))
            {
                string tempUserDir = Path.Combine(Path.GetTempPath(), "KidCut_App_Profile");
                string args = string.Format("--app=\"{0}\" --window-size=1300,850 --user-data-dir=\"{1}\" --enable-gpu-rasterization --enable-zero-copy --ignore-gpu-blocklist --disable-background-timer-throttling --disable-renderer-backgrounding --disable-features=Translate", url, tempUserDir);

                ProcessStartInfo psi = new ProcessStartInfo(browserPath, args)
                {
                    UseShellExecute = false
                };
                return Process.Start(psi);
            }

            return null;
        }
    }
}
