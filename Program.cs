using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace KidCut
{
    static class Program
    {
        public static string BaseDir;
        public static string DistDir;
        public static int Port = 5173;
        public static string TargetUrl;
        public static HttpServer Server;
        public static KidCutControlForm MainForm;

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 1. Setup crash logging so nothing ever exits silently
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogFatalError(e.ExceptionObject as Exception);
            };
            Application.ThreadException += (s, e) =>
            {
                LogFatalError(e.Exception);
            };

            // 2. Single-instance Mutex check
            bool isNewInstance = false;
            Mutex mutex = new Mutex(true, "KidCut_VideoEditor_SingleInstance_Mutex", out isNewInstance);
            if (!isNewInstance)
            {
                // Already running: notify user and open browser
                try
                {
                    Process current = Process.GetCurrentProcess();
                    foreach (Process p in Process.GetProcessesByName(current.ProcessName))
                    {
                        if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
                            SetForegroundWindow(p.MainWindowHandle);
                            break;
                        }
                    }
                }
                catch { }

                OpenInBrowser("http://127.0.0.1:5173/");
                return;
            }

            try
            {
                // 3. Resolve base directories
                BaseDir = AppDomain.CurrentDomain.BaseDirectory;
                DistDir = Path.Combine(BaseDir, "dist");

                if (!File.Exists(Path.Combine(DistDir, "index.html")))
                {
                    if (File.Exists(Path.Combine(BaseDir, "index.html")))
                    {
                        DistDir = BaseDir;
                    }
                    else if (File.Exists(Path.Combine(BaseDir, @"..\dist\index.html")))
                    {
                        DistDir = Path.GetFullPath(Path.Combine(BaseDir, @"..\dist"));
                    }
                }

                if (!File.Exists(Path.Combine(DistDir, "index.html")))
                {
                    MessageBox.Show(
                        string.Format("No se encontró 'dist\\index.html'.\n\nBuscado en:\n{0}\n\nAsegúrate de que la carpeta 'dist' esté ubicada junto a KidCut.exe.", DistDir),
                        "KidCut - Error de Inicio",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                // 4. Start HTTP Server (HttpListener with automatic TcpListener fallback)
                Server = new HttpServer(DistDir);
                Port = Server.Start(5173, 5199);
                TargetUrl = string.Format("http://127.0.0.1:{0}/", Port);

                // 5. Open browser in app mode immediately
                OpenInBrowser(TargetUrl);

                // 6. Show Retro Control Window and run message loop
                MainForm = new KidCutControlForm();
                Application.Run(MainForm);
            }
            catch (Exception ex)
            {
                LogFatalError(ex);
            }
            finally
            {
                Cleanup();
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                    mutex.Close();
                }
            }
        }

        public static void OpenInBrowser(string url)
        {
            if (string.IsNullOrEmpty(url)) url = TargetUrl;

            // 1. Try Microsoft Edge in App Mode (without --user-data-dir to prevent Error 32 ProcessSingleton locks)
            string edge = FindExecutable(@"Microsoft\Edge\Application\msedge.exe");
            if (!string.IsNullOrEmpty(edge))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(edge, string.Format("--app=\"{0}\" --window-size=1300,850", url))
                    {
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                    return;
                }
                catch { }
            }

            // 2. Try Google Chrome in App Mode
            string chrome = FindExecutable(@"Google\Chrome\Application\chrome.exe");
            if (!string.IsNullOrEmpty(chrome))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(chrome, string.Format("--app=\"{0}\" --window-size=1300,850", url))
                    {
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                    return;
                }
                catch { }
            }

            // 3. Fallback: Default Browser
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo abrir el navegador de forma automática.\nPuede abrir manualmente la URL: " + url + "\n\nDetalle: " + ex.Message,
                    "KidCut",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private static string FindExecutable(string relPath)
        {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), relPath);
            if (File.Exists(p)) return p;
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), relPath);
            if (File.Exists(p)) return p;
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), relPath);
            if (File.Exists(p)) return p;
            return null;
        }

        public static void Cleanup()
        {
            if (Server != null)
            {
                Server.Stop();
                Server = null;
            }
        }

        private static void LogFatalError(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kidcut_error.log");
                File.AppendAllText(logFile, string.Format("\n[{0}] {1}\n{2}\n", DateTime.Now, ex.Message, ex.StackTrace));
            }
            catch { }

            MessageBox.Show(
                string.Format("Se produjo un error al iniciar KidCut:\n\n{0}\n\nRevisa el archivo 'kidcut_error.log' para más detalles.", ex.Message),
                "KidCut - Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    /// <summary>
    /// Dual-mode local server: tries HttpListener first, then falls back to pure TcpListener.
    /// Works 100% reliably without requiring Administrator or URL ACL permissions.
    /// </summary>
    public class HttpServer
    {
        private readonly string _rootDir;
        private HttpListener _httpListener;
        private TcpListener _tcpListener;
        private Thread _workerThread;
        private volatile bool _isRunning;
        public int Port { get; private set; }

        public HttpServer(string rootDir)
        {
            _rootDir = rootDir;
        }

        public int Start(int startPort, int endPort)
        {
            // First attempt: HttpListener
            for (int p = startPort; p <= endPort; p++)
            {
                try
                {
                    HttpListener hl = new HttpListener();
                    hl.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", p));
                    hl.Prefixes.Add(string.Format("http://localhost:{0}/", p));
                    hl.Start();

                    _httpListener = hl;
                    _isRunning = true;
                    Port = p;

                    _workerThread = new Thread(HttpListenerLoop) { IsBackground = true };
                    _workerThread.Start();
                    return p;
                }
                catch
                {
                    if (_httpListener != null)
                    {
                        try { _httpListener.Close(); } catch { }
                        _httpListener = null;
                    }
                }
            }

            // Fallback: Pure TcpListener HTTP Server (Bypasses HTTP.SYS URL ACL requirements completely)
            for (int p = startPort; p <= endPort; p++)
            {
                try
                {
                    TcpListener tl = new TcpListener(IPAddress.Loopback, p);
                    tl.Start();

                    _tcpListener = tl;
                    _isRunning = true;
                    Port = p;

                    _workerThread = new Thread(TcpListenerLoop) { IsBackground = true };
                    _workerThread.Start();
                    return p;
                }
                catch
                {
                    if (_tcpListener != null)
                    {
                        try { _tcpListener.Stop(); } catch { }
                        _tcpListener = null;
                    }
                }
            }

            throw new InvalidOperationException(string.Format("No se pudo iniciar el servidor en el rango de puertos {0}-{1}.", startPort, endPort));
        }

        public void Stop()
        {
            _isRunning = false;
            if (_httpListener != null)
            {
                try { _httpListener.Stop(); } catch { }
                try { _httpListener.Close(); } catch { }
                _httpListener = null;
            }
            if (_tcpListener != null)
            {
                try { _tcpListener.Stop(); } catch { }
                _tcpListener = null;
            }
        }

        private void HttpListenerLoop()
        {
            while (_isRunning && _httpListener != null && _httpListener.IsListening)
            {
                try
                {
                    HttpListenerContext context = _httpListener.GetContext();
                    ThreadPool.QueueUserWorkItem((state) => HandleHttpContext(context));
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private void HandleHttpContext(HttpListenerContext context)
        {
            try
            {
                string rawUrl = context.Request.Url.AbsolutePath;
                if (rawUrl == "/" || string.IsNullOrEmpty(rawUrl)) rawUrl = "/index.html";

                string relPath = rawUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                string filePath = Path.Combine(_rootDir, relPath);

                if (!File.Exists(filePath))
                {
                    filePath = Path.Combine(_rootDir, "index.html");
                }

                byte[] fileBytes = File.ReadAllBytes(filePath);
                context.Response.ContentType = GetMimeType(filePath);
                context.Response.ContentLength64 = fileBytes.Length;
                context.Response.AddHeader("Cache-Control", "no-cache");
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

        private void TcpListenerLoop()
        {
            while (_isRunning && _tcpListener != null)
            {
                try
                {
                    TcpClient client = _tcpListener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem((state) => HandleTcpClient(client));
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private void HandleTcpClient(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    byte[] buffer = new byte[4096];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) return;

                    string req = Encoding.UTF8.GetString(buffer, 0, read);
                    string[] lines = req.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;

                    string[] tokens = lines[0].Split(' ');
                    if (tokens.Length < 2) return;

                    string path = tokens[1].Split('?')[0];
                    if (path == "/" || string.IsNullOrEmpty(path)) path = "/index.html";

                    string relPath = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                    string filePath = Path.Combine(_rootDir, relPath);

                    if (!File.Exists(filePath))
                    {
                        filePath = Path.Combine(_rootDir, "index.html");
                    }

                    byte[] content = File.ReadAllBytes(filePath);
                    string mime = GetMimeType(filePath);

                    string header = string.Format(
                        "HTTP/1.1 200 OK\r\nContent-Type: {0}\r\nContent-Length: {1}\r\nConnection: close\r\n\r\n",
                        mime, content.Length
                    );
                    byte[] headerBytes = Encoding.UTF8.GetBytes(header);

                    stream.Write(headerBytes, 0, headerBytes.Length);
                    stream.Write(content, 0, content.Length);
                    stream.Flush();
                }
                catch { }
            }
        }

        public static string GetMimeType(string path)
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
    }

    /// <summary>
    /// Arcade-styled Control Form with System Tray minimization.
    /// Keeps the process alive and gives the user full control.
    /// </summary>
    public class KidCutControlForm : Form
    {
        private NotifyIcon _trayIcon;
        private ContextMenu _trayMenu;

        public KidCutControlForm()
        {
            Text = "KidCut - 8-Bit Pixel Video Studio";
            Size = new Size(540, 420);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(8, 6, 17); // Dark Retro Neon Background

            // Load Application Icon safely
            string iconPath = Path.Combine(Program.BaseDir, "icon.ico");
            if (File.Exists(iconPath))
            {
                try { Icon = new Icon(iconPath); } catch { }
            }

            // Setup System Tray Icon
            SetupSystemTray();

            // 1. Logo PictureBox (Safely loaded without file locks)
            PictureBox pbLogo = new PictureBox();
            pbLogo.Size = new Size(76, 76);
            pbLogo.Location = new Point((ClientSize.Width - 76) / 2, 20);
            pbLogo.SizeMode = PictureBoxSizeMode.Zoom;
            string logoPath = Path.Combine(Program.BaseDir, @"public\logo.png");
            if (File.Exists(logoPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(logoPath, FileMode.Open, FileAccess.Read))
                    {
                        pbLogo.Image = Image.FromStream(fs);
                    }
                }
                catch { }
            }
            Controls.Add(pbLogo);

            // 2. Title Label
            Label lblTitle = new Label();
            lblTitle.Text = "★ KIDCUT VIDEO STUDIO ★";
            lblTitle.Font = new Font("Segoe UI", 13, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(255, 210, 0); // Gold Arcade
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            lblTitle.Size = new Size(500, 28);
            lblTitle.Location = new Point(20, 104);
            Controls.Add(lblTitle);

            // 3. Status Label
            Label lblStatus = new Label();
            lblStatus.Text = string.Format("● SERVIDOR EN LÍNEA: {0}", Program.TargetUrl);
            lblStatus.Font = new Font("Consolas", 10, FontStyle.Bold);
            lblStatus.ForeColor = Color.FromArgb(57, 255, 20); // Neon Green
            lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            lblStatus.Size = new Size(500, 24);
            lblStatus.Location = new Point(20, 136);
            Controls.Add(lblStatus);

            // 4. Subtitle / Note
            Label lblSub = new Label();
            lblSub.Text = "KidCut está ejecutándose en tu navegador. Puedes minimizar esta ventana a la bandeja.";
            lblSub.Font = new Font("Segoe UI", 8, FontStyle.Regular);
            lblSub.ForeColor = Color.FromArgb(0, 240, 255); // Cyan
            lblSub.TextAlign = ContentAlignment.MiddleCenter;
            lblSub.Size = new Size(500, 32);
            lblSub.Location = new Point(20, 162);
            Controls.Add(lblSub);

            // 5. Button: Reabrir en Navegador
            Button btnOpen = new Button();
            btnOpen.Text = "🚀 ABRIR / REABRIR KIDCUT (VENTANA PRINCIPAL)";
            btnOpen.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            btnOpen.BackColor = Color.FromArgb(255, 210, 0);
            btnOpen.ForeColor = Color.Black;
            btnOpen.FlatStyle = FlatStyle.Flat;
            btnOpen.FlatAppearance.BorderSize = 0;
            btnOpen.Size = new Size(460, 42);
            btnOpen.Location = new Point(40, 204);
            btnOpen.Cursor = Cursors.Hand;
            btnOpen.Click += (s, e) => Program.OpenInBrowser(Program.TargetUrl);
            Controls.Add(btnOpen);

            // 6. Button: Crear Acceso Directo en el Escritorio
            Button btnShortcut = new Button();
            btnShortcut.Text = "📌 CREAR ACCESO DIRECTO EN EL ESCRITORIO";
            btnShortcut.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            btnShortcut.BackColor = Color.FromArgb(30, 40, 75);
            btnShortcut.ForeColor = Color.FromArgb(0, 240, 255);
            btnShortcut.FlatStyle = FlatStyle.Flat;
            btnShortcut.FlatAppearance.BorderColor = Color.FromArgb(0, 240, 255);
            btnShortcut.Size = new Size(460, 34);
            btnShortcut.Location = new Point(40, 254);
            btnShortcut.Cursor = Cursors.Hand;
            btnShortcut.Click += (s, e) => CreateDesktopShortcut();
            Controls.Add(btnShortcut);

            // 7. Button: Abrir Carpeta Local
            Button btnFolder = new Button();
            btnFolder.Text = "📁 ABRIR CARPETA";
            btnFolder.Font = new Font("Segoe UI", 8, FontStyle.Regular);
            btnFolder.BackColor = Color.FromArgb(25, 20, 45);
            btnFolder.ForeColor = Color.White;
            btnFolder.FlatStyle = FlatStyle.Flat;
            btnFolder.FlatAppearance.BorderColor = Color.FromArgb(100, 90, 140);
            btnFolder.Size = new Size(225, 34);
            btnFolder.Location = new Point(40, 296);
            btnFolder.Cursor = Cursors.Hand;
            btnFolder.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", string.Format("\"{0}\"", Program.BaseDir)) { UseShellExecute = true });
                }
                catch { }
            };
            Controls.Add(btnFolder);

            // 8. Button: Detener y Salir
            Button btnExit = new Button();
            btnExit.Text = "❌ CERRAR KIDCUT";
            btnExit.Font = new Font("Segoe UI", 8, FontStyle.Regular);
            btnExit.BackColor = Color.FromArgb(45, 15, 25);
            btnExit.ForeColor = Color.FromArgb(255, 80, 100);
            btnExit.FlatStyle = FlatStyle.Flat;
            btnExit.FlatAppearance.BorderColor = Color.FromArgb(255, 50, 70);
            btnExit.Size = new Size(225, 34);
            btnExit.Location = new Point(275, 296);
            btnExit.Cursor = Cursors.Hand;
            btnExit.Click += (s, e) => Close();
            Controls.Add(btnExit);

            // 9. Info Footer
            Label lblFooter = new Label();
            lblFooter.Text = "Al minimizar, KidCut permanecerá activo en la bandeja junto al reloj.";
            lblFooter.Font = new Font("Segoe UI", 7, FontStyle.Italic);
            lblFooter.ForeColor = Color.FromArgb(120, 110, 150);
            lblFooter.TextAlign = ContentAlignment.MiddleCenter;
            lblFooter.Size = new Size(500, 20);
            lblFooter.Location = new Point(20, 345);
            Controls.Add(lblFooter);

            // Events
            FormClosing += (s, e) =>
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                Program.Cleanup();
            };

            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    Hide();
                    if (_trayIcon != null)
                    {
                        _trayIcon.ShowBalloonTip(1500, "KidCut Studio", "KidCut sigue ejecutándose en segundo plano.", ToolTipIcon.Info);
                    }
                }
            };
        }

        private void SetupSystemTray()
        {
            _trayMenu = new ContextMenu();
            _trayMenu.MenuItems.Add("🚀 Abrir KidCut Studio", (s, e) =>
            {
                Show();
                WindowState = FormWindowState.Normal;
                Program.OpenInBrowser(Program.TargetUrl);
            });
            _trayMenu.MenuItems.Add("📁 Carpeta de Proyecto", (s, e) =>
            {
                Process.Start(new ProcessStartInfo("explorer.exe", string.Format("\"{0}\"", Program.BaseDir)) { UseShellExecute = true });
            });
            _trayMenu.MenuItems.Add("-");
            _trayMenu.MenuItems.Add("❌ Salir de KidCut", (s, e) => Close());

            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "KidCut Studio (Activo)";
            if (Icon != null) _trayIcon.Icon = Icon;
            _trayIcon.ContextMenu = _trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) =>
            {
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
            };
        }

        private void CreateDesktopShortcut()
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) throw new Exception("WScript.Shell no está disponible en este sistema.");

                object shell = Activator.CreateInstance(shellType);
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktop, "KidCut Studio.lnk");

                object shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type scType = shortcut.GetType();

                scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(Program.BaseDir, "KidCut.exe") });
                scType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Program.BaseDir });
                scType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "KidCut - 8-Bit Pixel Video Studio" });

                string ico = Path.Combine(Program.BaseDir, "icon.ico");
                if (File.Exists(ico))
                {
                    scType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { ico });
                }

                scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);

                MessageBox.Show(
                    "¡Acceso directo 'KidCut Studio' creado exitosamente en tu Escritorio!",
                    "KidCut Studio",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo crear el acceso directo:\n" + ex.Message,
                    "KidCut",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }
    }
}

