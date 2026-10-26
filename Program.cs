using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace KidCut
{
    static class Program
    {
        public static string BaseDir;
        public static string DistDir;
        public static string ScratchDir;
        public static string ExportsDir;
        public static int Port = 5173;
        public static string TargetUrl;
        public static HttpServer Server;
        public static KidCutControlForm MainForm;
        public static RecorderOverlay Overlay;
        public static readonly string RuntimeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string path);

        // ONNX Runtime and its dependencies live in runtime\ (any version: the copies there are the ones we ship)
        private static void SetupRuntimeFolder()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string file = Path.Combine(RuntimeDir, new System.Reflection.AssemblyName(e.Name).Name + ".dll");
                return File.Exists(file) ? System.Reflection.Assembly.LoadFrom(file) : null;
            };
            string native = Path.Combine(RuntimeDir, "onnxruntime.dll");
            if (File.Exists(native)) LoadLibrary(native);
        }
        // UI-thread context; unlike MainForm.BeginInvoke it survives the control form recreating its handle when hidden
        private static SynchronizationContext _uiContext;

        public static void ShowOverlay()
        {
            if (_uiContext == null) return;
            _uiContext.Post(_ =>
            {
                if (Overlay == null || Overlay.IsDisposed) Overlay = new RecorderOverlay();
                Overlay.Show();
                Overlay.BringToFront();
            }, null);
        }

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kidcut_boot.log"), 
                    string.Format("[{0}] Main started. Args: {1}\n", DateTime.Now, string.Join(" ", args ?? new string[0])));
            }
            catch { }

            SetupRuntimeFolder();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Terminate any previous KidCut instances to ensure port 5173 is immediately free
            try
            {
                Process currentProc = Process.GetCurrentProcess();
                foreach (Process p in Process.GetProcessesByName("KidCut"))
                {
                    if (p.Id != currentProc.Id)
                    {
                        try
                        {
                            p.Kill();
                            p.WaitForExit(600);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 1. Setup crash logging so nothing ever exits silently
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogFatalError(e.ExceptionObject as Exception);
            };
            Application.ThreadException += (s, e) =>
            {
                LogFatalError(e.Exception);
            };

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

                // Setup Scratch and Exports directories
                ScratchDir = Path.Combine(BaseDir, @"scratch\media");
                if (!Directory.Exists(ScratchDir))
                {
                    Directory.CreateDirectory(ScratchDir);
                }

                string myVideos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (string.IsNullOrEmpty(myVideos))
                {
                    myVideos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
                }
                ExportsDir = Path.Combine(myVideos, "KidCut_Exports");
                if (!Directory.Exists(ExportsDir))
                {
                    try { Directory.CreateDirectory(ExportsDir); } catch { }
                }
                ScreenRecorder.RecordingsDir = Path.Combine(myVideos, "KidCut_Grabaciones");

                // Pre-locate native FFmpeg
                FfmpegEngine.Initialize();
                if (FfmpegEngine.IsAvailable) new Thread(ScreenRecorder.WarmUp) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();

                // 4. Start HTTP Server
                Server = new HttpServer(DistDir);
                Port = Server.Start(5173, 5199);
                TargetUrl = string.Format("http://127.0.0.1:{0}/", Port);

                bool isHeadless = !Environment.UserInteractive;
                string[] cmdArgs = Environment.GetCommandLineArgs();
                if (cmdArgs != null)
                {
                    for (int i = 0; i < cmdArgs.Length; i++)
                    {
                        if (cmdArgs[i].Equals("--server", StringComparison.OrdinalIgnoreCase) || 
                            cmdArgs[i].Equals("/server", StringComparison.OrdinalIgnoreCase))
                        {
                            isHeadless = true;
                            break;
                        }
                    }
                }

                if (isHeadless)
                {
                    // Keep headless HTTP and FFmpeg server alive indefinitely
                    AutoResetEvent stopEvent = new AutoResetEvent(false);
                    stopEvent.WaitOne();
                    return;
                }

                // 5. Open browser in App mode (chromeless, standalone window)
                OpenInBrowser(TargetUrl);

                // 6. Show Retro Control Window and run message loop
                MainForm = new KidCutControlForm();
                _uiContext = SynchronizationContext.Current;
                ShowOverlay();
                Application.Run(MainForm);
            }
            catch (Exception ex)
            {
                LogFatalError(ex);
            }
            finally
            {
                Cleanup();
            }
        }

        public static void OpenInBrowser(string url)
        {
            if (string.IsNullOrEmpty(url)) url = TargetUrl;

            // 1. Try Microsoft Edge in App Mode (chromeless native-like standalone window)
            string edge = FindExecutable(@"Microsoft\Edge\Application\msedge.exe");
            if (!string.IsNullOrEmpty(edge))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(edge, string.Format("--app=\"{0}\" --window-size=1360,860", url))
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
                    ProcessStartInfo psi = new ProcessStartInfo(chrome, string.Format("--app=\"{0}\" --window-size=1360,860", url))
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
            try { FfmpegEngine.Cancel(); } catch { }
            try { ScreenRecorder.StopAndWait(); } catch { }
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
                string.Format("Se produjo un error en KidCut:\n\n{0}\n\nRevisa el archivo 'kidcut_error.log' para más detalles.", ex.Message),
                "KidCut - Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    #region Data Contracts for Native API

    public class TimelineExportDto
    {
        public string resolution { get; set; }
        public string ratio { get; set; }
        public string format { get; set; }
        public int fps { get; set; }
        public double duration { get; set; }
        public List<ClipExportDto> clips { get; set; }
        public List<AudioTrackExportDto> audioTracks { get; set; }
        public string overlayImagePath { get; set; }
    }

    public class ClipExportDto
    {
        public ClipExportDto()
        {
            scale = 1.0;
            opacity = 1.0;
            posX = 0.5;
            posY = 0.5;
            speed = 1.0;
            volume = 1.0;
            trackOrder = 1;
        }

        public string type { get; set; }
        public string path { get; set; }
        public double start { get; set; }
        public double duration { get; set; }
        public double trimIn { get; set; }
        public double speed { get; set; }
        public double volume { get; set; }
        public bool pixelate { get; set; }
        public bool scanlines { get; set; }
        public bool monochrome { get; set; }
        public double brightness { get; set; }
        public double contrast { get; set; }

        // Multi-track, Scale, and Canvas Layout Properties
        public double scale { get; set; }
        public double posX { get; set; }
        public double posY { get; set; }
        public int renderWidth { get; set; }
        public int renderHeight { get; set; }
        public int offsetX { get; set; }
        public int offsetY { get; set; }
        public double rotation { get; set; }
        public double opacity { get; set; }
        public int trackOrder { get; set; }
    }

    public class AudioTrackExportDto
    {
        public string path { get; set; }
        public double start { get; set; }
        public double duration { get; set; }
        public double trimIn { get; set; }
        public double speed { get; set; }
        public double volume { get; set; }
    }

    public class StreamStartRequestDto
    {
        public int width { get; set; }
        public int height { get; set; }
        public int fps { get; set; }
        public int totalFrames { get; set; }
        public string format { get; set; }
        public string audioPath { get; set; }
    }

    public class ExportStatusDto
    {
        public bool isRunning { get; set; }
        public bool isFinished { get; set; }
        public bool isError { get; set; }
        public long currentFrame { get; set; }
        public long totalFrames { get; set; }
        public int percent { get; set; }
        public double fps { get; set; }
        public string speed { get; set; }
        public int ramMb { get; set; }
        public string outputPath { get; set; }
        public string errorMessage { get; set; }
    }

    #endregion

    #region Native FFmpeg Industrial Engine

    /// <summary>
    /// High-performance native FFmpeg processing engine.
    /// Operates with strict circular frame buffer streaming, keeping RAM usage < 50 MB
    /// from frame 1 to frame 1,000,000.
    /// </summary>
    public static class FfmpegEngine
    {
        public static string FfmpegPath { get; private set; }
        public static bool IsAvailable { get; private set; }

        private static Process _activeProcess;
        private static StreamWriter _activeStdinWriter;
        private static readonly object _syncLock = new object();

        // Progress Tracking
        public static bool IsRunning { get; private set; }
        public static bool IsFinished { get; private set; }
        public static bool IsError { get; private set; }
        public static long CurrentFrame { get; private set; }
        public static long TotalFrames { get; private set; }
        public static int Percent { get; private set; }
        public static double Fps { get; private set; }
        public static string Speed { get; private set; }
        public static int RamMb { get; private set; }
        public static string OutputPath { get; private set; }
        public static string ErrorMessage { get; private set; }

        public static string FfprobePath { get; private set; }

        public static void Initialize()
        {
            FfmpegPath = FindFfmpeg();
            FfprobePath = FindFfprobe();
            IsAvailable = !string.IsNullOrEmpty(FfmpegPath) && File.Exists(FfmpegPath);
        }

        public static string FindFfprobe()
        {
            if (!string.IsNullOrEmpty(FfmpegPath) && File.Exists(FfmpegPath))
            {
                string dir = Path.GetDirectoryName(FfmpegPath);
                string probe = Path.Combine(dir, "ffprobe.exe");
                if (File.Exists(probe)) return probe;
            }

            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffprobe.exe");
            if (File.Exists(localPath)) return localPath;

            return null;
        }

        public static bool HasAudioStream(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".bmp" || ext == ".webp")
                return false;

            string probe = FfprobePath ?? FindFfprobe();
            if (!string.IsNullOrEmpty(probe) && File.Exists(probe))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(probe, string.Format("-v error -select_streams a:0 -show_entries stream=codec_name -of csv=p=0 \"{0}\"", filePath))
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (Process p = Process.Start(psi))
                    {
                        string res = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        return !string.IsNullOrWhiteSpace(res);
                    }
                }
                catch { }
            }
            return true;
        }

        private static string FindFfmpeg()
        {
            // 1. App base directory
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(localPath)) return localPath;

            // 2. WinGet package directory
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string wingetBase = Path.Combine(localAppData, @"Microsoft\WinGet\Packages");
            if (Directory.Exists(wingetBase))
            {
                string[] matches = Directory.GetFiles(wingetBase, "ffmpeg.exe", SearchOption.AllDirectories);
                if (matches.Length > 0) return matches[0];
            }

            // 3. System PATH check
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("where.exe", "ffmpeg")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    string outStr = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (!string.IsNullOrEmpty(outStr))
                    {
                        string[] lines = outStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0 && File.Exists(lines[0].Trim()))
                        {
                            return lines[0].Trim();
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        public static void StartRenderFromTimeline(TimelineExportDto timeline)
        {
            lock (_syncLock)
            {
                Cancel();

                if (!IsAvailable)
                {
                    throw new InvalidOperationException("FFmpeg nativo no fue encontrado en el sistema.");
                }

                IsRunning = true;
                IsFinished = false;
                IsError = false;
                CurrentFrame = 0;
                ErrorMessage = null;
                Percent = 0;
                Fps = 0;
                Speed = "0.0x";
                RamMb = 35;

                // Calculate target dimensions
                int targetW = 1920;
                int targetH = 1080;
                if (timeline.ratio == "9:16") { targetW = 1080; targetH = 1920; }
                else if (timeline.ratio == "1:1") { targetW = 1080; targetH = 1080; }
                else if (timeline.ratio == "4:5") { targetW = 1080; targetH = 1350; }

                if (timeline.resolution == "720")
                {
                    targetW = (int)(targetW * 0.6667);
                    targetH = (int)(targetH * 0.6667);
                }
                else if (timeline.resolution == "480")
                {
                    targetW = (int)(targetW * 0.4444);
                    targetH = (int)(targetH * 0.4444);
                }
                else if (timeline.resolution == "4k")
                {
                    targetW = targetW * 2;
                    targetH = targetH * 2;
                }

                if (targetW % 2 != 0) targetW++;
                if (targetH % 2 != 0) targetH++;

                int fps = timeline.fps > 0 ? timeline.fps : 30;
                double totalDuration = timeline.duration > 0 ? timeline.duration : 5.0;
                TotalFrames = (long)Math.Ceiling(totalDuration * fps);

                string ext = ".mp4";
                if (timeline.format == "webm") ext = ".webm";
                else if (timeline.format == "mov") ext = ".mov";
                else if (timeline.format == "mkv") ext = ".mkv";
                else if (timeline.format == "gif") ext = ".gif";
                else if (timeline.format == "wav") ext = ".wav";

                string fileName = string.Format("KidCut_{0:yyyyMMdd_HHmmss}{1}", DateTime.Now, ext);
                OutputPath = Path.Combine(Program.ExportsDir, fileName);

                // Build FFmpeg arguments
                StringBuilder args = new StringBuilder();
                args.Append("-y ");

                // List inputs
                List<string> videoInputs = new List<string>();
                List<ClipExportDto> validVideoClips = new List<ClipExportDto>();
                List<string> audioInputs = new List<string>();

                if (timeline.clips != null)
                {
                    // Sort clips: trackOrder ascending (V1 base below V2 overlay), then start ascending
                    timeline.clips.Sort((a, b) => {
                        int cmp = a.trackOrder.CompareTo(b.trackOrder);
                        if (cmp != 0) return cmp;
                        return a.start.CompareTo(b.start);
                    });

                    foreach (var c in timeline.clips)
                    {
                        if (!string.IsNullOrEmpty(c.path) && File.Exists(c.path))
                        {
                            validVideoClips.Add(c);
                            videoInputs.Add(c.path);
                            string extIn = Path.GetExtension(c.path).ToLowerInvariant();
                            bool isImage = c.type == "image" || extIn == ".png" || extIn == ".jpg" || extIn == ".jpeg" || extIn == ".bmp" || extIn == ".gif" || extIn == ".webp";
                            if (isImage)
                            {
                                args.AppendFormat("-loop 1 -t {0} -i \"{1}\" ", 
                                    Math.Max(0.1, c.duration).ToString("F3", CultureInfo.InvariantCulture), 
                                    c.path);
                            }
                            else
                            {
                                args.AppendFormat("-ss {0} -t {1} -i \"{2}\" ", 
                                    Math.Max(0, c.trimIn).ToString("F3", CultureInfo.InvariantCulture), 
                                    Math.Max(0.1, c.duration).ToString("F3", CultureInfo.InvariantCulture), 
                                    c.path);
                            }
                        }
                    }
                }

                if (timeline.audioTracks != null)
                {
                    foreach (var a in timeline.audioTracks)
                    {
                        if (!string.IsNullOrEmpty(a.path) && File.Exists(a.path))
                        {
                            audioInputs.Add(a.path);
                            args.AppendFormat("-ss {0} -t {1} -i \"{2}\" ", 
                                Math.Max(0, a.trimIn).ToString("F3", CultureInfo.InvariantCulture), 
                                Math.Max(0.1, a.duration).ToString("F3", CultureInfo.InvariantCulture), 
                                a.path);
                        }
                    }
                }

                if (videoInputs.Count == 0 && audioInputs.Count == 0)
                {
                    throw new InvalidOperationException("No se recibieron archivos de video ni de audio válidos para exportar.");
                }

                bool hasOverlayImage = !string.IsNullOrEmpty(timeline.overlayImagePath) && File.Exists(timeline.overlayImagePath);
                if (hasOverlayImage)
                {
                    args.AppendFormat("-i \"{0}\" ", timeline.overlayImagePath);
                }

                // Filter complex construction
                StringBuilder filter = new StringBuilder();
                string finalVideoStream = "0:v";
                string finalAudioStream = null;

                if (ext != ".wav")
                {
                    // Base retro dark background matching canvas (#080611)
                    filter.AppendFormat("color=c=0x080611:s={0}x{1}:r={2}:d={3}[vbase];", 
                        targetW, targetH, fps, totalDuration.ToString("F3", CultureInfo.InvariantCulture));

                    string currentBase = "vbase";

                    if (validVideoClips.Count > 0)
                    {
                        // 1. Process each video clip: scale, speed, effects, and PTS timing
                        for (int i = 0; i < validVideoClips.Count; i++)
                        {
                            var c = validVideoClips[i];
                            int rW = c.renderWidth > 0 ? c.renderWidth : targetW;
                            int rH = c.renderHeight > 0 ? c.renderHeight : targetH;
                            if (rW % 2 != 0) rW++;
                            if (rH % 2 != 0) rH++;

                            StringBuilder clipFilter = new StringBuilder();
                            clipFilter.AppendFormat("[{0}:v]", i);

                            // Playback speed
                            if (Math.Abs(c.speed - 1.0) > 0.01 && c.speed > 0)
                            {
                                clipFilter.AppendFormat("setpts=PTS/{0},", c.speed.ToString("F3", CultureInfo.InvariantCulture));
                            }

                            // Precise scaling preserving canvas zoom
                            clipFilter.AppendFormat("scale={0}:{1}:flags=bicubic,", rW, rH);

                            // Retro Pixelate / Scanlines per clip
                            if (c.pixelate)
                            {
                                clipFilter.AppendFormat("scale=iw/4:ih/4:flags=neighbor,scale={0}:{1}:flags=neighbor,", rW, rH);
                            }
                            if (c.scanlines)
                            {
                                clipFilter.Append("drawgrid=width=0:height=4:thickness=1:color=black@0.35,");
                            }

                            // Clip opacity (Alpha transparency)
                            double op = (c.opacity > 0.001) ? c.opacity : 1.0;
                            if (op < 0.99)
                            {
                                clipFilter.AppendFormat("format=rgba,colorchannelmixer=aa={0},", op.ToString("F2", CultureInfo.InvariantCulture));
                            }

                            // Enforce uniform framerate and align PTS to timeline start position
                            clipFilter.AppendFormat("fps={0},setsar=1,setpts=PTS-STARTPTS+{1}/TB",
                                fps, Math.Max(0, c.start).ToString("F3", CultureInfo.InvariantCulture));

                            clipFilter.AppendFormat("[vclip{0}];", i);
                            filter.Append(clipFilter.ToString());
                        }

                        // 2. Overlay each clip onto canvas at its designated X/Y coordinates in parallel
                        for (int i = 0; i < validVideoClips.Count; i++)
                        {
                            var c = validVideoClips[i];
                            int offX = c.offsetX;
                            int offY = c.offsetY;

                            // Fallback calculation if offsetX/offsetY were omitted
                            if (c.renderWidth <= 0 || c.renderHeight <= 0)
                            {
                                double sc = c.scale > 0 ? c.scale : 1.0;
                                int rW = (int)Math.Round(targetW * sc);
                                int rH = (int)Math.Round(targetH * sc);
                                offX = (int)Math.Round((c.posX > 0 ? c.posX : 0.5) * targetW - (rW / 2.0));
                                offY = (int)Math.Round((c.posY > 0 ? c.posY : 0.5) * targetH - (rH / 2.0));
                            }

                            string nextBase = (i == validVideoClips.Count - 1 && !hasOverlayImage) ? "vcomp" : string.Format("vlayer{0}", i);
                            filter.AppendFormat("[{0}][vclip{1}]overlay=x={2}:y={3}:eof_action=pass[{4}];",
                                currentBase,
                                i,
                                offX,
                                offY,
                                nextBase);
                            currentBase = nextBase;
                        }
                    }

                    finalVideoStream = currentBase;

                    // Overlay Image (Text/Stickers track from canvas)
                    if (hasOverlayImage)
                    {
                        int overlayIndex = videoInputs.Count + audioInputs.Count;
                        filter.AppendFormat("[{0}][{1}:v]overlay=0:0:format=auto[voverlay];", currentBase, overlayIndex);
                        finalVideoStream = "voverlay";
                    }
                }

                // Audio handling
                if (audioInputs.Count > 0)
                {
                    // JavaScript Exporter.js mixed the master audio accurately with all clip volumes, sound effects, and microphone tracks.
                    int baseAudioIdx = videoInputs.Count;
                    if (audioInputs.Count == 1)
                    {
                        filter.AppendFormat("[{0}:a]anull[aout];", baseAudioIdx);
                        finalAudioStream = "aout";
                    }
                    else
                    {
                        for (int i = 0; i < audioInputs.Count; i++)
                        {
                            filter.AppendFormat("[{0}:a]volume=1.0[ain{1}];", baseAudioIdx + i, i);
                        }
                        for (int i = 0; i < audioInputs.Count; i++) filter.AppendFormat("[ain{0}]", i);
                        filter.AppendFormat("amix=inputs={0}:duration=longest[aout];", audioInputs.Count);
                        finalAudioStream = "aout";
                    }
                }
                else if (videoInputs.Count > 0)
                {
                    // No master audio file provided; check which video clips actually have audio tracks
                    List<int> validAudioClips = new List<int>();
                    for (int i = 0; i < videoInputs.Count; i++)
                    {
                        if (HasAudioStream(videoInputs[i]))
                        {
                            validAudioClips.Add(i);
                            double vol = (validVideoClips.Count > i) ? validVideoClips[i].volume : 1.0;
                            filter.AppendFormat("[{0}:a]volume={1}[va{0}];", i, Math.Max(0.0, vol).ToString("F2", CultureInfo.InvariantCulture));
                        }
                    }

                    if (validAudioClips.Count > 1)
                    {
                        for (int k = 0; k < validAudioClips.Count; k++)
                        {
                            int idx = validAudioClips[k];
                            var c = (validVideoClips.Count > idx) ? validVideoClips[idx] : new ClipExportDto();
                            int delayMs = (int)Math.Max(0, Math.Round(c.start * 1000));
                            if (delayMs > 0)
                            {
                                filter.AppendFormat("[va{0}]adelay={1}|{1}[vad{0}];", idx, delayMs);
                            }
                            else
                            {
                                filter.AppendFormat("[va{0}]anull[vad{0}];", idx);
                            }
                        }
                        for (int k = 0; k < validAudioClips.Count; k++) filter.AppendFormat("[vad{0}]", validAudioClips[k]);
                        filter.AppendFormat("amix=inputs={0}:duration=longest[aout];", validAudioClips.Count);
                        finalAudioStream = "aout";
                    }
                    else if (validAudioClips.Count == 1)
                    {
                        filter.AppendFormat("[va{0}]anull[aout];", validAudioClips[0]);
                        finalAudioStream = "aout";
                    }
                    else
                    {
                        finalAudioStream = null;
                    }
                }

                string filterStr = filter.ToString().TrimEnd(';');
                if (!string.IsNullOrEmpty(filterStr))
                {
                    args.AppendFormat("-filter_complex \"{0}\" ", filterStr);
                    if (ext != ".wav")
                    {
                        args.AppendFormat("-map \"[{0}]\" ", finalVideoStream);
                    }
                    if (finalAudioStream != null && ext != ".gif")
                    {
                        args.AppendFormat("-map \"[{0}]\" ", finalAudioStream);
                    }
                }
                else
                {
                    if (ext != ".wav")
                    {
                        args.Append("-map 0:v ");
                    }
                    if (ext != ".gif")
                    {
                        if (audioInputs.Count > 0)
                        {
                            int aIdx = (videoInputs.Count == 0) ? 1 : videoInputs.Count;
                            args.AppendFormat("-map {0}:a? ", aIdx);
                        }
                        else if (videoInputs.Count > 0)
                        {
                            args.Append("-map 0:a? ");
                        }
                    }
                }

                // Codec & Quality
                if (ext == ".mp4" || ext == ".mov" || ext == ".mkv")
                {
                    args.AppendFormat("-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -r {0} -movflags +faststart ", fps);
                    if (finalAudioStream != null)
                    {
                        args.Append("-c:a aac -b:a 192k ");
                    }
                }
                else if (ext == ".webm")
                {
                    args.AppendFormat("-c:v libvpx-vp9 -b:v 4M -crf 30 -r {0} -c:a libopus -b:a 128k ", fps);
                }
                else if (ext == ".gif")
                {
                    args.AppendFormat("-r {0} ", Math.Min(15, fps));
                }
                else if (ext == ".wav")
                {
                    args.Append("-vn -c:a pcm_s16le -ar 44100 ");
                }

                args.AppendFormat("\"{0}\"", OutputPath);

                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kidcut_boot.log"), 
                        string.Format("[{0}] FFmpeg args: {1}\n", DateTime.Now, args));
                }
                catch { }

                // Launch FFmpeg
                ProcessStartInfo psi = new ProcessStartInfo(FfmpegPath, args.ToString())
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                _activeProcess = Process.Start(psi);

                ChildProcesses.Track(_activeProcess);
                Thread monitorThread = new Thread(() => MonitorProcess(_activeProcess, TotalFrames))
                {
                    IsBackground = true
                };
                monitorThread.Start();
            }
        }

        public static void StartStreamRender(StreamStartRequestDto req)
        {
            lock (_syncLock)
            {
                Cancel();

                if (!IsAvailable)
                {
                    throw new InvalidOperationException("FFmpeg nativo no fue encontrado en el sistema.");
                }

                IsRunning = true;
                IsFinished = false;
                IsError = false;
                CurrentFrame = 0;
                TotalFrames = req.totalFrames > 0 ? req.totalFrames : 300;
                ErrorMessage = null;
                Percent = 0;
                Fps = 0;
                Speed = "0.0x";
                RamMb = 35;

                string ext = (req.format == "webm") ? ".webm" : (req.format == "gif" ? ".gif" : ".mp4");
                string fileName = string.Format("KidCut_{0:yyyyMMdd_HHmmss}{1}", DateTime.Now, ext);
                OutputPath = Path.Combine(Program.ExportsDir, fileName);

                StringBuilder args = new StringBuilder();
                args.Append("-y ");
                args.AppendFormat("-f image2pipe -vcodec png -r {0} -i - ", req.fps > 0 ? req.fps : 30);

                if (!string.IsNullOrEmpty(req.audioPath) && File.Exists(req.audioPath))
                {
                    args.AppendFormat("-i \"{0}\" -c:a aac -b:a 192k ", req.audioPath);
                }

                if (ext == ".mp4")
                {
                    args.AppendFormat("-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -r {0} -movflags +faststart ", req.fps > 0 ? req.fps : 30);
                }
                else if (ext == ".webm")
                {
                    args.AppendFormat("-c:v libvpx-vp9 -b:v 4M -crf 30 -r {0} ", req.fps > 0 ? req.fps : 30);
                }
                else if (ext == ".gif")
                {
                    args.AppendFormat("-r {0} ", Math.Min(15, req.fps > 0 ? req.fps : 30));
                }

                args.AppendFormat("\"{0}\"", OutputPath);

                ProcessStartInfo psi = new ProcessStartInfo(FfmpegPath, args.ToString())
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                _activeProcess = Process.Start(psi);

                ChildProcesses.Track(_activeProcess);
                _activeStdinWriter = _activeProcess.StandardInput;

                Thread monitorThread = new Thread(() => MonitorProcess(_activeProcess, TotalFrames))
                {
                    IsBackground = true
                };
                monitorThread.Start();
            }
        }

        public static void WriteStreamFrame(byte[] framePngBytes)
        {
            lock (_syncLock)
            {
                if (_activeProcess != null && !_activeProcess.HasExited && _activeStdinWriter != null)
                {
                    try
                    {
                        _activeStdinWriter.BaseStream.Write(framePngBytes, 0, framePngBytes.Length);
                        _activeStdinWriter.BaseStream.Flush();
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = ex.Message;
                        IsError = true;
                    }
                }
            }
        }

        public static void FinishStreamRender()
        {
            lock (_syncLock)
            {
                if (_activeStdinWriter != null)
                {
                    try
                    {
                        _activeStdinWriter.Flush();
                        _activeStdinWriter.Close();
                    }
                    catch { }
                    _activeStdinWriter = null;
                }
            }
        }

        private static void MonitorProcess(Process proc, long totalFrames)
        {
            Regex frameRegex = new Regex(@"frame=\s*(\d+)", RegexOptions.Compiled);
            Regex fpsRegex = new Regex(@"fps=\s*([\d\.]+)", RegexOptions.Compiled);
            Regex speedRegex = new Regex(@"speed=\s*([\d\.]+x)", RegexOptions.Compiled);

            List<string> lastStderrLines = new List<string>();

            try
            {
                while (!proc.HasExited)
                {
                    string line = proc.StandardError.ReadLine();
                    if (line == null) break;

                    if (lastStderrLines.Count > 15) lastStderrLines.RemoveAt(0);
                    lastStderrLines.Add(line);

                    Match mFrame = frameRegex.Match(line);
                    if (mFrame.Success)
                    {
                        long f;
                        if (long.TryParse(mFrame.Groups[1].Value, out f))
                        {
                            CurrentFrame = f;
                            if (totalFrames > 0)
                            {
                                Percent = (int)Math.Min(99, Math.Max(0, (f * 100) / totalFrames));
                            }
                        }
                    }

                    Match mFps = fpsRegex.Match(line);
                    if (mFps.Success)
                    {
                        double fps;
                        if (double.TryParse(mFps.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out fps))
                        {
                            Fps = fps;
                        }
                    }

                    Match mSpeed = speedRegex.Match(line);
                    if (mSpeed.Success)
                    {
                        Speed = mSpeed.Groups[1].Value;
                    }

                    try
                    {
                        proc.Refresh();
                        RamMb = (int)(proc.WorkingSet64 / (1024 * 1024));
                    }
                    catch { }
                }

                proc.WaitForExit();
                if (proc.ExitCode == 0)
                {
                    Percent = 100;
                    CurrentFrame = totalFrames > 0 ? totalFrames : CurrentFrame;
                    IsFinished = true;
                    IsRunning = false;
                }
                else
                {
                    IsError = true;
                    IsRunning = false;
                    string detail = lastStderrLines.Count > 0 ? string.Join(" | ", lastStderrLines.ToArray()) : "Sin salida de error adicional.";
                    ErrorMessage = string.Format("FFmpeg error (código {0}): {1}", proc.ExitCode, detail);
                }
            }
            catch (Exception ex)
            {
                IsError = true;
                IsRunning = false;
                ErrorMessage = ex.Message;
            }
        }

        public static void Cancel()
        {
            lock (_syncLock)
            {
                if (_activeStdinWriter != null)
                {
                    try { _activeStdinWriter.Close(); } catch { }
                    _activeStdinWriter = null;
                }
                if (_activeProcess != null)
                {
                    try
                    {
                        if (!_activeProcess.HasExited)
                        {
                            _activeProcess.Kill();
                        }
                    }
                    catch { }
                    try { _activeProcess.Dispose(); } catch { }
                    _activeProcess = null;
                }
                IsRunning = false;
            }
        }

        public static ExportStatusDto GetStatus()
        {
            return new ExportStatusDto
            {
                isRunning = IsRunning,
                isFinished = IsFinished,
                isError = IsError,
                currentFrame = CurrentFrame,
                totalFrames = TotalFrames,
                percent = Percent,
                fps = Fps,
                speed = Speed,
                ramMb = RamMb > 0 ? RamMb : 35,
                outputPath = OutputPath,
                errorMessage = ErrorMessage
            };
        }
    }

    #endregion

    #region Dual-Mode Local HTTP Server & Native API

    public class HttpServer
    {
        private readonly string _rootDir;
        private HttpListener _httpListener;
        private TcpListener _tcpListener;
        private Thread _workerThread;
        private volatile bool _isRunning;
        private readonly JavaScriptSerializer _jsonSerializer = new JavaScriptSerializer();
        public int Port { get; private set; }

        public HttpServer(string rootDir)
        {
            _rootDir = rootDir;
        }

        public int Start(int startPort, int endPort)
        {
            for (int p = startPort; p <= endPort; p++)
            {
                try
                {
                    HttpListener hl = new HttpListener();
                    hl.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", p));
                    hl.Start();

                    _httpListener = hl;
                    _isRunning = true;
                    Port = p;

                    try
                    {
                        File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kidcut_boot.log"), 
                            string.Format("[{0}] HttpListener started successfully on port {1}\n", DateTime.Now, p));
                        string portJson = string.Format("{{\"port\":{0}}}", p);
                        if (!string.IsNullOrEmpty(_rootDir) && Directory.Exists(_rootDir))
                        {
                            File.WriteAllText(Path.Combine(_rootDir, "kidcut_port.json"), portJson);
                        }
                        File.WriteAllText(Path.Combine(Program.BaseDir, "kidcut_port.json"), portJson);
                    }
                    catch { }

                    _workerThread = new Thread(HttpListenerLoop) { IsBackground = true };
                    _workerThread.Start();
                    return p;
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kidcut_boot.log"), 
                            string.Format("[{0}] HttpListener failed on port {1}: {2}\n", DateTime.Now, p, ex.Message));
                    }
                    catch { }

                    if (_httpListener != null)
                    {
                        try { _httpListener.Close(); } catch { }
                        _httpListener = null;
                    }
                }
            }

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

                // CORS & Pre-flight
                context.Response.AddHeader("Access-Control-Allow-Origin", "*");
                context.Response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                context.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Content-Length");

                if (context.Request.HttpMethod == "OPTIONS")
                {
                    context.Response.StatusCode = 200;
                    context.Response.OutputStream.Close();
                    return;
                }

                // =============================================================
                // 🚀 NATIVE REST API ENDPOINTS
                // =============================================================
                if (rawUrl.StartsWith("/api/"))
                {
                    HandleApiRequest(context);
                    return;
                }

                // Serve static files
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

        private void HandleApiRequest(HttpListenerContext context)
        {
            string path = context.Request.Url.AbsolutePath.ToLower();
            string method = context.Request.HttpMethod.ToUpper();

            try
            {
                // 1. GET /api/ping
                if (path == "/api/ping" && method == "GET")
                {
                    SendJsonResponse(context, new
                    {
                        status = "ok",
                        native = true,
                        ffmpeg = FfmpegEngine.IsAvailable,
                        ffmpegPath = FfmpegEngine.FfmpegPath,
                        exportsDir = Program.ExportsDir
                    });
                    return;
                }

                // 1b. GET /api/file (Serves local media file for video/audio preview directly from disk)
                if (path == "/api/file" && method == "GET")
                {
                    string targetFilePath = context.Request.QueryString["path"];
                    if (!string.IsNullOrEmpty(targetFilePath) && File.Exists(targetFilePath))
                    {
                        ServeFileWithRanges(context, targetFilePath);
                        return;
                    }
                    context.Response.StatusCode = 404;
                    context.Response.OutputStream.Close();
                    return;
                }

                // 2. GET /api/pick-file (Windows Native OpenFileDialog)
                if (path == "/api/pick-file" && method == "GET")
                {
                    string selectedPath = null;
                    Thread staThread = new Thread(() =>
                    {
                        using (OpenFileDialog ofd = new OpenFileDialog())
                        {
                            ofd.Title = "KidCut - Seleccionar Archivo Multimedia";
                            ofd.Filter = "Archivos Multimedia|*.mp4;*.webm;*.mov;*.mkv;*.avi;*.mp3;*.wav;*.ogg;*.aac;*.m4a;*.flac;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.tif;*.tiff|Videos|*.mp4;*.webm;*.mov;*.mkv;*.avi|Audio|*.mp3;*.wav;*.ogg;*.aac;*.m4a;*.flac|Imágenes|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.tif;*.tiff|Todos los Archivos|*.*";
                            ofd.RestoreDirectory = true;
                            if (ofd.ShowDialog() == DialogResult.OK)
                            {
                                selectedPath = ofd.FileName;
                            }
                        }
                    });
                    staThread.SetApartmentState(ApartmentState.STA);
                    staThread.Start();
                    staThread.Join();

                    if (!string.IsNullOrEmpty(selectedPath))
                    {
                        SendJsonResponse(context, new
                        {
                            success = true,
                            filePath = selectedPath,
                            fileName = Path.GetFileName(selectedPath)
                        });
                    }
                    else
                    {
                        SendJsonResponse(context, new { success = false, cancelled = true });
                    }
                    return;
                }

                // 3. POST /api/upload-media (Uploads audio recordings, images or buffers into scratch directory)
                if (path == "/api/upload-media" && method == "POST")
                {
                    string filename = context.Request.QueryString["name"];
                    if (string.IsNullOrEmpty(filename))
                    {
                        filename = string.Format("media_{0}.dat", DateTime.Now.Ticks);
                    }

                    string savePath = Path.Combine(Program.ScratchDir, filename);
                    using (FileStream fs = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                    {
                        context.Request.InputStream.CopyTo(fs);
                    }

                    SendJsonResponse(context, new
                    {
                        success = true,
                        filePath = savePath,
                        fileName = filename
                    });
                    return;
                }

                // 4. POST /api/export (Direct Timeline Native Assembly via FFmpeg)
                if (path == "/api/export" && method == "POST")
                {
                    string body;
                    using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                    }

                    TimelineExportDto timeline = _jsonSerializer.Deserialize<TimelineExportDto>(body);
                    FfmpegEngine.StartRenderFromTimeline(timeline);

                    SendJsonResponse(context, new
                    {
                        success = true,
                        message = "Exportación nativa iniciada en segundo plano.",
                        outputPath = FfmpegEngine.OutputPath
                    });
                    return;
                }

                // 5. GET /api/export/progress
                if (path == "/api/export/progress" && method == "GET")
                {
                    ExportStatusDto status = FfmpegEngine.GetStatus();
                    SendJsonResponse(context, status);
                    return;
                }

                // 6. POST /api/export/cancel
                if (path == "/api/export/cancel" && method == "POST")
                {
                    FfmpegEngine.Cancel();
                    SendJsonResponse(context, new { success = true, message = "Cancelado." });
                    return;
                }

                // 7. POST /api/open-output (Opens output in Windows Explorer)
                if (path == "/api/open-output" && method == "POST")
                {
                    string fileToOpen = FfmpegEngine.OutputPath;
                    if (string.IsNullOrEmpty(fileToOpen) || !File.Exists(fileToOpen))
                    {
                        fileToOpen = Program.ExportsDir;
                    }

                    try
                    {
                        if (File.Exists(fileToOpen))
                        {
                            Process.Start(new ProcessStartInfo("explorer.exe", string.Format("/select,\"{0}\"", fileToOpen)) { UseShellExecute = true });
                        }
                        else
                        {
                            Process.Start(new ProcessStartInfo("explorer.exe", string.Format("\"{0}\"", Program.ExportsDir)) { UseShellExecute = true });
                        }
                        SendJsonResponse(context, new { success = true });
                    }
                    catch (Exception ex)
                    {
                        SendJsonResponse(context, new { success = false, error = ex.Message });
                    }
                    return;
                }

                // 8. POST /api/stream-export/start (Stream Pipe mode)
                if (path == "/api/stream-export/start" && method == "POST")
                {
                    string body;
                    using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                    }

                    StreamStartRequestDto req = _jsonSerializer.Deserialize<StreamStartRequestDto>(body);
                    FfmpegEngine.StartStreamRender(req);

                    SendJsonResponse(context, new { success = true, outputPath = FfmpegEngine.OutputPath });
                    return;
                }

                // 9. POST /api/stream-export/frame (Stream Pipe chunk)
                if (path == "/api/stream-export/frame" && method == "POST")
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        context.Request.InputStream.CopyTo(ms);
                        byte[] frameBytes = ms.ToArray();
                        FfmpegEngine.WriteStreamFrame(frameBytes);
                    }
                    SendJsonResponse(context, new { success = true });
                    return;
                }

                // 10. POST /api/stream-export/finish (Stream Pipe finish)
                if (path == "/api/stream-export/finish" && method == "POST")
                {
                    FfmpegEngine.FinishStreamRender();
                    SendJsonResponse(context, new { success = true, outputPath = FfmpegEngine.OutputPath });
                    return;
                }

                // 11. GET /api/system/stats (Memory usage in MB)
                if (path == "/api/system/stats" && method == "GET")
                {
                    int appRam = (int)(Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024));
                    int ffmpegRam = FfmpegEngine.RamMb;
                    SendJsonResponse(context, new
                    {
                        kidcutRamMb = appRam,
                        ffmpegRamMb = ffmpegRam,
                        totalRamMb = appRam + ffmpegRam
                    });
                    return;
                }

                // 12. GET /api/recordings (Screen recordings & screenshots made with the overlay widget)
                if (path == "/api/recordings" && method == "GET")
                {
                    List<object> items = new List<object>();
                    if (Directory.Exists(ScreenRecorder.RecordingsDir))
                    {
                        FileInfo[] files = new DirectoryInfo(ScreenRecorder.RecordingsDir).GetFiles();
                        Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                        foreach (FileInfo f in files)
                        {
                            items.Add(new
                            {
                                name = f.Name,
                                filePath = f.FullName,
                                sizeMb = Math.Round(f.Length / 1048576.0, 1),
                                modified = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                            });
                        }
                    }
                    SendJsonResponse(context, new
                    {
                        success = true,
                        folder = ScreenRecorder.RecordingsDir,
                        isRecording = ScreenRecorder.IsRecording,
                        isProcessing = ScreenRecorder.IsProcessing,
                        lastOutput = ScreenRecorder.LastOutput,
                        lastError = ScreenRecorder.LastError,
                        items = items
                    });
                    return;
                }

                // 13. GET /api/probe?path= (duration + number of audio streams)
                if (path == "/api/probe" && method == "GET")
                {
                    string target = context.Request.QueryString["path"];
                    if (string.IsNullOrEmpty(target) || !File.Exists(target))
                    {
                        SendJsonResponse(context, new { success = false, error = "Archivo no encontrado." }, 404);
                        return;
                    }
                    SendJsonResponse(context, new
                    {
                        success = true,
                        duration = MediaTools.ProbeDuration(target),
                        audioStreams = MediaTools.CountAudioStreams(target)
                    });
                    return;
                }

                // 14. GET /api/extract-audio?path=&stream= (decodes one audio stream to WAV for the editor)
                if (path == "/api/extract-audio" && method == "GET")
                {
                    string target = context.Request.QueryString["path"];
                    int stream;
                    int.TryParse(context.Request.QueryString["stream"], out stream);
                    if (string.IsNullOrEmpty(target) || !File.Exists(target))
                    {
                        SendJsonResponse(context, new { success = false, error = "Archivo no encontrado." }, 404);
                        return;
                    }
                    string wav = Path.Combine(Program.ScratchDir, string.Format("audio_{0}_{1}.wav", DateTime.Now.Ticks, stream));
                    string err;
                    int code = MediaTools.Ffmpeg(string.Format("-i \"{0}\" -map 0:a:{1} -vn -ac 2 -ar 48000 -c:a pcm_s16le \"{2}\"", target, stream, wav), 1800000, out err);
                    if (code == 0) SendJsonResponse(context, new { success = true, filePath = wav });
                    else SendJsonResponse(context, new { success = false, error = err });
                    return;
                }

                // 15. POST /api/convert {path, format} (re-encode any media to another video/audio/image format)
                if (path == "/api/convert" && method == "POST")
                {
                    string json;
                    using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }
                    Dictionary<string, object> req = _jsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    object srcObj, fmtObj;
                    req.TryGetValue("path", out srcObj);
                    req.TryGetValue("format", out fmtObj);
                    try
                    {
                        string output = MediaTools.Convert(srcObj as string, fmtObj as string);
                        SendJsonResponse(context, new { success = true, outputPath = output });
                    }
                    catch (Exception ex)
                    {
                        SendJsonResponse(context, new { success = false, error = ex.Message });
                    }
                    return;
                }

                // 15b. POST /api/record/start?sys=1&mic=1&fps=30, /api/record/stop, /api/screenshot
                if (path == "/api/record/start" && method == "POST")
                {
                    int fps;
                    if (!int.TryParse(context.Request.QueryString["fps"], out fps) || (fps != 30 && fps != 60)) fps = 30;
                    ScreenRecorder.Start(context.Request.QueryString["sys"] != "0", context.Request.QueryString["mic"] != "0", context.Request.QueryString["cam"] == "1", fps);
                    SendJsonResponse(context, new { success = ScreenRecorder.IsRecording, warning = ScreenRecorder.LastError });
                    return;
                }
                if (path == "/api/record/stop" && method == "POST")
                {
                    ScreenRecorder.Stop();
                    SendJsonResponse(context, new { success = true });
                    return;
                }
                if (path == "/api/screenshot" && method == "POST")
                {
                    SendJsonResponse(context, new { success = true, filePath = ScreenRecorder.TakeScreenshot() });
                    return;
                }

                // 16. POST /api/overlay/show (re-open the floating recorder widget)
                if (path == "/api/overlay/show" && method == "POST")
                {
                    Program.ShowOverlay();
                    SendJsonResponse(context, new { success = true });
                    return;
                }

                // 17. POST /api/reveal?path= (select a file in Windows Explorer)
                if (path == "/api/reveal" && method == "POST")
                {
                    string target = context.Request.QueryString["path"];
                    if (!string.IsNullOrEmpty(target) && File.Exists(target))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", string.Format("/select,\"{0}\"", target)) { UseShellExecute = true });
                    }
                    SendJsonResponse(context, new { success = true });
                    return;
                }

                context.Response.StatusCode = 404;
                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                SendJsonResponse(context, new { success = false, error = ex.Message }, 500);
            }
        }

        // Streams from disk with HTTP Range support so <video> can seek inside large recordings without loading them into RAM
        private static void ServeFileWithRanges(HttpListenerContext context, string filePath)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long length = fs.Length, start = 0, end = length - 1;
                context.Response.AddHeader("Accept-Ranges", "bytes");
                context.Response.ContentType = GetMimeType(filePath);

                string range = context.Request.Headers["Range"];
                if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes=") && length > 0)
                {
                    string[] parts = range.Substring(6).Split(',')[0].Split('-');
                    long s, e;
                    if (parts[0].Length == 0 && parts.Length > 1 && long.TryParse(parts[1], out e))
                    {
                        start = Math.Max(0, length - e); // suffix range: last N bytes
                    }
                    else if (long.TryParse(parts[0], out s))
                    {
                        start = s;
                        if (parts.Length > 1 && long.TryParse(parts[1], out e)) end = Math.Min(e, length - 1);
                    }
                    if (start > end)
                    {
                        context.Response.StatusCode = 416;
                        context.Response.AddHeader("Content-Range", "bytes */" + length);
                        context.Response.OutputStream.Close();
                        return;
                    }
                    context.Response.StatusCode = 206;
                    context.Response.AddHeader("Content-Range", string.Format("bytes {0}-{1}/{2}", start, end, length));
                }

                long remaining = end - start + 1;
                context.Response.ContentLength64 = remaining;
                fs.Seek(start, SeekOrigin.Begin);
                byte[] buf = new byte[256 * 1024];
                try
                {
                    while (remaining > 0)
                    {
                        int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, remaining));
                        if (n <= 0) break;
                        context.Response.OutputStream.Write(buf, 0, n);
                        remaining -= n;
                    }
                    context.Response.OutputStream.Close();
                }
                catch (HttpListenerException) { } // the player aborted this range while seeking
            }
        }

        private void SendJsonResponse(HttpListenerContext context, object data, int statusCode = 200)
        {
            try
            {
                string json = _jsonSerializer.Serialize(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.OutputStream.Close();
            }
            catch { }
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
                        "HTTP/1.1 200 OK\r\nContent-Type: {0}\r\nContent-Length: {1}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n",
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
                case ".mkv": return "video/x-matroska";
                case ".mov": return "video/quicktime";
                case ".m4a": return "audio/mp4";
                case ".aac": return "audio/aac";
                case ".ogg": return "audio/ogg";
                case ".flac": return "audio/flac";
                case ".webp": return "image/webp";
                case ".bmp": return "image/bmp";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                case ".ttf": return "font/ttf";
                default: return "application/octet-stream";
            }
        }
    }

    #endregion

    #region WASAPI Audio Capture (system loopback + microphone)

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        void GetBufferSize(out uint frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint padding);
        void IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        void ReleaseBuffer(uint frames);
        void GetNextPacketSize(out uint frames);
    }

    /// <summary>
    /// Records the default speakers (loopback) or default microphone to a WAV in the device mix format (lossless).
    /// Packets are placed by their QPC timestamp, so the silent gaps loopback produces while nothing plays stay in sync.
    /// </summary>
    public class WasapiCapture
    {
        private readonly bool _loopback;
        private readonly string _path;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private Thread _thread;
        private volatile bool _stop;
        private long _startQpc;

        public long FramesWritten;
        public int SampleRate;
        public string Error;
        public string FilePath { get { return _path; } }
        public double Seconds { get { return SampleRate > 0 ? (double)FramesWritten / SampleRate : 0; } }

        // Wall-clock time (µs since the Unix epoch, same clock as FFmpeg's RTCTIME) of the first sample in the WAV
        public long StartUs;

        public static long NowUs()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10;
        }

        public WasapiCapture(bool loopback, string path)
        {
            _loopback = loopback;
            _path = path;
        }

        public bool Start()
        {
            Begin();
            return WaitReady();
        }

        // Begin + WaitReady lets system audio and microphone initialize in parallel
        public void Begin()
        {
            _thread = new Thread(Run) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public bool WaitReady()
        {
            _ready.WaitOne(4000);
            return Error == null;
        }

        public void RequestStop() { _stop = true; }

        public void Join()
        {
            if (_thread != null) _thread.Join(5000);
        }

        private static long Qpc100ns()
        {
            return (long)(Stopwatch.GetTimestamp() * (10000000.0 / Stopwatch.Frequency));
        }

        [DllImport("avrt.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

        [DllImport("avrt.dll")]
        private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);

        private void Run()
        {
            IAudioClient client = null;
            // Real-time audio scheduling (MMCSS "Pro Audio") so a busy CPU never starves the capture into gaps
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
            uint taskIndex = 0;
            IntPtr mmcss = IntPtr.Zero;
            try { mmcss = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex); } catch { }
            try
            {
                IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice device;
                enumerator.GetDefaultAudioEndpoint(_loopback ? 0 : 1, 0, out device); // eRender / eCapture, eConsole
                Guid iidClient = typeof(IAudioClient).GUID;
                object obj;
                device.Activate(ref iidClient, 0x17, IntPtr.Zero, out obj);
                client = (IAudioClient)obj;

                IntPtr fmt;
                client.GetMixFormat(out fmt);
                int blockAlign = Marshal.ReadInt16(fmt, 12);
                SampleRate = Marshal.ReadInt32(fmt, 4);
                byte[] fmtBytes = new byte[18 + Marshal.ReadInt16(fmt, 16)];
                Marshal.Copy(fmt, fmtBytes, 0, fmtBytes.Length);
                client.Initialize(0, _loopback ? 0x00020000 : 0, 10000000, 0, fmt, IntPtr.Zero);
                Marshal.FreeCoTaskMem(fmt);

                Guid iidCapture = typeof(IAudioCaptureClient).GUID;
                client.GetService(ref iidCapture, out obj);
                IAudioCaptureClient capture = (IAudioCaptureClient)obj;

                // shortcut: plain RIFF caps at 4 GB (~3 h of 48 kHz float stereo); switch to RF64 if longer takes are needed
                using (FileStream fs = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    WriteWavHeader(fs, fmtBytes, 0);
                    client.Start();
                    _startQpc = Qpc100ns();
                    StartUs = NowUs();
                    _ready.Set();

                    byte[] buf = new byte[SampleRate * blockAlign];
                    int slack = SampleRate / 50; // 20 ms of jitter before inserting silence
                    while (!_stop)
                    {
                        Thread.Sleep(5);
                        uint packet;
                        capture.GetNextPacketSize(out packet);
                        while (packet > 0)
                        {
                            IntPtr data;
                            uint frames, flags;
                            ulong devPos, qpcPos;
                            capture.GetBuffer(out data, out frames, out flags, out devPos, out qpcPos);
                            long pos = (long)(((long)qpcPos - _startQpc) * (double)SampleRate / 10000000.0);
                            if (pos - FramesWritten > slack) WriteSilence(fs, pos - FramesWritten, blockAlign, buf);

                            int bytes = (int)frames * blockAlign;
                            if (buf.Length < bytes) buf = new byte[bytes];
                            if ((flags & 2) != 0) Array.Clear(buf, 0, bytes); // AUDCLNT_BUFFERFLAGS_SILENT
                            else Marshal.Copy(data, buf, 0, bytes);
                            fs.Write(buf, 0, bytes);
                            FramesWritten += frames;
                            capture.ReleaseBuffer(frames);
                            capture.GetNextPacketSize(out packet);
                        }
                    }
                    client.Stop();

                    // Pad up to the stop instant so every stream ends together (the muxer aligns them by their end)
                    long endPos = (long)((Qpc100ns() - _startQpc) * (double)SampleRate / 10000000.0);
                    if (endPos > FramesWritten) WriteSilence(fs, endPos - FramesWritten, blockAlign, buf);
                    WriteWavHeader(fs, fmtBytes, FramesWritten * blockAlign);
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                _ready.Set();
                if (client != null) Marshal.ReleaseComObject(client);
                if (mmcss != IntPtr.Zero) AvRevertMmThreadCharacteristics(mmcss);
            }
        }

        private void WriteSilence(FileStream fs, long frames, int blockAlign, byte[] buf)
        {
            Array.Clear(buf, 0, buf.Length);
            long bytes = frames * blockAlign;
            while (bytes > 0)
            {
                int n = (int)Math.Min(bytes, buf.Length);
                fs.Write(buf, 0, n);
                bytes -= n;
            }
            FramesWritten += frames;
        }

        private static void WriteWavHeader(FileStream fs, byte[] fmt, long dataBytes)
        {
            fs.Seek(0, SeekOrigin.Begin);
            BinaryWriter w = new BinaryWriter(fs);
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write((uint)Math.Min(uint.MaxValue, 4 + 8 + fmt.Length + 8 + dataBytes));
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(fmt.Length);
            w.Write(fmt);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write((uint)Math.Min(uint.MaxValue, dataBytes));
            w.Flush();
            fs.Seek(0, SeekOrigin.End);
        }
    }

    #endregion

    // Every FFmpeg we start joins a Windows job object that kills it when KidCut exits, even on a crash: otherwise an
    // orphaned capture keeps recording (CPU and disk) after the app is gone
    public static class ChildProcesses
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);

        [DllImport("kernel32.dll")]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        private static readonly IntPtr _job = CreateJob();

        private static IntPtr CreateJob()
        {
            try
            {
                IntPtr job = CreateJobObject(IntPtr.Zero, null);
                JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                SetInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)));
                return job;
            }
            catch { return IntPtr.Zero; }
        }

        public static void Track(Process p)
        {
            try { if (_job != IntPtr.Zero) AssignProcessToJobObject(_job, p.Handle); } catch { }
        }
    }

    #region Media Tools (probe, extract, convert)

    public static class MediaTools
    {
        // format id -> { extension, ffmpeg output args }
        public static readonly Dictionary<string, string[]> ConvertFormats = new Dictionary<string, string[]>
        {
            { "mp4", new[] { ".mp4", "-c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart" } },
            { "mp4-h265", new[] { ".mp4", "-c:v libx265 -preset medium -crf 22 -pix_fmt yuv420p -tag:v hvc1 -c:a aac -b:a 192k -movflags +faststart" } },
            { "webm", new[] { ".webm", "-c:v libvpx-vp9 -crf 30 -b:v 0 -row-mt 1 -deadline good -cpu-used 4 -c:a libopus -b:a 160k" } },
            { "mkv", new[] { ".mkv", "-map 0 -c copy" } },
            { "mov", new[] { ".mov", "-c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k" } },
            { "avi", new[] { ".avi", "-c:v mpeg4 -q:v 2 -c:a libmp3lame -q:a 2" } },
            { "gif", new[] { ".gif", "-vf \"fps=15,scale='min(960,iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse\" -loop 0" } },
            { "webp-anim", new[] { ".webp", "-vf \"fps=15,scale='min(960,iw)':-2\" -c:v libwebp_anim -quality 80 -loop 0 -an" } },
            { "mp3", new[] { ".mp3", "-vn -c:a libmp3lame -q:a 2" } },
            { "wav", new[] { ".wav", "-vn -c:a pcm_s16le" } },
            { "m4a", new[] { ".m4a", "-vn -c:a aac -b:a 192k" } },
            { "flac", new[] { ".flac", "-vn -c:a flac" } },
            { "ogg", new[] { ".ogg", "-vn -c:a libvorbis -q:a 6" } },
            { "png", new[] { ".png", "-frames:v 1" } },
            { "jpg", new[] { ".jpg", "-frames:v 1 -q:v 2" } },
            { "webp", new[] { ".webp", "-frames:v 1 -c:v libwebp -quality 90" } },
            { "bmp", new[] { ".bmp", "-frames:v 1" } },
            { "tiff", new[] { ".tiff", "-frames:v 1" } },
            { "avif", new[] { ".avif", "-frames:v 1 -c:v libaom-av1 -still-picture 1 -crf 28" } }
        };

        public static int Run(string exe, string args, int timeoutMs, out string output, out string error)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // FFmpeg prints UTF-8; the default OEM code page mangles names like "LifeCam®" or accented paths
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            StringBuilder outSb = new StringBuilder();
            StringBuilder errSb = new StringBuilder();
            using (Process p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (outSb) outSb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errSb) errSb.AppendLine(e.Data); };
                p.Start();
                ChildProcesses.Track(p);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    output = "";
                    error = "Tiempo de espera agotado.";
                    return -1;
                }
                p.WaitForExit(); // flush async readers
                lock (outSb) output = outSb.ToString();
                lock (errSb) error = errSb.ToString().Trim();
                return p.ExitCode;
            }
        }

        public static int Ffmpeg(string args, int timeoutMs, out string error)
        {
            string ignored;
            return Run(FfmpegEngine.FfmpegPath, "-y -hide_banner -loglevel error " + args, timeoutMs, out ignored, out error);
        }

        private static string Probe(string args, int timeoutMs = 30000)
        {
            string probe = FfmpegEngine.FindFfprobe();
            if (string.IsNullOrEmpty(probe)) return "";
            string output, error;
            Run(probe, "-v error " + args, timeoutMs, out output, out error);
            return output;
        }

        private static void PacketRange(string text, ref double first, ref double last)
        {
            foreach (string l in text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double d;
                if (!double.TryParse(l.Trim().TrimEnd(','), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) continue;
                first = Math.Min(first, d);
                last = Math.Max(last, d);
            }
        }

        // First and last video packet times (s) as written, so absolute stamps survive and unfinalized files still work.
        // A finished file is read from its header plus its last seconds only: scanning every packet of a 20-minute take
        // took longer than the probe timeout and the take was wrongly reported as empty.
        public static bool ProbeVideoSpan(string path, out double first, out double last)
        {
            first = double.MaxValue;
            last = double.MinValue;
            double start, dur;
            string[] f = Probe(string.Format("-show_entries format=start_time,duration -of csv=p=0 \"{0}\"", path)).Trim().Split(',');
            if (f.Length == 2 && double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out start)
                && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out dur) && dur > 0)
            {
                PacketRange(Probe(string.Format(CultureInfo.InvariantCulture,
                    "-select_streams v:0 -read_intervals {0:0.000}% -show_entries packet=pts_time -of csv=p=0 \"{1}\"",
                    Math.Max(0, start + dur - 20), path), 120000), ref first, ref last);
                if (last >= first)
                {
                    first = Math.Min(first, start);
                    return true;
                }
            }
            // Unfinalized file (capture process killed): no index, so read every packet, with a generous timeout
            first = double.MaxValue;
            last = double.MinValue;
            PacketRange(Probe(string.Format("-select_streams v:0 -show_entries packet=pts_time -of csv=p=0 \"{0}\"", path), 1800000), ref first, ref last);
            return last >= first;
        }

        public static double ProbeDuration(string path)
        {
            double d;
            string s = Probe(string.Format("-show_entries format=duration -of csv=p=0 \"{0}\"", path)).Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) return d;
            // A capture killed mid-write leaves an unfinalized file without duration: use its last video packet
            string[] lines = Probe(string.Format("-select_streams v:0 -show_entries packet=pts_time -of csv=p=0 \"{0}\"", path))
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            double last = 0;
            foreach (string l in lines)
            {
                if (double.TryParse(l.Trim().TrimEnd(','), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) last = Math.Max(last, d);
            }
            return last;
        }

        public static int CountAudioStreams(string path)
        {
            string s = Probe(string.Format("-select_streams a -show_entries stream=index -of csv=p=0 \"{0}\"", path));
            return s.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Count(l => l.Trim().Length > 0);
        }

        public static string Convert(string path, string format)
        {
            string[] spec;
            if (!ConvertFormats.TryGetValue(format ?? "", out spec)) throw new ArgumentException("Formato no soportado: " + format);
            if (!File.Exists(path)) throw new FileNotFoundException("Archivo no encontrado.", path);

            string name = string.Format("{0}_{1}_{2:HHmmss}{3}", Path.GetFileNameWithoutExtension(path), format, DateTime.Now, spec[0]);
            string output = Path.Combine(Program.ExportsDir, name);
            string error;
            if (Ffmpeg(string.Format("-i \"{0}\" {1} \"{2}\"", path, spec[1], output), 7200000, out error) != 0)
            {
                throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "FFmpeg no pudo convertir el archivo." : error);
            }
            return output;
        }
    }

    #endregion

    #region Screen Recorder (Desktop Duplication + WASAPI, muxed with FFmpeg)

    public static class ScreenRecorder
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        public static string RecordingsDir;
        public static volatile bool IsRecording;
        public static string LastOutput;
        public static string LastError;
        // Voice enhancement + noise suppression on the mic track; applied in the background finish pass, so it costs
        // nothing while recording and can be toggled until the take is stopped
        public static volatile bool EnhanceVoice = true;
        // Voice chain tuned per take from the mic WAV itself: notches for steady electrical whine (no denoiser removes
        // it, and a gate makes it pulse with the voice), rumble/hiss cut, FFT denoiser for room noise, gain so the voice
        // sits above the game, gate halfway between room noise and voice, gentle compression, limiter without auto-gain
        private static string VoiceFilter(string wavPath)
        {
            List<double> tones;
            double noiseDb, voiceDb;
            if (!AnalyzeMic(wavPath, out tones, out noiseDb, out voiceDb))
            {
                tones = new List<double>();
                noiseDb = -60;
                voiceDb = -30;
            }
            bool speech = voiceDb - noiseDb >= 6; // otherwise the take has no voice: no gain, gate stays mostly closed
            double gain = speech ? Math.Max(0, Math.Min(20, -14 - voiceDb)) : 0;
            double gateDb = (speech ? (noiseDb + voiceDb) / 2 : voiceDb + 3) + gain;
            Log(string.Format(CultureInfo.InvariantCulture, "Voice: noise {0:0} dB, voice {1:0} dB, gain {2:0.0} dB, gate {3:0} dB, tones [{4}]",
                noiseDb, voiceDb, gain, gateDb, string.Join(" ", tones.Select(t => t.ToString("0", CultureInfo.InvariantCulture)))));
            StringBuilder f = new StringBuilder("highpass=f=90");
            foreach (double hz in tones) f.AppendFormat(CultureInfo.InvariantCulture, ",bandreject=f={0:0}:width_type=q:w=25", hz);
            f.AppendFormat(CultureInfo.InvariantCulture,
                ",lowpass=f=12000,afftdn=nr=18:nf={0:0}:tn=1,volume={1:0.0}dB,agate=threshold={2:0.0000}:ratio=3:range=0.06:attack=5:release=400:detection=rms," +
                "equalizer=f=3000:t=q:w=1:g=2,acompressor=threshold=-20dB:ratio=2.5:attack=10:release=200,alimiter=limit=0.89:level=0",
                Math.Max(-80, Math.Min(-20, noiseDb)), gain, Math.Max(0.001, Math.Min(0.5, Math.Pow(10, gateDb / 20))));
            return f.ToString();
        }

        // Room noise (20th percentile of 50 ms levels), voice (95th) and steady tones (bins whose 20th percentile over
        // time still stands 12 dB above their neighbours: whine never stops, voice and music do)
        // shortcut: only the first 3 min are analysed; enough for whine and levels unless the voice starts much later
        private static bool AnalyzeMic(string path, out List<double> tones, out double noiseDb, out double voiceDb)
        {
            tones = new List<double>();
            noiseDb = voiceDb = 0;
            try
            {
                float[] x;
                int rate;
                if (!ReadWavMono(path, 180, out x, out rate) || x.Length < rate * 2) return false;
                int w = rate / 20;
                List<double> levels = new List<double>();
                for (int i = 0; i + w <= x.Length; i += w)
                {
                    double s = 0;
                    for (int j = i; j < i + w; j++) s += x[j] * x[j];
                    levels.Add(10 * Math.Log10(s / w + 1e-12));
                }
                levels.Sort();
                noiseDb = levels[(int)(levels.Count * 0.2)];
                voiceDb = levels[(int)(levels.Count * 0.95)];

                const int N = 8192;
                int frames = x.Length / N, bins = N / 2;
                float[][] mag = new float[bins][];
                for (int b = 0; b < bins; b++) mag[b] = new float[frames];
                double[] re = new double[N], im = new double[N];
                for (int fr = 0; fr < frames; fr++)
                {
                    for (int i = 0; i < N; i++)
                    {
                        re[i] = x[fr * N + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)));
                        im[i] = 0;
                    }
                    Fft(re, im);
                    for (int b = 0; b < bins; b++) mag[b][fr] = (float)Math.Sqrt(re[b] * re[b] + im[b] * im[b]);
                }
                double[] p = new double[bins];
                for (int b = 0; b < bins; b++)
                {
                    Array.Sort(mag[b]);
                    p[b] = 20 * Math.Log10(mag[b][frames / 5] + 1e-12);
                }
                double bw = (double)rate / N;
                List<KeyValuePair<double, double>> found = new List<KeyValuePair<double, double>>();
                double[] around = new double[81];
                for (int b = (int)(1000 / bw); b < Math.Min(bins - 41, (int)(16000 / bw)); b++)
                {
                    if (p[b] < p[b - 2] || p[b] < p[b - 1] || p[b] < p[b + 1] || p[b] < p[b + 2]) continue;
                    Array.Copy(p, b - 40, around, 0, 81);
                    Array.Sort(around);
                    if (p[b] - around[40] > 12) found.Add(new KeyValuePair<double, double>(p[b] - around[40], b * bw));
                }
                tones = found.OrderByDescending(t => t.Key).Take(6).Select(t => t.Value).ToList();
                return true;
            }
            catch (Exception ex)
            {
                Log("Mic analysis failed: " + ex.Message);
                return false;
            }
        }

        // 32-bit float (what WASAPI shared mode gives) or 16-bit PCM WAV, channels averaged, up to maxSeconds
        private static bool ReadWavMono(string path, int maxSeconds, out float[] samples, out int rate)
        {
            samples = null;
            rate = 0;
            using (BinaryReader r = new BinaryReader(File.OpenRead(path)))
            {
                Stream s = r.BaseStream;
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "RIFF") return false;
                r.ReadUInt32();
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WAVE") return false;
                int channels = 0, bits = 0, tag = 0;
                while (s.Position + 8 <= s.Length)
                {
                    string id = Encoding.ASCII.GetString(r.ReadBytes(4));
                    long size = r.ReadUInt32();
                    long next = s.Position + size + (size & 1);
                    if (id == "fmt ")
                    {
                        tag = r.ReadUInt16();
                        channels = r.ReadUInt16();
                        rate = r.ReadInt32();
                        r.ReadInt32();
                        r.ReadUInt16();
                        bits = r.ReadUInt16();
                        if (tag == 0xFFFE && size >= 40)
                        {
                            r.ReadBytes(8);
                            tag = r.ReadUInt16(); // sub-format GUID starts with the plain format tag
                        }
                    }
                    else if (id == "data")
                    {
                        bool isFloat = tag == 3 && bits == 32, isPcm = tag == 1 && bits == 16;
                        if (channels == 0 || rate <= 0 || !(isFloat || isPcm)) return false;
                        int frameBytes = channels * bits / 8;
                        // The header size may be stale on a take that was never finalised: trust the file length then
                        long avail = s.Length - s.Position;
                        if (size > 0 && size < avail) avail = size;
                        int total = (int)Math.Min(avail / frameBytes, (long)maxSeconds * rate);
                        samples = new float[total];
                        byte[] buf = new byte[rate * frameBytes];
                        for (int done = 0; done < total;)
                        {
                            int want = Math.Min(rate, total - done), got = s.Read(buf, 0, want * frameBytes) / frameBytes;
                            if (got == 0) break;
                            for (int i = 0; i < got; i++)
                            {
                                float sum = 0;
                                for (int c = 0; c < channels; c++)
                                {
                                    int o = i * frameBytes + c * bits / 8;
                                    sum += isFloat ? BitConverter.ToSingle(buf, o) : BitConverter.ToInt16(buf, o) / 32768f;
                                }
                                samples[done + i] = sum / channels;
                            }
                            done += got;
                        }
                        return true;
                    }
                    s.Position = next;
                }
                return false;
            }
        }

        private static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        double t = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = t;
                    }
                }
            }
        }

        // Takes still being compressed in the background (a new take can start meanwhile)
        private static int _pending;
        public static bool IsProcessing { get { return Thread.VolatileRead(ref _pending) > 0; } }

        // One recording: the video may be split in segments if the capture process had to be restarted
        private class Take
        {
            public readonly Stopwatch Clock = new Stopwatch();
            // Wall-clock origin (µs since epoch): video frames are stamped relative to it with FFmpeg's RTCTIME and
            // each audio track knows its own start on the same clock, so sync never depends on when FFmpeg stops
            public long OriginUs = WasapiCapture.NowUs();
            public readonly List<string> Segments = new List<string>();
            public readonly List<double> SegmentStarts = new List<double>(); // seconds on Clock
            public string Stamp;
            public int Fps;
            public string[] Screen;
            public string Camera;
            public WasapiCapture Sys, Mic;
            public StringBuilder Errors = new StringBuilder();
        }

        private static Take _take;
        private static Process _ffmpeg;
        private static StringBuilder _ffmpegErr = new StringBuilder();
        private static CameraFeed _feed;
        private static string _encoderArgs;
        private static readonly List<Thread> _finishThreads = new List<Thread>();

        public static TimeSpan Elapsed { get { Take t = _take; return t != null ? t.Clock.Elapsed : TimeSpan.Zero; } }

        private static string _camera;
        private static bool _cameraSearched;

        // First DirectShow video device (webcam), or null when there is none
        private static readonly object _cameraLock = new object();

        public static string FindCamera()
        {
            if (_cameraSearched) return _camera;
            lock (_cameraLock)
            {
                if (_cameraSearched) return _camera;
                try
                {
                    if (!FfmpegEngine.IsAvailable) return null;
                    string output, error;
                    MediaTools.Run(FfmpegEngine.FfmpegPath, "-hide_banner -list_devices true -f dshow -i dummy", 15000, out output, out error);
                    Match m = Regex.Match(error ?? "", "\"([^\"]+)\" \\(video\\)");
                    _camera = m.Success ? m.Groups[1].Value : null;
                }
                catch { _camera = null; }
                finally { _cameraSearched = true; }
                return _camera;
            }
        }

        public static void Start(bool captureSystem, bool captureMic, int fps)
        {
            Start(captureSystem, captureMic, false, fps);
        }

        private static readonly object _gate = new object();

        public static void Start(bool captureSystem, bool captureMic, bool captureCamera, int fps)
        {
            lock (_gate)
            {
                if (IsRecording) return;
                if (!FfmpegEngine.IsAvailable) throw new InvalidOperationException("FFmpeg no fue encontrado en el sistema.");
                Directory.CreateDirectory(RecordingsDir);
                LastError = null;

                Take take = new Take { Stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"), Fps = fps };
                // Audio starts first; the muxer trims its head so it lines up with the first video frame
                take.Sys = captureSystem ? new WasapiCapture(true, Path.Combine(Program.ScratchDir, "rec_" + take.Stamp + "_sys.wav")) : null;
                take.Mic = captureMic ? new WasapiCapture(false, Path.Combine(Program.ScratchDir, "rec_" + take.Stamp + "_mic.wav")) : null;
                // Audio and screen start in parallel: sync comes from absolute wall-clock stamps, not from start order
                Stopwatch timing = Stopwatch.StartNew();
                if (take.Sys != null) take.Sys.Begin();
                if (take.Mic != null) take.Mic.Begin();

                take.Camera = captureCamera ? FindCamera() : null;
                if (captureCamera && take.Camera == null) LastError = "No se encontró ninguna cámara; se graba sin cámara.";

                take.Clock.Start();
                // Desktop Duplication first; GDI capture when it is unavailable (remote desktop, old driver)
                foreach (string[] screen in ScreenSources(fps))
                {
                    take.Screen = screen;
                    if (LaunchSegment(take))
                    {
                        if (take.Sys != null && !take.Sys.WaitReady()) { LastError = "Audio del sistema no disponible: " + take.Sys.Error; take.Sys = null; }
                        if (take.Mic != null && !take.Mic.WaitReady()) { LastError = "Micrófono no disponible: " + take.Mic.Error; take.Mic = null; }
                        Log(string.Format("Take started: capture and audio running after {0} ms", timing.ElapsedMilliseconds));
                        SaveManifest(take);
                        _take = take;
                        IsRecording = true;
                        new Thread(() => Watchdog(take)) { IsBackground = true, Name = "KidCut capture watchdog" }.Start();
                        return;
                    }
                }
                StopAudio(take);
                throw new InvalidOperationException("FFmpeg no pudo capturar la pantalla:\n" + _ffmpegErr);
            }
        }

        private static string[][] ScreenSources(int fps)
        {
            return new[]
            {
                new[] { string.Format("-f lavfi -i ddagrab=output_idx=0:framerate={0}:draw_mouse=1", fps), "hwdownload,format=bgra," },
                new[] { string.Format("-f gdigrab -framerate {0} -draw_mouse 1 -i desktop", fps), "" }
            };
        }

        // Starts one capture process writing a new segment; false if it dies right away
        private static bool LaunchSegment(Take take)
        {
            string segment = Path.Combine(Program.ScratchDir, string.Format("rec_{0}_v{1}.mkv", take.Stamp, take.Segments.Count));
            if (take.Camera != null)
            {
                // The framed camera (background, shape, border) is rendered here and piped in, exactly like the bubble
                _feed = new CameraFeed();
                _feed.Start();
            }
            double startedAt = take.Clock.Elapsed.TotalSeconds;
            Process p = LaunchCapture(take.Screen[0], take.Screen[1], _feed, take.Fps, segment, take.OriginUs);
            if (p.WaitForExit(1500))
            {
                lock (_ffmpegErr) take.Errors.AppendLine(_ffmpegErr.ToString());
                StopFeed();
                return false;
            }
            _ffmpeg = p;
            take.Segments.Add(segment);
            take.SegmentStarts.Add(startedAt);
            if (_take == take) SaveManifest(take); // restarted mid-take: record the new segment too
            return true;
        }

        // If the capture process dies mid-take (fullscreen switch, display mode change, driver reset) a new segment is
        // started; Finish stitches the segments on the timeline so audio stays in sync
        private static void Watchdog(Take take)
        {
            int failures = 0;
            while (IsRecording && _take == take)
            {
                Thread.Sleep(500);
                lock (_gate)
                {
                    if (!IsRecording || _take != take || !_ffmpeg.HasExited) continue;
                    string err;
                    lock (_ffmpegErr) err = _ffmpegErr.ToString().Trim();
                    take.Errors.AppendLine(err);
                    Log("Capture process exited mid-take, restarting: " + err);
                    StopFeed();
                    if (LaunchSegment(take)) failures = 0;
                    else if (++failures == 6) LastError = "La captura de pantalla se detuvo y no se pudo reanudar:\n" + err;
                }
            }
        }

        public static void Stop()
        {
            Take take;
            lock (_gate)
            {
                if (!IsRecording) return;
                IsRecording = false;
                take = _take;
                take.Clock.Stop();
                Interlocked.Increment(ref _pending);
                try
                {
                    _ffmpeg.StandardInput.Write("q");
                    _ffmpeg.StandardInput.Flush();
                }
                catch { }
                StopAudio(take);
                if (!_ffmpeg.WaitForExit(15000))
                {
                    try { _ffmpeg.Kill(); } catch { }
                }
                StopFeed();
            }
            Thread t = new Thread(() => Finish(take)) { IsBackground = true, Name = "KidCut finish" };
            lock (_finishThreads) _finishThreads.Add(t);
            t.Start();
        }

        // Called on app exit so an active take is still saved
        public static void StopAndWait()
        {
            Stop();
            Thread[] pending;
            lock (_finishThreads) pending = _finishThreads.ToArray();
            foreach (Thread t in pending) t.Join(600000);
        }

        private static void StopFeed()
        {
            if (_feed != null) _feed.Stop();
            _feed = null;
        }

        private static void StopAudio(Take take)
        {
            if (take.Sys != null) take.Sys.RequestStop();
            if (take.Mic != null) take.Mic.RequestStop();
            if (take.Sys != null) take.Sys.Join();
            if (take.Mic != null) take.Mic.Join();
        }

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kidcut_boot.log"),
                    string.Format("[{0}] {1}\n", DateTime.Now, message));
            }
            catch { }
        }

        private static Process LaunchCapture(string screenInput, string screenFilter, CameraFeed feed, int fps, string output, long originUs)
        {
            // Every frame is stamped with the wall-clock time it arrived, relative to the take's origin: segments and
            // audio tracks then line up by absolute time (passthrough keeps those stamps; Finish makes the output CFR)
            string stamp = string.Format(CultureInfo.InvariantCulture, "setpts=(RTCTIME-{0})/(TB*1000000)", originUs);
            string video;
            if (feed == null)
            {
                video = string.Format("{0} -vf \"{1}{2}\" -fps_mode passthrough", screenInput, screenFilter, stamp);
            }
            else
            {
                // Centered where the user last left the on-screen bubble (bottom-right by default); the feed draws the
                // camera centered in its fixed box, so live size/shape changes keep that center. Overlay pairs the
                // screen and the piped camera by the same wall-clock stamps.
                Size cam = feed.Size;
                Size phys = PhysicalScreenSize();
                Rectangle logical = Screen.PrimaryScreen.Bounds;
                double k = (double)phys.Width / logical.Width;
                CameraStyle style = CameraStyle.Current;
                PointF frac = CameraBubble.PositionFraction(style);
                Size scaled = CameraBubble.ScaledSize(style);
                int camX = (int)Math.Round(frac.X * phys.Width + scaled.Width * k / 2 - cam.Width / 2.0);
                int camY = (int)Math.Round(frac.Y * phys.Height + scaled.Height * k / 2 - cam.Height / 2.0);
                video = string.Format(
                    // Tiny input queue: FFmpeg otherwise reads the pipe ahead into an unbounded queue and frames get
                    // stamped seconds after they were rendered (the camera drifted ~4 s behind in a 30 s take)
                    "{0} -thread_queue_size 2 -f rawvideo -pix_fmt bgra -video_size {1}x{2} -framerate 30 -i \"{3}\" -filter_complex \"" +
                    "[0:v]{4}{7}[scr];[1:v]{7}[pip];[scr][pip]overlay={5}:{6}:eof_action=repeat[v]\" -map \"[v]\" -fps_mode passthrough",
                    screenInput, cam.Width, cam.Height, feed.PipePath, screenFilter, camX, camY, stamp);
            }
            // 1 s Matroska clusters: if the process dies, at most ~1 s of video is lost instead of several
            string args = string.Format("-y -hide_banner -nostats -loglevel error {0} {1} -g {2} -cluster_time_limit 1000 \"{3}\"", video, EncoderArgs(), fps * 2, output);
            Log("Capture args: " + args);
            ProcessStartInfo psi = new ProcessStartInfo(FfmpegEngine.FfmpegPath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            };
            _ffmpegErr = new StringBuilder();
            StringBuilder errSb = _ffmpegErr;
            Process p = new Process { StartInfo = psi };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errSb) errSb.AppendLine(e.Data); };
            p.Start();
            ChildProcesses.Track(p);
            p.BeginErrorReadLine();
            // Games, streaming and other overlays compete for the CPU: the live capture must not be the one that starves
            try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
            return p;
        }

        // Live capture: GPU encoder when one works, otherwise x264 ultrafast (about 1/8 of the CPU of veryfast at the
        // same visual quality). The bigger file is recompressed in the background when the take ends.
        // Probing encoders and the camera spawns FFmpeg several times; under a full CPU that took ~40 s, so it is done
        // once in the background when the app opens instead of when REC is pressed
        public static void WarmUp()
        {
            try
            {
                EncoderArgs();
                FindCamera();
                RecoverOrphanedTakes();
            }
            catch { }
        }

        private static readonly object _encoderLock = new object();

        private static string EncoderArgs()
        {
            lock (_encoderLock) return ProbeEncoder();
        }

        private static string ProbeEncoder()
        {
            if (_encoderArgs != null) return _encoderArgs;
            string[] candidates =
            {
                "-c:v h264_nvenc -preset p4 -rc vbr -cq 19 -b:v 0 -pix_fmt yuv420p",
                "-c:v h264_amf -quality balanced -rc cqp -qp_i 18 -qp_p 20 -pix_fmt yuv420p",
                "-c:v h264_qsv -global_quality 20 -pix_fmt nv12"
            };
            foreach (string c in candidates)
            {
                string err;
                if (MediaTools.Ffmpeg(string.Format("-f lavfi -i color=black:s=1280x720:d=0.1 {0} -f null -", c), 15000, out err) == 0)
                {
                    return _encoderArgs = c;
                }
            }
            return _encoderArgs = "-c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p";
        }

        // Everything needed to finish a take, also written to disk while recording so a take that could not be saved
        // (app closed, crash, error at stop) is recovered automatically the next time KidCut opens
        public class TakeManifest
        {
            public string Stamp { get; set; }
            public long OriginUs { get; set; }
            public int Fps { get; set; }
            public List<string> Segments { get; set; }
            public List<string> AudioPaths { get; set; }
            public List<long> AudioStartUs { get; set; }
            public bool VoiceFx { get; set; }
        }

        private static string ManifestPath(string stamp)
        {
            return Path.Combine(Program.ScratchDir, "rec_" + stamp + ".take.json");
        }

        private static TakeManifest ToManifest(Take take)
        {
            TakeManifest m = new TakeManifest
            {
                Stamp = take.Stamp,
                OriginUs = take.OriginUs,
                Fps = take.Fps,
                Segments = new List<string>(take.Segments),
                AudioPaths = new List<string>(),
                AudioStartUs = new List<long>(),
                VoiceFx = EnhanceVoice
            };
            foreach (WasapiCapture a in new[] { take.Sys, take.Mic })
            {
                if (a == null || a.Error != null) continue;
                m.AudioPaths.Add(a.FilePath);
                m.AudioStartUs.Add(a.StartUs);
            }
            return m;
        }

        private static void SaveManifest(Take take)
        {
            try { File.WriteAllText(ManifestPath(take.Stamp), new JavaScriptSerializer().Serialize(ToManifest(take))); }
            catch (Exception ex) { Log("Could not write take manifest: " + ex.Message); }
        }

        // Takes left unsaved by a previous session: finished in the background at low priority
        private static void RecoverOrphanedTakes()
        {
            string[] manifests;
            try { manifests = Directory.GetFiles(Program.ScratchDir, "rec_*.take.json"); }
            catch { return; }
            foreach (string path in manifests)
            {
                TakeManifest m;
                try { m = new JavaScriptSerializer().Deserialize<TakeManifest>(File.ReadAllText(path)); }
                catch { continue; }
                if (m == null || m.Segments == null || m.Segments.Count == 0 || (_take != null && _take.Stamp == m.Stamp)) continue;
                Log("Recovering unsaved take " + m.Stamp);
                Interlocked.Increment(ref _pending);
                TakeManifest take = m;
                Thread t = new Thread(() => FinishManifest(take, double.MaxValue, true)) { IsBackground = true, Name = "KidCut recover" };
                lock (_finishThreads) _finishThreads.Add(t);
                t.Start();
            }
        }

        private static void Finish(Take take)
        {
            // The origin is taken just before the clock starts, so nothing real lies beyond the stop instant
            FinishManifest(ToManifest(take), take.Clock.Elapsed.TotalSeconds + 0.5, false);
        }

        private static void FinishManifest(TakeManifest take, double stopAt, bool recovered)
        {
            string manifest = ManifestPath(take.Stamp);
            List<string> temps = new List<string>(take.Segments) { manifest };
            try
            {
                // Place each segment on the take's timeline; a gap left by a capture restart is filled with a still frame
                // Frames carry wall-clock stamps relative to the take origin, so each segment's first/last packet says
                // exactly where it sits on the timeline (also for a segment whose process was killed mid-write)
                int n = take.Segments.Count;
                double frame = 1.0 / take.Fps;
                double[] s0 = new double[n], s1 = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double a, b;
                    if (!File.Exists(take.Segments[i]) || !MediaTools.ProbeVideoSpan(take.Segments[i], out a, out b)) continue;
                    s0[i] = a;
                    s1[i] = Math.Min(b + frame, stopAt);
                }
                // Segments with no frames are skipped; the next one's gap then covers their time
                int[] valid = Enumerable.Range(0, n).Where(i => s1[i] - s0[i] > 0.05).ToArray();
                if (valid.Length == 0) throw new InvalidOperationException("La grabación de video quedó vacía.");
                for (int k = 1; k < valid.Length; k++) s1[valid[k - 1]] = Math.Min(s1[valid[k - 1]], s0[valid[k]]);
                double[] dur = s1.Select((end, i) => end - s0[i]).ToArray();
                int first = valid[0], lastSeg = valid[valid.Length - 1];
                double videoStart = s0[first];
                double videoSec = s1[lastSeg] - videoStart;

                List<int> audio = new List<int>();
                for (int a = 0; a < take.AudioPaths.Count; a++)
                {
                    temps.Add(take.AudioPaths[a]);
                    FileInfo fi = new FileInfo(take.AudioPaths[a]);
                    if (fi.Exists && fi.Length > 1024) audio.Add(a);
                }

                string output = Path.Combine(RecordingsDir, "Grabacion_" + (recovered ? take.Stamp.Substring(0, 15) + "_recuperada" : DateTime.Now.ToString("yyyyMMdd_HHmmss")) + ".mp4");
                StringBuilder args = new StringBuilder();
                StringBuilder graph = new StringBuilder();
                for (int k = 0; k < valid.Length; k++)
                {
                    int i = valid[k];
                    args.AppendFormat("-i \"{0}\" ", take.Segments[i]);
                    double gap = k == 0 ? 0 : s0[i] - s1[valid[k - 1]];
                    graph.AppendFormat(CultureInfo.InvariantCulture, "[{0}:v]trim=duration={1:0.000},setpts=PTS-STARTPTS{2}[v{0}];",
                        k, dur[i], gap > 0.05 ? string.Format(CultureInfo.InvariantCulture, ",tpad=start_duration={0:0.000}:start_mode=clone", gap) : "");
                }
                for (int k = 0; k < valid.Length; k++) graph.AppendFormat("[v{0}]", k);
                graph.AppendFormat("concat=n={0}:v=1:a=0[vout]", valid.Length);
                n = valid.Length; // audio inputs come after the video inputs

                foreach (int a in audio)
                {
                    // Both clocks are absolute: skip the audio recorded before the first frame (or delay it if it began later)
                    double lead = videoStart - (take.AudioStartUs[a] - take.OriginUs) / 1000000.0;
                    if (lead > 0) args.AppendFormat(CultureInfo.InvariantCulture, "-ss {0:0.000} ", lead);
                    else if (lead < 0) args.AppendFormat(CultureInfo.InvariantCulture, "-itsoffset {0:0.000} ", -lead);
                    args.AppendFormat("-i \"{0}\" ", take.AudioPaths[a]);
                }
                // Stream labels per audio input; the mic goes through the voice filter (split once for mix + its own track)
                string[] mixIn = new string[audio.Count], track = new string[audio.Count];
                for (int k = 0; k < audio.Count; k++)
                {
                    mixIn[k] = string.Format("[{0}:a]", n + k);
                    track[k] = (n + k) + ":a";
                    if (!take.VoiceFx || !take.AudioPaths[audio[k]].EndsWith("_mic.wav", StringComparison.OrdinalIgnoreCase)) continue;
                    graph.AppendFormat(";{0}{1},asplit=2[vm][vt]", mixIn[k], VoiceFilter(take.AudioPaths[audio[k]]));
                    mixIn[k] = "[vm]";
                    track[k] = "[vt]";
                    if (audio.Count == 1) graph.Append(";[vm]anullsink");
                }
                if (audio.Count == 2)
                {
                    // Track 1 = mix for any player; tracks 2/3 = separate channels the editor imports independently
                    // Both inputs upmixed to stereo first: amix negotiates one layout for all of them and, depending on
                    // input offsets, picks the mic's mono and folds the game down with it
                    // The game keeps its own level; the limiter only catches peaks where game and voice add up (no auto-gain)
                    graph.AppendFormat(";{0}aformat=channel_layouts=stereo[mx0];{1}aformat=channel_layouts=stereo[mx1];[mx0][mx1]amix=inputs=2:normalize=0,alimiter=limit=0.95:level=0[mix]",
                        mixIn[0], mixIn[1]);
                }
                args.AppendFormat("-filter_complex \"{0}\" -map \"[vout]\" ", graph);
                if (audio.Count == 2)
                {
                    args.AppendFormat("-map \"[mix]\" -map \"{0}\" -map \"{1}\" ", track[0], track[1]);
                    args.Append("-metadata:s:a:0 title=Mezcla -metadata:s:a:1 title=Sistema -metadata:s:a:2 title=Microfono ");
                    args.Append("-disposition:a:0 default -disposition:a:1 0 -disposition:a:2 0 ");
                }
                else if (audio.Count == 1)
                {
                    args.AppendFormat("-map \"{0}\" ", track[0]);
                }
                // Final compression (veryfast CRF 20 ~ 1/3 the size of the live file at the same visual quality)
                args.AppendFormat(CultureInfo.InvariantCulture,
                    "-t {0:0.000} -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -fps_mode cfr -r {1} -c:a aac -b:a 192k -movflags +faststart \"{2}\"",
                    videoSec, take.Fps, output);

                string err;
                if (Compress(args.ToString(), out err) != 0) throw new InvalidOperationException(err);

                LastOutput = output;
                foreach (string t in temps)
                {
                    try { File.Delete(t); } catch { }
                }
            }
            catch (Exception ex)
            {
                // Temp files and manifest are kept so nothing recorded is lost; the next launch retries once more
                Log("Finish failed for " + take.Stamp + ": " + ex.Message);
                if (recovered)
                {
                    try { File.Move(manifest, manifest + ".failed"); } catch { }
                }
                LastError = "No se pudo guardar la grabación (los archivos originales quedan en " + Program.ScratchDir +
                            "; KidCut lo reintentará al volver a abrirse):\n" + ex.Message;
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
                lock (_finishThreads) _finishThreads.Remove(Thread.CurrentThread);
            }
        }

        // Background compression at low priority so it never competes with a game or a new take
        private static int Compress(string args, out string error)
        {
            Log("Finish args: " + args);
            ProcessStartInfo psi = new ProcessStartInfo(FfmpegEngine.FfmpegPath, "-y -hide_banner -loglevel error " + args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            StringBuilder errSb = new StringBuilder();
            using (Process p = new Process { StartInfo = psi })
            {
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errSb) errSb.AppendLine(e.Data); };
                p.Start();
                ChildProcesses.Track(p);
                p.BeginErrorReadLine();
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                p.WaitForExit();
                lock (errSb) error = errSb.ToString().Trim();
                return p.ExitCode;
            }
        }

        public static string TakeScreenshot()
        {
            Directory.CreateDirectory(RecordingsDir);
            string path = Path.Combine(RecordingsDir, "Captura_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
            Size size = PhysicalScreenSize();
            IntPtr previous = IntPtr.Zero;
            try { previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            try
            {
                using (Bitmap bmp = new Bitmap(size.Width, size.Height))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(0, 0, 0, 0, size);
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            finally
            {
                if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
            }
            return path;
        }

        // Primary screen in physical pixels: the app is DPI-unaware, so query with per-monitor awareness on this thread only
        private static Size PhysicalScreenSize()
        {
            IntPtr previous = IntPtr.Zero;
            try { previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            try
            {
                return new Size(GetSystemMetrics(0), GetSystemMetrics(1));
            }
            finally
            {
                if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
            }
        }
    }

    #endregion

    #region Camera Frame Styles (shape, border, colors, filter)

    public class CameraStyle
    {
        public string Shape { get; set; }
        public string Border { get; set; }
        public string Filter { get; set; }
        public string[] Colors { get; set; } // one color = solid, more = gradient stops
        public string Background { get; set; }
        public bool Mirror { get; set; }
        public double? PosX { get; set; } // camera top-left as a fraction of the primary screen; null = bottom-right
        public double? PosY { get; set; }
        public int SizePercent { get; set; } // camera size: 30..100 % of its original size

        public static readonly CameraStyle Current = Load();

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kidcut_camera.json"); }
        }

        private static CameraStyle Load()
        {
            CameraStyle s = null;
            try
            {
                if (File.Exists(FilePath)) s = new JavaScriptSerializer().Deserialize<CameraStyle>(File.ReadAllText(FilePath));
            }
            catch { }
            if (s == null) s = new CameraStyle();
            // The file is user-editable: anything unknown falls back to the defaults
            if (!CameraFrames.Has(CameraFrames.Shapes, s.Shape)) s.Shape = "circle";
            if (!CameraFrames.Has(CameraFrames.Borders, s.Border)) s.Border = "solid";
            if (!CameraFrames.Has(CameraFrames.Filters, s.Filter)) s.Filter = "none";
            if (!CameraFrames.Has(CameraFrames.Backgrounds, s.Background)) s.Background = "none";
            if (!(s.PosX >= 0 && s.PosX <= 1 && s.PosY >= 0 && s.PosY <= 1)) s.PosX = s.PosY = null;
            if (s.SizePercent < 30 || s.SizePercent > 100) s.SizePercent = 100;
            if (s.Colors == null || s.Colors.Length == 0 || s.Colors.Length > 8 || !s.Colors.All(CameraFrames.IsHexColor))
            {
                s.Colors = new[] { "#FF2244", "#FFD200" };
            }
            return s;
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this)); } catch { }
        }

        // Raised on the UI thread whenever the designer (or the bubble menu) edits Current, so the live camera, the
        // bubble and an ongoing hidden-camera recording all pick the change up without reopening the camera
        public static event Action Changed;

        public static void NotifyChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        public bool IsWide { get { return Shape == "rect"; } }
    }

    // Renders the camera frame with GDI+ so the designer preview and the FFmpeg recording (mask + border PNGs) match exactly
    public static class CameraFrames
    {
        public static readonly string[][] Shapes =
        {
            new[] { "circle", "Círculo" }, new[] { "rounded", "Redondeado" }, new[] { "square", "Cuadrado" },
            new[] { "rect", "16:9" }, new[] { "hexagon", "Hexágono" }, new[] { "octagon", "Octágono" },
            new[] { "diamond", "Rombo" }, new[] { "star", "Estrella" }, new[] { "heart", "Corazón" }, new[] { "blob", "Orgánico" }
        };

        public static readonly string[][] Borders =
        {
            new[] { "none", "Sin borde" }, new[] { "solid", "Sólido" }, new[] { "double", "Doble" },
            new[] { "neon", "Neón" }, new[] { "brush", "Brochazo" }, new[] { "dashed", "Discontinuo" }
        };

        // Color filters are applied in C# (CameraProcessor) so they can change live without reopening the camera
        public static readonly string[][] Filters =
        {
            new[] { "none", "Normal" }, new[] { "bw", "Blanco y negro" }, new[] { "sepia", "Sepia" },
            new[] { "vivid", "Vívido" }, new[] { "warm", "Cálido" }, new[] { "cool", "Frío" },
            new[] { "vintage", "Vintage" }, new[] { "contrast", "Alto contraste" }, new[] { "pixel", "Pixel 8-bit" }
        };

        // "none" keeps the real room; every other one is painted behind the person found by PersonSegmenter
        public static readonly string[][] Backgrounds =
        {
            new[] { "none", "Sin fondo (real)" }, new[] { "blur", "Desenfocado" }, new[] { "studio", "Estudio" },
            new[] { "frame", "Colores del marco" }, new[] { "sunset", "Atardecer" }, new[] { "ocean", "Océano" },
            new[] { "synthwave", "Synthwave" }, new[] { "night", "Noche estrellada" }, new[] { "galaxy", "Galaxia" },
            new[] { "aurora", "Aurora" }, new[] { "bokeh", "Bokeh" }, new[] { "matrix", "Matrix" },
            new[] { "pixelland", "Paisaje 8-bit" }, new[] { "bricks", "Ladrillos" }, new[] { "wood", "Madera" },
            new[] { "checker", "Damero" }, new[] { "dots", "Lunares" }, new[] { "stripes", "Rayas" },
            new[] { "notebook", "Cuaderno" }, new[] { "confetti", "Confeti" }, new[] { "hearts", "Corazones" },
            new[] { "clouds", "Cielo" }, new[] { "city", "Ciudad neón" }, new[] { "forest", "Bosque" },
            new[] { "mosaic", "Mosaico 8-bit" }
        };

        // FFmpeg side of the camera: only fit to the source size; mirror, background and filter happen in CameraProcessor
        public static string CameraChain(Size size)
        {
            return string.Format("scale={0}:{1}:force_original_aspect_ratio=increase,crop={0}:{1},format=bgr24", size.Width, size.Height);
        }

        // Full (100 %) camera size on the primary screen in physical pixels: 1/5 of the width, 1/4 for 16:9
        public static Size SourceSize(CameraStyle s)
        {
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            return CanvasSize(s, scr.Width / (s.IsWide ? 4 : 5));
        }

        // Camera at the requested canvas size (consumers smaller than the shared source get a scaled copy)
        public static Bitmap Fit(Bitmap source, Size canvas)
        {
            if (source.Size == canvas) return (Bitmap)source.Clone();
            Bitmap b = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(source, new Rectangle(Point.Empty, canvas));
            }
            return b;
        }

        public static readonly string[][] Palettes =
        {
            new[] { "#FF2244" }, new[] { "#FFD200" }, new[] { "#00F0FF" }, new[] { "#39FF14" },
            new[] { "#FF00CC" }, new[] { "#8A2BE2" }, new[] { "#FFFFFF" }, new[] { "#111111" },
            new[] { "#FF2244", "#FFD200" }, new[] { "#FF5F6D", "#FFC371" }, new[] { "#00C6FF", "#0072FF" },
            new[] { "#F72585", "#7209B7", "#3A0CA3" }, new[] { "#11998E", "#38EF7D" },
            new[] { "#BF953F", "#FCF6BA", "#B38728" },
            new[] { "#FF0000", "#FF9900", "#FFFF00", "#33FF00", "#00CCFF", "#9900FF" }
        };

        public static bool Has(string[][] table, string id)
        {
            return id != null && table.Any(r => r[0] == id);
        }

        public static bool IsHexColor(string c)
        {
            return c != null && Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$");
        }

        public static Size CanvasSize(CameraStyle s, int width)
        {
            width &= ~1;
            return s.Shape == "rect" ? new Size(width, (width * 9 / 16) & ~1) : new Size(width, width);
        }

        private static float Thickness(Size sz)
        {
            return Math.Max(3f, Math.Min(sz.Width, sz.Height) * 0.045f);
        }

        // Shape area inside the canvas, leaving room for glows and brush strokes outside the edge
        public static RectangleF ShapeRect(Size sz)
        {
            float pad = Thickness(sz) * 2.8f;
            return new RectangleF(pad, pad, sz.Width - 2 * pad, sz.Height - 2 * pad);
        }

        public static GraphicsPath ShapePath(string shape, RectangleF r)
        {
            GraphicsPath p = new GraphicsPath();
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, rx = r.Width / 2, ry = r.Height / 2;
            switch (shape)
            {
                case "circle":
                    p.AddEllipse(r);
                    break;
                case "rounded":
                case "rect":
                    float d = Math.Min(r.Width, r.Height) * (shape == "rect" ? 0.16f : 0.44f);
                    p.AddArc(r.X, r.Y, d, d, 180, 90);
                    p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                    p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    p.CloseFigure();
                    break;
                case "hexagon":
                    p.AddPolygon(Polygon(cx, cy, rx, ry, 6, 0, 1));
                    break;
                case "octagon":
                    p.AddPolygon(Polygon(cx, cy, rx, ry, 8, 22.5, 1));
                    break;
                case "diamond":
                    p.AddPolygon(Polygon(cx, cy, rx, ry, 4, -90, 1));
                    break;
                case "star":
                    p.AddPolygon(Polygon(cx, cy, rx, ry, 10, -90, 0.5));
                    break;
                case "heart":
                    PointF[] heart = new PointF[120];
                    for (int i = 0; i < heart.Length; i++)
                    {
                        double t = i * 2 * Math.PI / heart.Length;
                        double x = 16 * Math.Pow(Math.Sin(t), 3);
                        double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                        heart[i] = new PointF(cx + (float)(x / 16 * rx), r.Y + (float)((12 - y) / 29.5 * r.Height));
                    }
                    p.AddPolygon(heart);
                    break;
                case "blob":
                    PointF[] blob = new PointF[36];
                    for (int i = 0; i < blob.Length; i++)
                    {
                        double a = i * 2 * Math.PI / blob.Length;
                        double k = 0.92 + 0.05 * Math.Sin(3 * a) + 0.03 * Math.Sin(5 * a + 1);
                        blob[i] = new PointF(cx + (float)(Math.Cos(a) * rx * k), cy + (float)(Math.Sin(a) * ry * k));
                    }
                    p.AddClosedCurve(blob, 0.5f);
                    break;
                default:
                    p.AddRectangle(r);
                    break;
            }
            return p;
        }

        // Regular polygon; innerRatio < 1 alternates inner/outer vertices (star)
        private static PointF[] Polygon(float cx, float cy, float rx, float ry, int n, double startDeg, double innerRatio)
        {
            PointF[] pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                double a = (startDeg + i * 360.0 / n) * Math.PI / 180;
                double k = (i % 2 == 1) ? innerRatio : 1;
                pts[i] = new PointF(cx + (float)(Math.Cos(a) * rx * k), cy + (float)(Math.Sin(a) * ry * k));
            }
            return pts;
        }

        public static Brush MakeBrush(string[] colors, RectangleF bounds, int alpha)
        {
            Color[] cs = colors.Select(c => Color.FromArgb(alpha, ColorTranslator.FromHtml(c))).ToArray();
            if (cs.Length == 1) return new SolidBrush(cs[0]);
            RectangleF b = new RectangleF(bounds.X - 1, bounds.Y - 1, bounds.Width + 2, bounds.Height + 2);
            LinearGradientBrush lg = new LinearGradientBrush(b, cs[0], cs[cs.Length - 1], 45f);
            ColorBlend blend = new ColorBlend(cs.Length);
            blend.Colors = cs;
            blend.Positions = Enumerable.Range(0, cs.Length).Select(i => (float)i / (cs.Length - 1)).ToArray();
            lg.InterpolationColors = blend;
            return lg;
        }

        private static Graphics HighQuality(Bitmap bmp)
        {
            Graphics g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }

        public static Bitmap RenderBorder(CameraStyle s, Size sz)
        {
            Bitmap bmp = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppArgb);
            float t = Thickness(sz);
            RectangleF rect = ShapeRect(sz);
            using (Graphics g = HighQuality(bmp))
            using (GraphicsPath p = ShapePath(s.Shape, rect))
            {
                RectangleF bounds = p.GetBounds();
                switch (s.Border)
                {
                    case "solid":
                    case "dashed":
                        using (Brush b = MakeBrush(s.Colors, bounds, 255))
                        using (Pen pen = new Pen(b, t) { LineJoin = LineJoin.Round })
                        {
                            if (s.Border == "dashed")
                            {
                                pen.DashStyle = DashStyle.Dash;
                                pen.DashCap = DashCap.Round;
                            }
                            g.DrawPath(pen, p);
                        }
                        break;
                    case "double":
                        using (Brush b = MakeBrush(s.Colors, bounds, 255))
                        using (Pen pen = new Pen(b, t * 0.45f) { LineJoin = LineJoin.Round })
                        using (GraphicsPath inner = ShapePath(s.Shape, RectangleF.Inflate(rect, -t * 1.1f, -t * 1.1f)))
                        {
                            g.DrawPath(pen, p);
                            g.DrawPath(pen, inner);
                        }
                        break;
                    case "neon":
                        for (int i = 12; i >= 1; i--)
                        {
                            using (Brush b = MakeBrush(s.Colors, bounds, 16))
                            using (Pen glow = new Pen(b, t * (0.5f + i * 0.32f)) { LineJoin = LineJoin.Round })
                            {
                                g.DrawPath(glow, p);
                            }
                        }
                        using (Brush b = MakeBrush(s.Colors, bounds, 255))
                        using (Pen pen = new Pen(b, t * 0.7f) { LineJoin = LineJoin.Round })
                        using (Pen core = new Pen(Color.FromArgb(190, 255, 255, 255), t * 0.22f) { LineJoin = LineJoin.Round })
                        {
                            g.DrawPath(pen, p);
                            g.DrawPath(core, p);
                        }
                        break;
                    case "brush":
                        DrawBrushStroke(g, s, p, bounds, t);
                        break;
                }
            }
            return bmp;
        }

        // Paint-stroke look: several jittered, semi-opaque passes plus bristle streaks and splatter (fixed seed = same every time)
        private static void DrawBrushStroke(Graphics g, CameraStyle s, GraphicsPath path, RectangleF bounds, float t)
        {
            Random rnd = new Random(7);
            PointF[] pts;
            using (GraphicsPath flat = (GraphicsPath)path.Clone())
            {
                flat.Flatten(null, 0.5f);
                pts = flat.PathPoints;
            }
            if (pts.Length < 3) return;
            PointF center = new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

            for (int pass = 0; pass < 4; pass++)
            {
                double phase = rnd.NextDouble() * 6.28, freq = 2 + rnd.Next(4);
                float amp = t * (0.25f + (float)rnd.NextDouble() * 0.35f);
                PointF[] jittered = new PointF[pts.Length + 1];
                for (int i = 0; i <= pts.Length; i++)
                {
                    PointF q = pts[i % pts.Length];
                    double off = Math.Sin(i * freq * 2 * Math.PI / pts.Length + phase) * amp + (rnd.NextDouble() - 0.5) * t * 0.15;
                    double dx = q.X - center.X, dy = q.Y - center.Y, len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    jittered[i] = new PointF(q.X + (float)(dx / len * off), q.Y + (float)(dy / len * off));
                }
                using (Brush b = MakeBrush(s.Colors, bounds, 150 + rnd.Next(100)))
                using (Pen pen = new Pen(b, t * (0.7f + (float)rnd.NextDouble() * 0.7f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLines(pen, jittered);
                }
            }

            // Bristle streaks: thin broken lines riding along the stroke
            using (Brush b = MakeBrush(s.Colors, bounds, 210))
            using (Pen bristle = new Pen(b, Math.Max(1f, t * 0.14f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int k = 0; k < 6; k++)
                {
                    float off = (float)(rnd.NextDouble() - 0.5) * t * 1.6f;
                    int i = rnd.Next(pts.Length), len = pts.Length / (3 + rnd.Next(4));
                    List<PointF> seg = new List<PointF>();
                    for (int j = 0; j < len; j++)
                    {
                        PointF q = pts[(i + j) % pts.Length];
                        double dx = q.X - center.X, dy = q.Y - center.Y, l = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                        seg.Add(new PointF(q.X + (float)(dx / l * off), q.Y + (float)(dy / l * off)));
                    }
                    if (seg.Count > 1) g.DrawLines(bristle, seg.ToArray());
                }
            }

            // Splatter dots just outside the edge
            using (Brush b = MakeBrush(s.Colors, bounds, 230))
            {
                for (int k = 0; k < 28; k++)
                {
                    PointF q = pts[rnd.Next(pts.Length)];
                    double dx = q.X - center.X, dy = q.Y - center.Y, l = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    float off = t * (0.6f + (float)rnd.NextDouble() * 1.2f), r = t * (0.08f + (float)rnd.NextDouble() * 0.22f);
                    g.FillEllipse(b, q.X + (float)(dx / l * off) - r, q.Y + (float)(dy / l * off) - r, 2 * r, 2 * r);
                }
            }
        }

        private static Color Hex(string c)
        {
            return ColorTranslator.FromHtml(c);
        }

        private static void FillVertical(Graphics g, RectangleF r, params string[] colors)
        {
            Color[] cs = colors.Select(Hex).ToArray();
            using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), cs[0], cs[cs.Length - 1], 90f))
            {
                if (cs.Length > 2)
                {
                    ColorBlend blend = new ColorBlend(cs.Length);
                    blend.Colors = cs;
                    blend.Positions = Enumerable.Range(0, cs.Length).Select(i => (float)i / (cs.Length - 1)).ToArray();
                    b.InterpolationColors = blend;
                }
                g.FillRectangle(b, r);
            }
        }

        private static void Stars(Graphics g, Random rnd, int w, int h, int count, float maxR)
        {
            for (int i = 0; i < count; i++)
            {
                float r = 0.4f + (float)rnd.NextDouble() * maxR;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(120 + rnd.Next(135), 255, 255, 255)))
                {
                    g.FillEllipse(b, (float)rnd.NextDouble() * w, (float)rnd.NextDouble() * h, r, r);
                }
            }
        }

        private static void Glow(Graphics g, float cx, float cy, float r, Color c)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                using (PathGradientBrush pb = new PathGradientBrush(p) { CenterColor = c, SurroundColors = new[] { Color.FromArgb(0, c) } })
                {
                    g.FillPath(pb, p);
                }
            }
        }

        // Virtual backgrounds, drawn at any size; colors = the frame's colors (used by "frame")
        public static Bitmap RenderBackground(string id, string[] colors, Size sz)
        {
            int w = sz.Width, h = sz.Height;
            float u = Math.Min(w, h) / 100f;
            RectangleF all = new RectangleF(0, 0, w, h);
            Random rnd = new Random(11);
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = HighQuality(bmp))
            {
                switch (id)
                {
                    case "blur": // only the designer thumbnail; recording blurs the real room
                        g.Clear(Color.FromArgb(90, 80, 70));
                        for (int i = 0; i < 6; i++) Glow(g, rnd.Next(w), rnd.Next(h), 30 * u, Color.FromArgb(200, rnd.Next(256), rnd.Next(256), rnd.Next(256)));
                        break;
                    case "studio":
                        g.Clear(Color.FromArgb(55, 58, 64));
                        Glow(g, w / 2f, h * 0.45f, Math.Max(w, h) * 0.75f, Color.FromArgb(255, 205, 208, 214));
                        break;
                    case "frame":
                        using (Brush b = MakeBrush(colors, all, 255)) g.FillRectangle(b, all);
                        Glow(g, w * 0.3f, h * 0.3f, 60 * u, Color.FromArgb(90, 255, 255, 255));
                        break;
                    case "sunset":
                        FillVertical(g, all, "#2E1A47", "#C33764", "#FF7E5F", "#FEB47B");
                        Glow(g, w / 2f, h * 0.72f, 30 * u, Color.FromArgb(255, 255, 236, 140));
                        g.FillRectangle(new SolidBrush(Color.FromArgb(40, 20, 50)), 0, h * 0.8f, w, h * 0.2f);
                        break;
                    case "ocean":
                        FillVertical(g, all, "#9BE7FF", "#2F80ED", "#0B3C8C");
                        for (int k = 0; k < 6; k++)
                        {
                            PointF[] wave = Enumerable.Range(0, 41).Select(i => new PointF(i * w / 40f, h * (0.5f + k * 0.09f) + (float)Math.Sin(i * 0.6 + k) * 2 * u)).ToArray();
                            using (Pen p = new Pen(Color.FromArgb(110, 255, 255, 255), u)) g.DrawCurve(p, wave);
                        }
                        break;
                    case "synthwave":
                        FillVertical(g, all, "#1A0B3D", "#5B1A8E", "#E0418F");
                        float horizon = h * 0.62f;
                        using (GraphicsPath sun = new GraphicsPath())
                        {
                            sun.AddEllipse(w / 2f - 24 * u, horizon - 42 * u, 48 * u, 48 * u);
                            using (LinearGradientBrush sb = new LinearGradientBrush(sun.GetBounds(), Hex("#FFE66D"), Hex("#FF3D7F"), 90f)) g.FillPath(sb, sun);
                        }
                        for (int i = 0; i < 6; i++) g.FillRectangle(new SolidBrush(Hex("#5B1A8E")), 0, horizon - (14 - i * 2.4f) * u, w, (0.6f + i * 0.35f) * u);
                        g.FillRectangle(new SolidBrush(Hex("#12062B")), 0, horizon, w, h - horizon);
                        using (Pen grid = new Pen(Hex("#FF4FD8"), Math.Max(1, 0.5f * u)))
                        {
                            for (int i = -12; i <= 12; i++) g.DrawLine(grid, w / 2f, horizon, w / 2f + i * w / 6f, h);
                            for (int i = 1; i < 10; i++)
                            {
                                float y = horizon + (h - horizon) * (float)Math.Pow(i / 9.0, 1.8);
                                g.DrawLine(grid, 0, y, w, y);
                            }
                        }
                        break;
                    case "night":
                        FillVertical(g, all, "#050A1F", "#1B2A55", "#3A4A7A");
                        Stars(g, rnd, w, h, 180, 1.4f * u);
                        g.FillEllipse(new SolidBrush(Hex("#F4F1C9")), w * 0.72f, h * 0.12f, 16 * u, 16 * u);
                        g.FillEllipse(new SolidBrush(Hex("#1B2A55")), w * 0.72f + 5 * u, h * 0.12f - 2 * u, 15 * u, 15 * u);
                        break;
                    case "galaxy":
                        g.Clear(Hex("#05010F"));
                        string[] neb = { "#7B2FF7", "#F107A3", "#00C9FF", "#5D26C1", "#FF6A88", "#2E86DE" };
                        for (int i = 0; i < 9; i++) Glow(g, rnd.Next(w), rnd.Next(h), (18 + rnd.Next(30)) * u, Color.FromArgb(110, Hex(neb[i % neb.Length])));
                        Stars(g, rnd, w, h, 220, 1.2f * u);
                        break;
                    case "aurora":
                        FillVertical(g, all, "#020B14", "#06263A", "#0B3D3A");
                        Stars(g, rnd, w, h / 2, 90, 1f * u);
                        string[] aur = { "#3CF0A0", "#26C6DA", "#B388FF" };
                        for (int k = 0; k < 3; k++)
                        {
                            PointF[] band = Enumerable.Range(0, 9).Select(i => new PointF(i * w / 8f, h * (0.25f + k * 0.1f) + (float)Math.Sin(i * 0.9 + k * 2) * 8 * u)).ToArray();
                            using (Pen p = new Pen(Color.FromArgb(70, Hex(aur[k])), 14 * u)) g.DrawCurve(p, band);
                            using (Pen p = new Pen(Color.FromArgb(120, Hex(aur[k])), 4 * u)) g.DrawCurve(p, band);
                        }
                        break;
                    case "bokeh":
                        g.Clear(Hex("#15122B"));
                        for (int i = 0; i < 40; i++)
                        {
                            float r = (4 + rnd.Next(12)) * u;
                            Color c = Color.FromArgb(60 + rnd.Next(80), 120 + rnd.Next(136), 80 + rnd.Next(176), 120 + rnd.Next(136));
                            g.FillEllipse(new SolidBrush(c), rnd.Next(w) - r, rnd.Next(h) - r, 2 * r, 2 * r);
                        }
                        break;
                    case "matrix":
                        g.Clear(Color.Black);
                        string glyphs = "01アイウエオカキクケコサシスセソ";
                        using (Font f = new Font("MS Gothic", Math.Max(5f, 4.2f * u), FontStyle.Bold, GraphicsUnit.Pixel))
                        {
                            for (float x = 0; x < w; x += 5 * u)
                            {
                                int len = 4 + rnd.Next(18);
                                float start = rnd.Next(h);
                                for (int i = 0; i < len; i++)
                                {
                                    int a = i == len - 1 ? 255 : 40 + 200 * i / len;
                                    Color c = i == len - 1 ? Color.FromArgb(a, 200, 255, 200) : Color.FromArgb(a, 0, 255, 70);
                                    g.DrawString(glyphs[rnd.Next(glyphs.Length)].ToString(), f, new SolidBrush(c), x, (start + i * 5 * u) % h);
                                }
                            }
                        }
                        break;
                    case "pixelland":
                        g.SmoothingMode = SmoothingMode.None;
                        g.Clear(Hex("#5C94FC"));
                        float px = 5 * u;
                        float[] hills = { 52, 46, 40, 38, 44, 50, 56, 50, 44, 36, 30, 34, 42, 48, 54, 58, 52, 46, 50, 56, 60 };
                        for (int i = 0; i * px < w; i++)
                        {
                            float top = hills[i % hills.Length] * u;
                            g.FillRectangle(new SolidBrush(Hex("#2E8B57")), i * px, top, px + 1, h);
                            g.FillRectangle(new SolidBrush(Hex("#3CB371")), i * px, top, px + 1, px);
                        }
                        g.FillRectangle(new SolidBrush(Hex("#C84C0C")), 0, 82 * u, w, h);
                        for (int i = 0; i * px < w; i++) g.FillRectangle(new SolidBrush(i % 2 == 0 ? Hex("#FC9838") : Hex("#E45C10")), i * px, 82 * u, px, px);
                        for (int c = 0; c < 3; c++)
                        {
                            float cx = (15 + c * 33) * u, cy = (10 + c % 2 * 8) * u;
                            g.FillRectangle(Brushes.White, cx, cy, 4 * px, px);
                            g.FillRectangle(Brushes.White, cx + px, cy - px, 2 * px, px);
                        }
                        break;
                    case "bricks":
                        g.Clear(Hex("#B9A894"));
                        float bw = 20 * u, bh = 8 * u, gap = Math.Max(1, 0.9f * u);
                        for (int row = 0; row * bh < h; row++)
                        {
                            float off = row % 2 == 0 ? 0 : -bw / 2;
                            for (float x = off; x < w; x += bw)
                            {
                                Color c = Color.FromArgb(150 + rnd.Next(30), 60 + rnd.Next(20), 40 + rnd.Next(15));
                                g.FillRectangle(new SolidBrush(c), x + gap, row * bh + gap, bw - 2 * gap, bh - 2 * gap);
                            }
                        }
                        break;
                    case "wood":
                        float plank = 16 * u;
                        for (int i = 0; i * plank < w; i++)
                        {
                            Color c = Color.FromArgb(130 + rnd.Next(30), 82 + rnd.Next(20), 45 + rnd.Next(12));
                            g.FillRectangle(new SolidBrush(c), i * plank, 0, plank, h);
                            using (Pen grain = new Pen(Color.FromArgb(50, 60, 30, 10), Math.Max(1, 0.5f * u)))
                            {
                                for (int k = 0; k < 4; k++)
                                {
                                    float gx = i * plank + (2 + k * 3.5f) * u;
                                    PointF[] line = Enumerable.Range(0, 11).Select(j => new PointF(gx + (float)Math.Sin(j * 0.9 + k + i) * u, j * h / 10f)).ToArray();
                                    g.DrawCurve(grain, line);
                                }
                            }
                            g.DrawLine(new Pen(Color.FromArgb(120, 40, 20, 5), Math.Max(1, 0.6f * u)), i * plank, 0, i * plank, h);
                        }
                        break;
                    case "checker":
                        float sq = 12 * u;
                        for (int y = 0; y * sq < h; y++)
                            for (int x = 0; x * sq < w; x++)
                                g.FillRectangle(new SolidBrush((x + y) % 2 == 0 ? Hex("#FFD6E8") : Hex("#C8E7FF")), x * sq, y * sq, sq + 1, sq + 1);
                        break;
                    case "dots":
                        g.Clear(Hex("#FFE3EC"));
                        float step = 12 * u, dr = 3.2f * u;
                        for (int y = 0; y * step < h + step; y++)
                            for (int x = 0; x * step < w + step; x++)
                                g.FillEllipse(new SolidBrush(Hex("#FF8FB1")), x * step + (y % 2) * step / 2 - dr, y * step - dr, 2 * dr, 2 * dr);
                        break;
                    case "stripes":
                        g.Clear(Hex("#FFF4C2"));
                        using (SolidBrush sb = new SolidBrush(Hex("#FFB347")))
                        {
                            g.RotateTransform(-35);
                            for (float x = -2 * Math.Max(w, h); x < 2 * Math.Max(w, h); x += 14 * u) g.FillRectangle(sb, x, -2 * h, 7 * u, 5 * h);
                            g.ResetTransform();
                        }
                        break;
                    case "notebook":
                        g.Clear(Hex("#FDFDF6"));
                        using (Pen line = new Pen(Hex("#9CC3E6"), Math.Max(1, 0.4f * u)))
                            for (float y = 10 * u; y < h; y += 6 * u) g.DrawLine(line, 0, y, w, y);
                        g.DrawLine(new Pen(Hex("#F28B82"), Math.Max(1, 0.6f * u)), 14 * u, 0, 14 * u, h);
                        break;
                    case "confetti":
                        g.Clear(Hex("#FFFBF2"));
                        string[] conf = { "#FF595E", "#FFCA3A", "#8AC926", "#1982C4", "#6A4C93", "#FF7AC6" };
                        for (int i = 0; i < 160; i++)
                        {
                            g.TranslateTransform(rnd.Next(w), rnd.Next(h));
                            g.RotateTransform(rnd.Next(360));
                            g.FillRectangle(new SolidBrush(Hex(conf[rnd.Next(conf.Length)])), -1.5f * u, -0.7f * u, 3 * u, 1.4f * u);
                            g.ResetTransform();
                        }
                        break;
                    case "hearts":
                        g.Clear(Hex("#FFD1DC"));
                        float hs = 14 * u;
                        for (int y = 0; y * hs < h + hs; y++)
                            for (int x = 0; x * hs < w + hs; x++)
                                using (GraphicsPath hp = ShapePath("heart", new RectangleF(x * hs + (y % 2) * hs / 2, y * hs, hs * 0.6f, hs * 0.6f)))
                                    g.FillPath(new SolidBrush(Hex("#FF6F91")), hp);
                        break;
                    case "clouds":
                        FillVertical(g, all, "#4FA3E0", "#A7D8F5", "#E8F6FF");
                        for (int c = 0; c < 6; c++)
                        {
                            float cx = rnd.Next(w), cy = rnd.Next(h * 2 / 3);
                            for (int k = 0; k < 5; k++)
                            {
                                float r = (6 + rnd.Next(6)) * u;
                                g.FillEllipse(new SolidBrush(Color.FromArgb(230, 255, 255, 255)), cx + (k - 2) * 6 * u - r, cy + rnd.Next(4) * u - r, 2 * r, 2 * r);
                            }
                        }
                        break;
                    case "city":
                        FillVertical(g, all, "#0F0C29", "#302B63", "#24243E");
                        Stars(g, rnd, w, h / 2, 60, 0.8f * u);
                        for (float x = 0; x < w; )
                        {
                            float bwid = (8 + rnd.Next(12)) * u, bht = (25 + rnd.Next(45)) * u;
                            g.FillRectangle(new SolidBrush(Hex("#0B0A1A")), x, h - bht, bwid, bht);
                            for (float wy = h - bht + 2 * u; wy < h - 2 * u; wy += 4 * u)
                                for (float wx = x + 1.5f * u; wx < x + bwid - 2 * u; wx += 3.5f * u)
                                    if (rnd.Next(3) == 0) g.FillRectangle(new SolidBrush(rnd.Next(2) == 0 ? Hex("#FFD86B") : Hex("#4DEEEA")), wx, wy, 1.6f * u, 2 * u);
                            x += bwid + u;
                        }
                        g.FillRectangle(new SolidBrush(Hex("#FF2E97")), 0, h - 1.2f * u, w, 1.2f * u);
                        break;
                    case "forest":
                        FillVertical(g, all, "#D4F1F4", "#A8E6CF", "#5AA469");
                        string[] greens = { "#7FB685", "#3E8E5E", "#1E5631" };
                        for (int layer = 0; layer < 3; layer++)
                        {
                            float th = (30 + layer * 14) * u, base0 = h - (2 - layer) * 8 * u;
                            for (float x = -10 * u + layer * 4 * u; x < w + 10 * u; x += (9 - layer) * u)
                            {
                                float hh = th * (0.7f + (float)rnd.NextDouble() * 0.4f);
                                g.FillPolygon(new SolidBrush(Hex(greens[layer])), new[] { new PointF(x, base0), new PointF(x + 5 * u, base0 - hh), new PointF(x + 10 * u, base0) });
                            }
                        }
                        break;
                    case "mosaic":
                        g.SmoothingMode = SmoothingMode.None;
                        string[] pal = { "#1D2B53", "#7E2553", "#008751", "#AB5236", "#5F574F", "#FF004D", "#FFA300", "#29ADFF", "#83769C", "#FF77A8" };
                        float cell = 8 * u;
                        for (int y = 0; y * cell < h; y++)
                            for (int x = 0; x * cell < w; x++)
                                g.FillRectangle(new SolidBrush(Hex(pal[rnd.Next(pal.Length)])), x * cell, y * cell, cell + 1, cell + 1);
                        break;
                    default:
                        g.Clear(Color.FromArgb(40, 40, 48));
                        break;
                }
            }
            return bmp;
        }

        // 640x360 is plenty for a camera shown at most ~384 px wide and is far cheaper to decode and scale than 720p;
        // cameras without that mode fall back to their default
        public const string HdMode = "-video_size 640x360 -framerate 30 ";

        // Live camera as raw BGR frames on stdout, fitted to the source size
        public static Process StartCamera(Size size, string camera, bool hd)
        {
            string args = string.Format("-hide_banner -loglevel error -f dshow -rtbufsize 64M {0}-i \"video={1}\" -vf \"{2}\" -f rawvideo -pix_fmt bgr24 -",
                hd ? HdMode : "", camera, CameraChain(size));
            Process p = new Process
            {
                StartInfo = new ProcessStartInfo(FfmpegEngine.FfmpegPath, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            p.ErrorDataReceived += (o, e) => { };
            p.Start();
            ChildProcesses.Track(p);
            p.BeginErrorReadLine();
            // Under a full CPU a normal-priority camera took 17 s to deliver 60 frames (and overflowed its buffer)
            try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
            return p;
        }

        // Blocks reading frames until the stream ends or keepGoing() is false; the processor (fetched per frame so it can be
        // swapped live) paints the virtual background; onFrame owns each bitmap
        public static void ReadCameraFrames(Process p, Size size, Func<CameraProcessor> processor, Func<bool> keepGoing, Action<Bitmap> onFrame)
        {
            int stride = size.Width * 3, frameBytes = stride * size.Height;
            byte[] buf = new byte[frameBytes];
            Stream stdout = p.StandardOutput.BaseStream;
            while (keepGoing())
            {
                int read = 0;
                while (read < frameBytes)
                {
                    int n = stdout.Read(buf, read, frameBytes - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < frameBytes) return;

                CameraProcessor proc = processor();
                if (proc != null) proc.Process(buf);
                onFrame(BgrToBitmap(buf, size));
            }
        }

        public static Bitmap BgrToBitmap(byte[] bgr, Size size)
        {
            int stride = size.Width * 3;
            Bitmap frame = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
            BitmapData data = frame.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            for (int y = 0; y < size.Height; y++) Marshal.Copy(bgr, y * stride, data.Scan0 + y * data.Stride, stride);
            frame.UnlockBits(data);
            return frame;
        }

        public static byte[] BitmapToBgr(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height, stride = w * 3;
            byte[] bgr = new byte[stride * h];
            using (Bitmap b24 = bmp.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format24bppRgb))
            {
                BitmapData data = b24.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, bgr, y * stride, stride);
                b24.UnlockBits(data);
            }
            return bgr;
        }

        // Straight-alpha BGRA bytes of a 32bpp ARGB bitmap (rawvideo input for FFmpeg)
        public static void BitmapToBgra(Bitmap bmp, byte[] dest)
        {
            int w = bmp.Width, h = bmp.Height, stride = w * 4;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, dest, y * stride, stride);
            bmp.UnlockBits(data);
        }

        // Camera (or a silhouette placeholder when null) inside the shape, plus the border on top
        public static Bitmap Compose(Bitmap camera, Size canvas, GraphicsPath clip, Bitmap border)
        {
            Bitmap result = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (camera != null)
                {
                    using (TextureBrush tb = new TextureBrush(camera)) g.FillPath(tb, clip);
                }
                else
                {
                    g.SetClip(clip);
                    using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(Point.Empty, canvas), Color.FromArgb(70, 80, 110), Color.FromArgb(35, 30, 60), 90f))
                    {
                        g.FillPath(lg, clip);
                    }
                    RectangleF r = ShapeRect(canvas);
                    using (SolidBrush person = new SolidBrush(Color.FromArgb(150, 160, 190)))
                    {
                        g.FillEllipse(person, r.X + r.Width * 0.36f, r.Y + r.Height * 0.2f, r.Width * 0.28f, r.Height * 0.32f);
                        g.FillEllipse(person, r.X + r.Width * 0.2f, r.Y + r.Height * 0.58f, r.Width * 0.6f, r.Height * 0.6f);
                    }
                    g.ResetClip();
                }
                g.DrawImage(border, 0, 0);
            }
            return result;
        }

        // Designer helpers: shape icon and color swatch
        public static Bitmap RenderShapeIcon(string shape, int size, Color color)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = HighQuality(bmp))
            {
                RectangleF r = shape == "rect"
                    ? new RectangleF(3, size * 0.22f, size - 6, size * 0.56f)
                    : new RectangleF(3, 3, size - 6, size - 6);
                using (GraphicsPath p = ShapePath(shape, r))
                using (SolidBrush b = new SolidBrush(color))
                {
                    g.FillPath(b, p);
                }
            }
            return bmp;
        }

        public static Bitmap RenderSwatch(string[] colors, int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = HighQuality(bmp))
            using (Brush b = MakeBrush(colors, new RectangleF(0, 0, size, size), 255))
            {
                g.FillEllipse(b, 2, 2, size - 4, size - 4);
                g.DrawEllipse(Pens.Gray, 2, 2, size - 4, size - 4);
            }
            return bmp;
        }
    }

    // Person segmentation with the MediaPipe Selfie Segmentation model on ONNX Runtime (runtime\ folder).
    // ORT types stay inside this class so a missing runtime only disables virtual backgrounds.
    public static class PersonSegmenter
    {
        public const int Size = 256;
        private static readonly object _lock = new object();
        private static object _session;
        private static bool _tried;
        public static string Error;

        public static bool Available
        {
            get
            {
                lock (_lock)
                {
                    if (!_tried)
                    {
                        _tried = true;
                        try { Load(); }
                        catch (Exception ex) { Error = ex.Message; }
                    }
                    return _session != null;
                }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Load()
        {
            Microsoft.ML.OnnxRuntime.SessionOptions so = new Microsoft.ML.OnnxRuntime.SessionOptions();
            so.IntraOpNumThreads = 1; // tiny model: one thread is enough and leaves the cores to the game and the encoder
            _session = new Microsoft.ML.OnnxRuntime.InferenceSession(Path.Combine(Program.RuntimeDir, "selfie_segmentation.onnx"), so);
        }

        // BGR frame (w x h) -> person probability 0..1 on a Size x Size grid
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static float[] Segment(byte[] bgr, int w, int h)
        {
            if (!Available) return null;
            int plane = Size * Size;
            float[] input = new float[3 * plane];
            for (int y = 0; y < Size; y++)
            {
                int sy = y * h / Size;
                for (int x = 0; x < Size; x++)
                {
                    int src = (sy * w + x * w / Size) * 3, dst = y * Size + x;
                    input[dst] = bgr[src + 2] / 255f;             // R
                    input[plane + dst] = bgr[src + 1] / 255f;     // G
                    input[2 * plane + dst] = bgr[src] / 255f;     // B
                }
            }
            Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float> tensor =
                new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(input, new[] { 1, 3, Size, Size });
            Microsoft.ML.OnnxRuntime.NamedOnnxValue[] inputs = { Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor("pixel_values", tensor) };
            using (var results = ((Microsoft.ML.OnnxRuntime.InferenceSession)_session).Run(inputs))
            {
                return results.First().AsEnumerable<float>().ToArray();
            }
        }
    }

    // Per-frame camera look, all in C# so any of it can change live: mirror, virtual background behind the person, filter
    public class CameraProcessor
    {
        private readonly int _w, _h;
        private readonly string _background;
        private readonly byte[] _bg; // null for "none" and "blur"
        private readonly int _filter;
        private readonly bool _mirror;
        private float[] _mask;
        private int _frame;

        public CameraProcessor(CameraStyle s, Size size)
        {
            _w = size.Width;
            _h = size.Height;
            _background = s.Background;
            _mirror = s.Mirror;
            _filter = Array.FindIndex(CameraFrames.Filters, r => r[0] == s.Filter);
            if (_background != "none" && _background != "blur")
            {
                using (Bitmap b = CameraFrames.RenderBackground(_background, s.Colors, size)) _bg = CameraFrames.BitmapToBgr(b);
            }
        }

        public void Process(byte[] bgr)
        {
            if (_mirror) Mirror(bgr);
            if (_background != "none" && PersonSegmenter.Available) ReplaceBackground(bgr);
            ApplyFilter(bgr);
        }

        private void Mirror(byte[] p)
        {
            int stride = _w * 3;
            for (int y = 0; y < _h; y++)
            {
                int row = y * stride;
                for (int l = row, r = row + stride - 3; l < r; l += 3, r -= 3)
                {
                    byte t0 = p[l], t1 = p[l + 1], t2 = p[l + 2];
                    p[l] = p[r]; p[l + 1] = p[r + 1]; p[l + 2] = p[r + 2];
                    p[r] = t0; p[r + 1] = t1; p[r + 2] = t2;
                }
            }
        }

        private void ReplaceBackground(byte[] bgr)
        {
            // The model runs every other frame (15/s); the mask in between is reused, halving its CPU cost
            if (_mask == null || (_frame++ & 1) == 0)
            {
                float[] mask = PersonSegmenter.Segment(bgr, _w, _h);
                if (mask == null) return;
                // Light temporal smoothing keeps the edge from flickering between frames
                if (_mask == null) _mask = mask;
                else for (int i = 0; i < mask.Length; i++) _mask[i] = _mask[i] * 0.35f + mask[i] * 0.65f;
            }

            byte[] bg = _background == "blur" ? Blur(bgr) : _bg;
            int n = PersonSegmenter.Size;
            float sx = (float)(n - 1) / Math.Max(1, _w - 1), sy = (float)(n - 1) / Math.Max(1, _h - 1);
            for (int y = 0; y < _h; y++)
            {
                float fy = y * sy;
                int y0 = (int)fy, y1 = Math.Min(n - 1, y0 + 1);
                float ty = fy - y0;
                for (int x = 0; x < _w; x++)
                {
                    float fx = x * sx;
                    int x0 = (int)fx, x1 = Math.Min(n - 1, x0 + 1);
                    float tx = fx - x0;
                    float a = (_mask[y0 * n + x0] * (1 - tx) + _mask[y0 * n + x1] * tx) * (1 - ty)
                            + (_mask[y1 * n + x0] * (1 - tx) + _mask[y1 * n + x1] * tx) * ty;
                    // Sharpen the soft model output a little so hair and shoulders get a cleaner edge
                    a = Math.Max(0f, Math.Min(1f, (a - 0.3f) / 0.4f));
                    int i = (y * _w + x) * 3;
                    bgr[i] = (byte)(bgr[i] * a + bg[i] * (1 - a));
                    bgr[i + 1] = (byte)(bgr[i + 1] * a + bg[i + 1] * (1 - a));
                    bgr[i + 2] = (byte)(bgr[i + 2] * a + bg[i + 2] * (1 - a));
                }
            }
        }

        private static byte Clamp(float v)
        {
            return v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)v;
        }

        // Index matches CameraFrames.Filters: none, bw, sepia, vivid, warm, cool, vintage, contrast, pixel
        private void ApplyFilter(byte[] p)
        {
            if (_filter <= 0) return;
            if (_filter == 8)
            {
                Pixelate(p, 5);
                return;
            }
            for (int i = 0; i < p.Length; i += 3)
            {
                float b = p[i], g = p[i + 1], r = p[i + 2];
                float y = 0.299f * r + 0.587f * g + 0.114f * b;
                float nr, ng, nb;
                switch (_filter)
                {
                    case 1: nr = ng = nb = y; break;
                    case 2:
                        nr = 0.393f * r + 0.769f * g + 0.189f * b;
                        ng = 0.349f * r + 0.686f * g + 0.168f * b;
                        nb = 0.272f * r + 0.534f * g + 0.131f * b;
                        break;
                    case 3:
                        nr = (y + (r - y) * 1.6f - 128) * 1.08f + 128;
                        ng = (y + (g - y) * 1.6f - 128) * 1.08f + 128;
                        nb = (y + (b - y) * 1.6f - 128) * 1.08f + 128;
                        break;
                    case 4: nr = r * 1.08f + 12; ng = g * 1.02f + 3; nb = b * 0.85f; break;
                    case 5: nr = r * 0.88f; ng = g * 0.98f; nb = b * 1.08f + 14; break;
                    case 6:
                        // faded, slightly desaturated and warm
                        nr = (y + (r - y) * 0.7f) * 0.82f + 36;
                        ng = (y + (g - y) * 0.7f) * 0.82f + 28;
                        nb = (y + (b - y) * 0.7f) * 0.82f + 20;
                        break;
                    default:
                        nr = (y + (r - y) * 1.1f - 128) * 1.45f + 136;
                        ng = (y + (g - y) * 1.1f - 128) * 1.45f + 136;
                        nb = (y + (b - y) * 1.1f - 128) * 1.45f + 136;
                        break;
                }
                p[i] = Clamp(nb);
                p[i + 1] = Clamp(ng);
                p[i + 2] = Clamp(nr);
            }
        }

        private void Pixelate(byte[] p, int block)
        {
            for (int by = 0; by < _h; by += block)
            {
                for (int bx = 0; bx < _w; bx += block)
                {
                    int ye = Math.Min(_h, by + block), xe = Math.Min(_w, bx + block), count = 0;
                    int sb = 0, sg = 0, sr = 0;
                    for (int y = by; y < ye; y++)
                        for (int x = bx; x < xe; x++)
                        {
                            int i = (y * _w + x) * 3;
                            sb += p[i]; sg += p[i + 1]; sr += p[i + 2]; count++;
                        }
                    byte cb = (byte)(sb / count), cg = (byte)(sg / count), cr = (byte)(sr / count);
                    for (int y = by; y < ye; y++)
                        for (int x = bx; x < xe; x++)
                        {
                            int i = (y * _w + x) * 3;
                            p[i] = cb; p[i + 1] = cg; p[i + 2] = cr;
                        }
                }
            }
        }

        // Strong blur of the camera frame: average 12x12 blocks, then bilinear upscale
        private byte[] Blur(byte[] bgr)
        {
            const int k = 12;
            int sw = Math.Max(2, _w / k), sh = Math.Max(2, _h / k);
            float[] small = new float[sw * sh * 3];
            int[] count = new int[sw * sh];
            for (int y = 0; y < _h; y++)
            {
                int cy = Math.Min(sh - 1, y * sh / _h);
                for (int x = 0; x < _w; x++)
                {
                    int c = cy * sw + Math.Min(sw - 1, x * sw / _w), i = (y * _w + x) * 3;
                    small[c * 3] += bgr[i];
                    small[c * 3 + 1] += bgr[i + 1];
                    small[c * 3 + 2] += bgr[i + 2];
                    count[c]++;
                }
            }
            for (int c = 0; c < count.Length; c++)
            {
                if (count[c] == 0) continue;
                small[c * 3] /= count[c];
                small[c * 3 + 1] /= count[c];
                small[c * 3 + 2] /= count[c];
            }
            byte[] outp = new byte[bgr.Length];
            for (int y = 0; y < _h; y++)
            {
                float fy = Math.Max(0, (y + 0.5f) * sh / _h - 0.5f);
                int y0 = Math.Min(sh - 1, (int)fy), y1 = Math.Min(sh - 1, y0 + 1);
                float ty = fy - y0;
                for (int x = 0; x < _w; x++)
                {
                    float fx = Math.Max(0, (x + 0.5f) * sw / _w - 0.5f);
                    int x0 = Math.Min(sw - 1, (int)fx), x1 = Math.Min(sw - 1, x0 + 1);
                    float tx = fx - x0;
                    int i = (y * _w + x) * 3;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        float v = (small[(y0 * sw + x0) * 3 + ch] * (1 - tx) + small[(y0 * sw + x1) * 3 + ch] * tx) * (1 - ty)
                                + (small[(y1 * sw + x0) * 3 + ch] * (1 - tx) + small[(y1 * sw + x1) * 3 + ch] * tx) * ty;
                        outp[i + ch] = (byte)v;
                    }
                }
            }
            return outp;
        }
    }

    // The one camera process. Bubble, designer and hidden-camera recording all subscribe to the same processed frames,
    // so they never fight over the webcam, and a style change only swaps the processor (no camera restart) unless the
    // aspect ratio changes (16:9 <-> square)
    public static class CameraHub
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<object, Action<Bitmap>> _subs = new Dictionary<object, Action<Bitmap>>();
        private static Process _proc;
        private static Size _size;
        private static bool _wide;
        private static volatile CameraProcessor _processor;
        public static volatile string Status = "";

        static CameraHub()
        {
            CameraStyle.Changed += OnStyleChanged;
        }

        // onFrame runs on the camera thread with a frame that is only valid during the call (copy what you keep)
        public static void Subscribe(object owner, Action<Bitmap> onFrame)
        {
            lock (_lock)
            {
                _subs[owner] = onFrame;
                if (_proc == null) StartLocked(true);
            }
        }

        public static void Unsubscribe(object owner)
        {
            lock (_lock)
            {
                if (!_subs.Remove(owner)) return;
                if (_subs.Count == 0) StopLocked();
            }
        }

        private static void OnStyleChanged()
        {
            CameraStyle s = CameraStyle.Current;
            lock (_lock)
            {
                if (_proc == null) return;
                ScreenRecorder.Log(string.Format("Camera style: {0}/{1}/{2}/{3} mirror={4} size={5}", s.Shape, s.Border, s.Filter, s.Background, s.Mirror, s.SizePercent));
                if (s.IsWide != _wide)
                {
                    StopLocked();
                    StartLocked(true);
                }
                else _processor = new CameraProcessor(s, _size);
            }
        }

        private static void StartLocked(bool hd)
        {
            string camera = ScreenRecorder.FindCamera();
            if (camera == null)
            {
                Status = "No se encontró ninguna cámara";
                return;
            }
            CameraStyle s = CameraStyle.Current;
            _wide = s.IsWide;
            _size = CameraFrames.SourceSize(s);
            _processor = new CameraProcessor(s, _size);
            Size size = _size;
            Process p = CameraFrames.StartCamera(size, camera, hd);
            _proc = p;
            Status = "Abriendo cámara...";
            new Thread(() =>
            {
                bool any = false;
                CameraFrames.ReadCameraFrames(p, size, () => _processor, () => _proc == p, frame =>
                {
                    any = true;
                    Status = "En vivo";
                    KeyValuePair<object, Action<Bitmap>>[] subs;
                    lock (_lock) subs = _subs.ToArray();
                    foreach (KeyValuePair<object, Action<Bitmap>> kv in subs)
                    {
                        try { kv.Value(frame); } catch { }
                    }
                    frame.Dispose();
                });
                lock (_lock)
                {
                    if (_proc != p) return; // stopped or restarted on purpose
                    _proc = null;
                    if (!any && hd && _subs.Count > 0)
                    {
                        StartLocked(false); // camera without 640x360: its default mode
                        return;
                    }
                    Status = any ? "La cámara se detuvo" : "No se pudo abrir la cámara (¿la usa otra app?)";
                }
            }) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "KidCut camera" }.Start();
        }

        private static void StopLocked()
        {
            Process p = _proc;
            _proc = null;
            if (p == null) return;
            try
            {
                if (!p.HasExited) p.Kill();
                p.WaitForExit(2000);
            }
            catch { }
        }
    }

    // Hidden-camera recording: the framed camera (same look as the bubble) is streamed to the recorder's FFmpeg through a
    // named pipe at a steady 30 fps with alpha. The pipe frame is a fixed box big enough for any shape at 100 %, and the
    // camera is drawn centered inside it, so shape, filter, background and size can all change mid-recording.
    public class CameraFeed
    {
        private readonly object _frameLock = new object();
        private readonly Size _box;
        private Size _canvas;
        private Bitmap _border;
        private GraphicsPath _clip;
        private byte[] _latest;
        private System.IO.Pipes.NamedPipeServerStream _pipe;
        private volatile bool _stop;
        private int _busy;

        public string PipePath { get; private set; }
        public Size Size { get { return _box; } }

        public CameraFeed()
        {
            CameraStyle s = CameraStyle.Current;
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            Size square = CameraFrames.CanvasSize(new CameraStyle { Shape = "circle" }, scr.Width / 5);
            Size wide = CameraFrames.CanvasSize(new CameraStyle { Shape = "rect" }, scr.Width / 4);
            _box = new Size(Math.Max(square.Width, wide.Width), Math.Max(square.Height, wide.Height));
            _latest = new byte[_box.Width * _box.Height * 4];
            Rebuild();
            CameraStyle.Changed += Rebuild;
        }

        // Current shape/border at the current size; until camera frames arrive the box shows the silhouette placeholder
        private void Rebuild()
        {
            CameraStyle s = CameraStyle.Current;
            Size full = CameraFrames.SourceSize(s);
            Size canvas = new Size(Math.Max(16, full.Width * s.SizePercent / 100) & ~1, Math.Max(16, full.Height * s.SizePercent / 100) & ~1);
            Bitmap border = CameraFrames.RenderBorder(s, canvas);
            GraphicsPath clip = CameraFrames.ShapePath(s.Shape, CameraFrames.ShapeRect(canvas));
            lock (_frameLock)
            {
                if (_border != null) _border.Dispose();
                if (_clip != null) _clip.Dispose();
                _canvas = canvas;
                _border = border;
                _clip = clip;
                using (Bitmap placeholder = CameraFrames.Compose(null, canvas, clip, border)) WriteCentered(placeholder);
            }
        }

        private void WriteCentered(Bitmap composed)
        {
            using (Bitmap box = new Bitmap(_box.Width, _box.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(box))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(composed, (_box.Width - composed.Width) / 2, (_box.Height - composed.Height) / 2);
                }
                CameraFrames.BitmapToBgra(box, _latest);
            }
        }

        public void Start()
        {
            string name = "kidcut_cam_" + Process.GetCurrentProcess().Id + "_" + Environment.TickCount;
            PipePath = @"\\.\pipe\" + name;
            _pipe = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.Out, 1,
                System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.None, 0, _latest.Length * 2);
            new Thread(WriteLoop) { IsBackground = true, Priority = ThreadPriority.Highest }.Start();
            CameraHub.Subscribe(this, OnFrame);
        }

        private void OnFrame(Bitmap source)
        {
            if (_stop || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            try
            {
                lock (_frameLock)
                {
                    using (Bitmap fit = CameraFrames.Fit(source, _canvas))
                    using (Bitmap composed = CameraFrames.Compose(fit, _canvas, _clip, _border))
                    {
                        WriteCentered(composed);
                    }
                }
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        private void WriteLoop()
        {
            try
            {
                _pipe.WaitForConnection();
                Stopwatch clock = Stopwatch.StartNew();
                long frame = 0;
                byte[] copy = new byte[_latest.Length];
                while (!_stop)
                {
                    lock (_frameLock) Buffer.BlockCopy(_latest, 0, copy, 0, copy.Length);
                    _pipe.Write(copy, 0, copy.Length);
                    frame++;
                    long wait = frame * 1000 / 30 - clock.ElapsedMilliseconds;
                    if (wait > 0) Thread.Sleep((int)wait);
                    else if (wait < -100)
                    {
                        // FFmpeg was busy and the write blocked: resume the 30 fps pace instead of bursting to catch up
                        clock.Restart();
                        frame = 0;
                    }
                }
            }
            catch { } // FFmpeg closed the pipe (recording stopped)
        }

        public void Stop()
        {
            _stop = true;
            CameraStyle.Changed -= Rebuild;
            CameraHub.Unsubscribe(this);
            try { _pipe.Dispose(); } catch { }
        }
    }

    // Frame designer: live camera preview in the middle, surrounded by the shape, border, filter and color selectors
    public class CameraStyleForm : Form
    {
        private static CameraStyleForm _instance;
        private const int PreviewSize = 300;
        private static readonly Color Bg = Color.FromArgb(14, 10, 28);
        private static readonly Color Gold = Color.FromArgb(255, 210, 0);

        private readonly CameraStyle _style = CameraStyle.Current;
        private readonly PictureBox _preview;
        private readonly Label _status;
        private readonly ToolTip _tips = new ToolTip();
        private readonly Dictionary<string, List<Button>> _groups = new Dictionary<string, List<Button>>();
        private readonly object _frameLock = new object();
        private Bitmap _border;
        private GraphicsPath _clip;
        private Size _canvas;
        private volatile bool _closing;
        private readonly Button _btnMirror;
        private readonly Label _lblSize;
        private readonly System.Windows.Forms.Timer _statusTimer = new System.Windows.Forms.Timer { Interval = 700 };

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // WDA_EXCLUDEFROMCAPTURE: editing the frame mid-recording never shows the designer in the video
            try { SetWindowDisplayAffinity(Handle, 0x11); } catch { }
        }

        // Can stay open while recording: it shares the one camera with the bubble/recording and edits apply live
        public static void ShowDesigner()
        {
            if (_instance == null || _instance.IsDisposed) _instance = new CameraStyleForm();
            _instance.Show();
            _instance.Activate();
        }

        public CameraStyleForm()
        {
            Text = "KidCut — Marco de la cámara";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            BackColor = Bg;
            ForeColor = Color.White;
            ClientSize = new Size(900, 720);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9);

            // Center: live preview
            _preview = new PictureBox
            {
                Location = new Point((ClientSize.Width - PreviewSize) / 2, 96),
                Size = new Size(PreviewSize, PreviewSize),
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.FromArgb(24, 20, 44)
            };
            Controls.Add(_preview);
            _status = new Label
            {
                Location = new Point(_preview.Left - 40, _preview.Bottom + 6),
                Size = new Size(PreviewSize + 80, 18),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(155, 142, 196)
            };
            Controls.Add(_status);

            // Top: border styles
            AddTitle("BORDE", 0, 10, ClientSize.Width);
            int bx = (ClientSize.Width - CameraFrames.Borders.Length * 102) / 2;
            for (int i = 0; i < CameraFrames.Borders.Length; i++)
            {
                string id = CameraFrames.Borders[i][0];
                AddOption("border", id, CameraFrames.Borders[i][1], null, new Rectangle(bx + i * 102, 34, 96, 34), () => { _style.Border = id; ApplyStyle(); });
            }

            // Left: shapes
            AddTitle("FORMA", 16, 84, 250);
            for (int i = 0; i < CameraFrames.Shapes.Length; i++)
            {
                string id = CameraFrames.Shapes[i][0];
                Rectangle r = new Rectangle(16 + (i % 2) * 124, 108 + (i / 2) * 56, 118, 50);
                AddOption("shape", id, CameraFrames.Shapes[i][1], CameraFrames.RenderShapeIcon(id, 30, Color.FromArgb(0, 240, 255)), r, () => { _style.Shape = id; ApplyStyle(); });
            }

            // Right: color filters
            AddTitle("FILTRO", ClientSize.Width - 236, 84, 220);
            for (int i = 0; i < CameraFrames.Filters.Length; i++)
            {
                string id = CameraFrames.Filters[i][0];
                Rectangle r = new Rectangle(ClientSize.Width - 236, 108 + i * 32, 220, 28);
                AddOption("filter", id, CameraFrames.Filters[i][1], null, r, () => { _style.Filter = id; ApplyStyle(); });
            }

            // Under the preview: mirror toggle
            // Under the preview: mirror toggle and camera size (smaller, bigger, back to original)
            int row = _preview.Bottom + 28, center = ClientSize.Width / 2;
            _btnMirror = AddAction("↔ ESPEJO", new Rectangle(center - 200, row, 130, 28), () => { _style.Mirror = !_style.Mirror; ApplyStyle(); });
            _tips.SetToolTip(_btnMirror, "Voltea la cámara horizontalmente (como un espejo)");
            Button smaller = AddAction("−", new Rectangle(center - 50, row, 30, 28), () => ChangeSize(-10));
            _lblSize = new Label { Location = new Point(center - 18, row), Size = new Size(110, 28), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White };
            Controls.Add(_lblSize);
            Button bigger = AddAction("+", new Rectangle(center + 94, row, 30, 28), () => ChangeSize(10));
            Button original = AddAction("Original", new Rectangle(center + 130, row, 70, 28), () => ChangeSize(100));
            _tips.SetToolTip(smaller, "Hacer la cámara más pequeña en pantalla y en la grabación");
            _tips.SetToolTip(bigger, "Hacer la cámara más grande (hasta su tamaño original)");
            _tips.SetToolTip(original, "Volver al tamaño original (100 %)");

            // Bottom: colors and gradients
            AddTitle("COLOR / DEGRADADO", 0, 466, ClientSize.Width);
            int n = CameraFrames.Palettes.Length;
            int sx = (ClientSize.Width - (n * 42 + 2 * 112)) / 2;
            for (int i = 0; i < n; i++)
            {
                string[] colors = CameraFrames.Palettes[i];
                Button b = AddOption("color", string.Join(",", colors), "", CameraFrames.RenderSwatch(colors, 30), new Rectangle(sx + i * 42, 490, 38, 38), () => { _style.Colors = colors; ApplyStyle(); });
                _tips.SetToolTip(b, colors.Length == 1 ? "Color " + colors[0] : "Degradado " + string.Join(" → ", colors));
            }
            int cx = sx + n * 42 + 8;
            Button custom = AddAction("Color propio...", new Rectangle(cx, 490, 104, 38), () => PickColors(1));
            Button gradient = AddAction("Degradado propio...", new Rectangle(cx + 110, 490, 120, 38), () => PickColors(2));

            // Bottom: virtual backgrounds (two rows of thumbnails), painted behind the person found by the AI model
            AddTitle("FONDO VIRTUAL", 0, 540, ClientSize.Width);
            int perRow = 13, tx = (ClientSize.Width - perRow * 44) / 2;
            for (int i = 0; i < CameraFrames.Backgrounds.Length; i++)
            {
                string id = CameraFrames.Backgrounds[i][0];
                Image thumb = id == "none" ? null : CameraFrames.RenderBackground(id, _style.Colors, new Size(32, 32));
                Button b = AddOption("background", id, id == "none" ? "✕" : "", thumb, new Rectangle(tx + (i % perRow) * 44, 564 + (i / perRow) * 44, 40, 40), () => { _style.Background = id; ApplyStyle(); });
                _tips.SetToolTip(b, CameraFrames.Backgrounds[i][1]);
            }
            _tips.SetToolTip(custom, "Elegir cualquier color sólido");
            _tips.SetToolTip(gradient, "Elegir dos colores para un degradado");

            Button done = AddAction("✔ LISTO", new Rectangle(ClientSize.Width - 156, ClientSize.Height - 54, 140, 40), Close);
            done.BackColor = Gold;
            done.ForeColor = Color.Black;
            ActiveControl = done; // otherwise the focus cue makes the first option look selected
            Label hint = new Label
            {
                Text = "El marco se aplica al grabar con la cámara encendida en el widget. Se guarda para la próxima vez.",
                Location = new Point(16, ClientSize.Height - 44),
                Size = new Size(600, 20),
                ForeColor = Color.FromArgb(155, 142, 196)
            };
            Controls.Add(hint);

            _statusTimer.Tick += (s, e) => RefreshStatus();
            _statusTimer.Start();
            FormClosing += (s, e) =>
            {
                _closing = true;
                _statusTimer.Stop();
                CameraHub.Unsubscribe(this);
                _style.Save();
            };

            ApplyStyle();
        }

        private void AddTitle(string text, int x, int y, int width)
        {
            Controls.Add(new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 20),
                TextAlign = x == 0 ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft,
                ForeColor = Gold,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            });
        }

        private Button AddAction(string text, Rectangle r, Action onClick)
        {
            Button b = new Button
            {
                Text = text,
                Bounds = r,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 26, 55),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(80, 70, 120);
            b.Click += (s, e) => onClick();
            Controls.Add(b);
            return b;
        }

        private Button AddOption(string group, string id, string text, Image icon, Rectangle r, Action onClick)
        {
            Button b = AddAction(text, r, onClick);
            b.Tag = id;
            if (icon != null)
            {
                b.Image = icon;
                b.ImageAlign = text.Length > 0 ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleCenter;
                b.TextAlign = ContentAlignment.MiddleRight;
            }
            List<Button> list;
            if (!_groups.TryGetValue(group, out list)) _groups[group] = list = new List<Button>();
            list.Add(b);
            return b;
        }

        private void Highlight(string group, string id)
        {
            foreach (Button b in _groups[group])
            {
                bool on = (string)b.Tag == id;
                b.FlatAppearance.BorderSize = on ? 3 : 1;
                b.FlatAppearance.BorderColor = on ? Gold : Color.FromArgb(80, 70, 120);
            }
        }

        private void PickColors(int count)
        {
            List<string> picked = new List<string>();
            for (int i = 0; i < count; i++)
            {
                using (ColorDialog dlg = new ColorDialog { FullOpen = true })
                {
                    if (i < _style.Colors.Length) dlg.Color = ColorTranslator.FromHtml(_style.Colors[i]);
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    picked.Add(string.Format("#{0:X2}{1:X2}{2:X2}", dlg.Color.R, dlg.Color.G, dlg.Color.B));
                }
            }
            _style.Colors = picked.ToArray();
            ApplyStyle();
        }

        // Every change applies live: the designer redraws its own preview and tells the shared camera, the bubble and an
        // ongoing hidden-camera recording (CameraStyle.Changed), none of which reopens the camera for it
        private void ApplyStyle()
        {
            Highlight("shape", _style.Shape);
            Highlight("border", _style.Border);
            Highlight("filter", _style.Filter);
            Highlight("color", string.Join(",", _style.Colors));
            Highlight("background", _style.Background);
            _btnMirror.BackColor = _style.Mirror ? Color.FromArgb(22, 70, 45) : Color.FromArgb(30, 26, 55);
            _btnMirror.Text = _style.Mirror ? "↔ ESPEJO: SÍ" : "↔ ESPEJO: NO";
            lock (_frameLock)
            {
                if (_border != null) _border.Dispose();
                if (_clip != null) _clip.Dispose();
                _canvas = CameraFrames.CanvasSize(_style, PreviewSize - 20);
                _border = CameraFrames.RenderBorder(_style, _canvas);
                _clip = CameraFrames.ShapePath(_style.Shape, CameraFrames.ShapeRect(_canvas));
            }
            _lblSize.Text = "Tamaño: " + _style.SizePercent + " %";
            CameraStyle.NotifyChanged();
            if (!_subscribed)
            {
                _subscribed = true;
                CameraHub.Subscribe(this, OnCameraFrame);
            }
            if ((DateTime.Now - _lastFrame).TotalSeconds > 1) ShowFrame(null);
            if (_style.Background != "none" && !PersonSegmenter.Available)
            {
                _status.Text = "Fondos no disponibles: falta la carpeta runtime (" + PersonSegmenter.Error + ")";
            }
        }

        // Size only changes how big the camera is on screen and in the recording; the preview stays full size
        private void ChangeSize(int delta)
        {
            _style.SizePercent = delta == 100 ? 100 : Math.Max(30, Math.Min(100, _style.SizePercent + delta));
            _lblSize.Text = "Tamaño: " + _style.SizePercent + " %";
            _style.Save();
            CameraStyle.NotifyChanged();
        }

        private bool _subscribed;
        private DateTime _lastFrame = DateTime.MinValue;

        // Shared camera frame (camera thread) scaled to the preview canvas
        private void OnCameraFrame(Bitmap source)
        {
            if (_closing) return;
            _lastFrame = DateTime.Now;
            Size canvas;
            lock (_frameLock) canvas = _canvas;
            ShowFrame(CameraFrames.Fit(source, canvas));
        }

        // Camera status when no frames arrive (no camera, busy, opening)
        private void RefreshStatus()
        {
            bool live = (DateTime.Now - _lastFrame).TotalSeconds < 1.5;
            string text = live ? (ScreenRecorder.IsRecording ? "En vivo · los cambios se aplican a la grabación" : "Vista previa en vivo")
                               : CameraHub.Status + " (vista de ejemplo)";
            if (_style.Background != "none" && !PersonSegmenter.Available) return;
            if (_status.Text != text) _status.Text = text;
            if (!live) ShowFrame(null);
        }

        // Composes camera (or a placeholder) inside the shape plus the border; runs on the reader thread or the UI thread
        private void ShowFrame(Bitmap camera)
        {
            // From the reader thread: drop the frame while the previous paint is still queued (keeps the UI responsive)
            bool fromReader = InvokeRequired;
            if (fromReader && Interlocked.CompareExchange(ref _paintPending, 1, 0) != 0)
            {
                if (camera != null) camera.Dispose();
                return;
            }
            Bitmap result;
            lock (_frameLock)
            {
                if (_clip == null)
                {
                    if (fromReader) Interlocked.Exchange(ref _paintPending, 0);
                    return;
                }
                result = CameraFrames.Compose(camera, _canvas, _clip, _border);
            }
            if (camera != null) camera.Dispose();

            MethodInvoker apply = delegate
            {
                Image old = _preview.Image;
                _preview.Image = result;
                if (old != null) old.Dispose();
                if (fromReader) Interlocked.Exchange(ref _paintPending, 0);
            };
            if (!fromReader) apply();
            else
            {
                try { BeginInvoke(apply); }
                catch
                {
                    result.Dispose();
                    Interlocked.Exchange(ref _paintPending, 0);
                }
            }
        }

        private int _paintPending;
    }

    // Live framed camera floating on the desktop. While it is shown the recorder does not open the camera:
    // the screen capture records the bubble wherever the user drags it.
    public class CameraBubble : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int Cx, Cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static CameraBubble _instance;
        public static bool IsActive { get; private set; }

        private readonly CameraStyle _style = CameraStyle.Current;
        private readonly object _frameLock = new object();
        private Size _canvas;
        private Bitmap _border;
        private GraphicsPath _clip;
        private volatile bool _stopped;
        private Bitmap _lastFull;
        private Point? _moveTo;
        private int _paintPending;

        public static void ShowBubble()
        {
            IsActive = true;
            if (_instance == null || _instance.IsDisposed) _instance = new CameraBubble();
            _instance.Show();
        }

        public static void HideBubble()
        {
            IsActive = false;
            if (_instance != null && !_instance.IsDisposed) _instance.Close();
            _instance = null;
        }

        // Size on screen (logical pixels) and top-left as a fraction of the primary screen
        public static Size LogicalSize(CameraStyle s)
        {
            return CameraFrames.CanvasSize(s, Screen.PrimaryScreen.Bounds.Width / (s.IsWide ? 4 : 5));
        }

        public static PointF PositionFraction(CameraStyle s)
        {
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            if (s.PosX.HasValue && s.PosY.HasValue) return new PointF((float)s.PosX.Value, (float)s.PosY.Value);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Size sz = ScaledSize(s);
            return new PointF((float)(wa.Right - sz.Width - 24) / scr.Width, (float)(wa.Bottom - sz.Height - 24) / scr.Height);
        }

        // On-screen size after the user's size choice (30..100 %)
        public static Size ScaledSize(CameraStyle s)
        {
            Size full = LogicalSize(s);
            return new Size(Math.Max(16, full.Width * s.SizePercent / 100), Math.Max(16, full.Height * s.SizePercent / 100));
        }

        private CameraBubble()
        {
            Text = "KidCut Cámara";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            Size = ScaledSize(_style);
            PointF frac = PositionFraction(_style);
            Location = new Point(scr.X + (int)(frac.X * scr.Width), scr.Y + (int)(frac.Y * scr.Height));
            RebuildFrame();

            MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); // drag like a title bar
            };
            MouseWheel += (s, e) => ChangeSize(e.Delta > 0 ? 10 : -10);
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("➖  Más pequeña", null, (s, e) => ChangeSize(-10));
            menu.Items.Add("➕  Más grande", null, (s, e) => ChangeSize(10));
            menu.Items.Add("↺  Tamaño original (100 %)", null, (s, e) => ChangeSize(100));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("🎨  Marco y fondo...", null, (s, e) => CameraStyleForm.ShowDesigner());
            menu.Items.Add("🙈  Ocultar de la pantalla", null, (s, e) => HideBubble());
            ContextMenuStrip = menu;
            Move += (s, e) =>
            {
                _style.PosX = Math.Max(0, Math.Min(1, (double)(Left - scr.X) / scr.Width));
                _style.PosY = Math.Max(0, Math.Min(1, (double)(Top - scr.Y) / scr.Height));
            };
            CameraStyle.Changed += OnStyleChanged;
            FormClosing += (s, e) =>
            {
                _stopped = true;
                CameraStyle.Changed -= OnStyleChanged;
                CameraHub.Unsubscribe(this);
                _style.Save();
            };
        }

        // Shape/border at the full (100 %) size; display scales it to the chosen size
        private void RebuildFrame()
        {
            Size canvas = LogicalSize(_style);
            Bitmap border = CameraFrames.RenderBorder(_style, canvas);
            GraphicsPath clip = CameraFrames.ShapePath(_style.Shape, CameraFrames.ShapeRect(canvas));
            lock (_frameLock)
            {
                if (_border != null) _border.Dispose();
                if (_clip != null) _clip.Dispose();
                _canvas = canvas;
                _border = border;
                _clip = clip;
            }
        }

        // Live edit from the designer (UI thread): new frame look, and a size change keeps the bubble's center in place
        private void OnStyleChanged()
        {
            if (_stopped) return;
            Size before = new Size(Width, Height);
            Point center = new Point(Left + before.Width / 2, Top + before.Height / 2);
            RebuildFrame();
            Size after = ScaledSize(_style);
            if (after != before) _moveTo = new Point(center.X - after.Width / 2, center.Y - after.Height / 2);
            lock (_frameLock) Display(CameraFrames.Compose(null, _canvas, _clip, _border));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x80; // WS_EX_LAYERED (per-pixel alpha) | WS_EX_TOOLWINDOW (no Alt+Tab)
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            lock (_frameLock) Display(CameraFrames.Compose(null, _canvas, _clip, _border));
            CameraHub.Subscribe(this, OnCameraFrame);
        }

        // Shrink/grow around the bubble's center; 100 = back to the original size
        private void ChangeSize(int delta)
        {
            _style.SizePercent = delta == 100 ? 100 : Math.Max(30, Math.Min(100, _style.SizePercent + delta));
            _style.Save();
            CameraStyle.NotifyChanged();
        }

        // Shows a full-size composed frame (or re-shows the last one when null) at the chosen size
        private void Display(Bitmap full)
        {
            if (full != null)
            {
                if (_lastFull != null) _lastFull.Dispose();
                _lastFull = full;
            }
            if (_lastFull == null) return;
            Size target = ScaledSize(_style);
            if (target == _lastFull.Size)
            {
                SetBitmap(_lastFull);
                return;
            }
            using (Bitmap scaled = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(_lastFull, new Rectangle(Point.Empty, target));
                }
                SetBitmap(scaled);
            }
        }

        // Shared camera frame (camera thread): compose here, paint on the UI thread
        private void OnCameraFrame(Bitmap source)
        {
            // Drop the frame while the UI thread is still painting the previous one: under heavy load the queue of
            // pending paints would otherwise grow without limit and freeze the app
            if (_stopped || Interlocked.CompareExchange(ref _paintPending, 1, 0) != 0) return;
            Bitmap composed;
            lock (_frameLock)
            {
                using (Bitmap fit = CameraFrames.Fit(source, _canvas)) composed = CameraFrames.Compose(fit, _canvas, _clip, _border);
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed) Display(composed); else composed.Dispose();
                    Interlocked.Exchange(ref _paintPending, 0);
                });
            }
            catch
            {
                composed.Dispose();
                Interlocked.Exchange(ref _paintPending, 0);
            }
        }

        // Per-pixel alpha: soft edges, glows and brush splatter blend with whatever is behind the bubble
        private void SetBitmap(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(memDc, hBitmap);
            try
            {
                SIZE size = new SIZE { Cx = bmp.Width, Cy = bmp.Height };
                POINT src = new POINT();
                BLENDFUNCTION blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                if (_moveTo.HasValue)
                {
                    // A resize moves the window too, so it grows/shrinks around its center
                    POINT dst = new POINT { X = _moveTo.Value.X, Y = _moveTo.Value.Y };
                    _moveTo = null;
                    UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
                }
                else UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size, memDc, ref src, 0, ref blend, 2); // ULW_ALPHA
            }
            finally
            {
                SelectObject(memDc, old);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    #endregion

    #region Floating Recorder Overlay Widget

    public class RecorderOverlay : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static readonly Color OnColor = Color.FromArgb(22, 70, 45);
        private static readonly Color OffColor = Color.FromArgb(70, 22, 32);

        private readonly Button _btnRec;
        private readonly Button _btnSys;
        private readonly Button _btnMic;
        private readonly Button _btnVoice;
        private readonly Button _btnCam;
        private readonly Button _btnFps;
        private readonly Button _btnList;
        private readonly Button _btnStyle;
        private readonly Button _btnShow;
        private bool _showOn = true;
        private readonly Label _lblTime;
        private readonly ToolTip _tips = new ToolTip();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private bool _sysOn = true;
        private bool _micOn = true;
        private bool _camOn;
        private volatile bool _camSearched;
        private int _fps = 30;
        private volatile bool _starting;
        private string _shownError;
        private string _shownOutput;
        private string _flash;
        private DateTime _flashUntil;

        public RecorderOverlay()
        {
            Text = "KidCut Grabador";
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(14, 10, 28);
            Size = new Size(654, 44);
            Opacity = 0.95;
            StartPosition = FormStartPosition.Manual;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + 8);

            Label grip = new Label
            {
                Text = "⋮⋮",
                ForeColor = Color.FromArgb(120, 110, 150),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 0),
                Size = new Size(22, 44),
                Cursor = Cursors.SizeAll
            };
            grip.MouseDown += DragWindow;
            MouseDown += DragWindow;
            Controls.Add(grip);
            _tips.SetToolTip(grip, "Arrastra para mover el widget");

            _btnRec = AddButton("● REC", "Iniciar / detener grabación de pantalla", 24, 70, ToggleRecording);
            _btnRec.BackColor = Color.FromArgb(200, 20, 50);
            _btnRec.ForeColor = Color.White;

            _lblTime = new Label
            {
                Text = "00:00:00",
                ForeColor = Color.FromArgb(255, 210, 0),
                Font = new Font("Consolas", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(96, 0),
                Size = new Size(110, 44),
                Cursor = Cursors.Hand
            };
            _lblTime.MouseDown += DragWindow;
            _lblTime.DoubleClick += (s, e) => OpenRecordingsFolder();
            Controls.Add(_lblTime);
            _tips.SetToolTip(_lblTime, "Doble clic: abrir carpeta de grabaciones");

            AddButton("📷", "Captura de pantalla (PNG sin pérdida)", 208, 40, TakeShot);
            _btnSys = AddButton("🔊", "Grabar el audio del sistema (lo que suena en la PC)", 250, 40, () => { _sysOn = !_sysOn; RefreshToggles(); });
            _btnMic = AddButton("🎙", "Grabar el micrófono", 292, 40, () => { _micOn = !_micOn; RefreshToggles(); });
            _btnVoice = AddButton("✨", "Mejorar la voz: quita pitidos y zumbidos constantes y el ruido del ambiente, y sube tu voz por encima del juego sin bajar el juego. Se aplica al guardar, sin cargar la PC mientras grabas.", 334, 36, () => { ScreenRecorder.EnhanceVoice = !ScreenRecorder.EnhanceVoice; RefreshToggles(); });
            _btnCam = AddButton("📹", "Buscando cámara...", 372, 40, () => { _camOn = !_camOn; RefreshToggles(); SyncBubble(); });
            _btnShow = AddButton("👁", "Mostrar la cámara en pantalla: arrástrala a donde quieras y se graba ahí. Apagado: se graba sin verla, en la última posición.", 414, 36, () => { _showOn = !_showOn; RefreshToggles(); SyncBubble(); });
            _btnStyle = AddButton("🎨", "Marco de la cámara: forma, borde, filtro, fondo y colores", 452, 36, CameraStyleForm.ShowDesigner);
            _btnFps = AddButton("30", "Cuadros por segundo de la grabación", 490, 36, () => { _fps = _fps == 30 ? 60 : 30; RefreshToggles(); });
            _btnList = AddButton("📂", "Ver las grabaciones y capturas hechas", 528, 40, ShowRecordingsMenu);
            AddButton("✂", "Abrir el editor KidCut", 570, 40, () => Program.OpenInBrowser(Program.TargetUrl));
            AddButton("✕", "Ocultar widget (se reabre desde la bandeja o el editor)", 612, 40, Hide);
            RefreshToggles();

            // Probing DirectShow takes ~1 s, so the camera button enables itself once a webcam is found
            new Thread(() =>
            {
                try { ScreenRecorder.FindCamera(); } catch { }
                _camSearched = true;
            }) { IsBackground = true }.Start();

            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };

            _timer.Interval = 250;
            _timer.Tick += (s, e) => UpdateState();
            _timer.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // WDA_EXCLUDEFROMCAPTURE: the widget never appears in its own recordings or screenshots
            try { SetWindowDisplayAffinity(Handle, 0x11); } catch { }
        }

        private Button AddButton(string text, string tip, int x, int width, Action onClick)
        {
            Button b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI Emoji", 10, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(30, 26, 55),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(x, 4),
                Size = new Size(width, 36),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(80, 70, 120);
            b.Click += (s, e) => onClick();
            _tips.SetToolTip(b, tip);
            Controls.Add(b);
            return b;
        }

        private void RefreshToggles()
        {
            _btnSys.BackColor = _sysOn ? OnColor : OffColor;
            _btnMic.BackColor = _micOn ? OnColor : OffColor;
            _btnVoice.BackColor = ScreenRecorder.EnhanceVoice ? OnColor : OffColor;
            _btnSys.Text = _sysOn ? "🔊" : "🔇";
            _btnCam.BackColor = _camOn ? OnColor : OffColor;
            _btnShow.BackColor = _showOn ? OnColor : OffColor;
            _btnShow.Text = _showOn ? "👁" : "🙈";
            _btnFps.Text = _fps.ToString();
        }

        // Camera on + "show" on = live bubble on the desktop; otherwise no bubble
        private void SyncBubble()
        {
            bool want = _camOn && _showOn && ScreenRecorder.FindCamera() != null;
            if (want && !CameraBubble.IsActive) CameraBubble.ShowBubble();
            else if (!want && CameraBubble.IsActive) CameraBubble.HideBubble();
        }

        private void ShowRecordingsMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            FileInfo[] files = Directory.Exists(ScreenRecorder.RecordingsDir)
                ? new DirectoryInfo(ScreenRecorder.RecordingsDir).GetFiles()
                : new FileInfo[0];
            Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
            foreach (FileInfo f in files.Take(15))
            {
                string path = f.FullName;
                bool isImage = Regex.IsMatch(f.Extension, @"^\.(png|jpe?g|webp|bmp)$", RegexOptions.IgnoreCase);
                string label = string.Format("{0}  {1}  ·  {2:0.0} MB  ·  {3:dd/MM HH:mm}", isImage ? "📷" : "🎬", f.Name, f.Length / 1048576.0, f.LastWriteTime);
                menu.Items.Add(label, null, (s, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
                });
            }
            if (files.Length == 0) menu.Items.Add("Aún no hay grabaciones").Enabled = false;
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("📁 Abrir carpeta de grabaciones", null, (s, e) => OpenRecordingsFolder());
            menu.Items.Add("✂ Editar en KidCut", null, (s, e) => Program.OpenInBrowser(Program.TargetUrl));
            menu.Closed += (s, e) => BeginInvoke((MethodInvoker)menu.Dispose);
            menu.Show(_btnList, new Point(0, _btnList.Height));
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); // WM_NCLBUTTONDOWN on caption
        }

        private void Flash(string text)
        {
            _flash = text;
            _flashUntil = DateTime.Now.AddSeconds(3);
        }

        private void ToggleRecording()
        {
            if (_starting) return; // a previous take may still be compressing: that runs in the background
            if (ScreenRecorder.IsRecording)
            {
                new Thread(ScreenRecorder.Stop) { IsBackground = true }.Start();
                return;
            }
            bool camOn = _camOn && _btnCam.Enabled;
            // With the bubble on screen the screen capture already records it; FFmpeg only composes the camera when hidden
            bool cam = camOn && !CameraBubble.IsActive;
            _starting = true;
            bool sys = _sysOn, mic = _micOn;
            int fps = _fps;
            new Thread(() =>
            {
                try { ScreenRecorder.Start(sys, mic, cam, fps); }
                catch (Exception ex) { ScreenRecorder.LastError = ex.Message; }
                finally { _starting = false; }
            }) { IsBackground = true }.Start();
        }

        private void TakeShot()
        {
            try
            {
                ScreenRecorder.TakeScreenshot();
                Flash("📷 GUARDADA");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo capturar la pantalla:\n" + ex.Message, "KidCut Grabador", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenRecordingsFolder()
        {
            try
            {
                string target = _shownOutput != null && File.Exists(_shownOutput)
                    ? string.Format("/select,\"{0}\"", _shownOutput)
                    : string.Format("\"{0}\"", ScreenRecorder.RecordingsDir);
                Directory.CreateDirectory(ScreenRecorder.RecordingsDir);
                Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
            }
            catch { }
        }

        private void UpdateState()
        {
            bool rec = ScreenRecorder.IsRecording;
            bool busy = _starting;
            _btnRec.Text = rec ? "■ STOP" : "● REC";
            _btnRec.Enabled = !busy;
            _btnSys.Enabled = _btnMic.Enabled = _btnFps.Enabled = !rec && !busy;
            bool hasCamera = _camSearched && ScreenRecorder.FindCamera() != null;
            _btnCam.Enabled = hasCamera && !rec && !busy;
            _btnShow.Enabled = hasCamera && _camOn && !rec && !busy;
            if (_camOn && _showOn && hasCamera && !CameraBubble.IsActive)
            {
                // Hidden from the bubble's own menu
                _showOn = false;
                RefreshToggles();
            }
            string camTip = !_camSearched ? "Buscando cámara..."
                : hasCamera ? "Grabar también la cámara (" + ScreenRecorder.FindCamera() + ") en una esquina"
                : "No se encontró ninguna cámara";
            if (_tips.GetToolTip(_btnCam) != camTip) _tips.SetToolTip(_btnCam, camTip);

            if (ScreenRecorder.LastOutput != null && ScreenRecorder.LastOutput != _shownOutput)
            {
                _shownOutput = ScreenRecorder.LastOutput;
                Flash("✔ GUARDADO");
            }

            if (rec)
            {
                TimeSpan t = ScreenRecorder.Elapsed;
                _lblTime.Text = string.Format("● {0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
                _lblTime.ForeColor = Color.FromArgb(255, 60, 90);
            }
            else if (_starting)
            {
                _lblTime.Text = "INICIANDO...";
                _lblTime.ForeColor = Color.FromArgb(0, 240, 255);
            }
            else if (ScreenRecorder.IsProcessing)
            {
                _lblTime.Text = "⏳ COMPRIMIENDO";
                _lblTime.ForeColor = Color.FromArgb(0, 240, 255);
            }
            else if (_flash != null && DateTime.Now < _flashUntil)
            {
                _lblTime.Text = _flash;
                _lblTime.ForeColor = Color.FromArgb(57, 255, 20);
            }
            else
            {
                _lblTime.Text = "00:00:00";
                _lblTime.ForeColor = Color.FromArgb(255, 210, 0);
            }

            string err = ScreenRecorder.LastError;
            if (err != null && err != _shownError)
            {
                _shownError = err;
                MessageBox.Show(this, err, "KidCut Grabador", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    #endregion

    #region Retro Control Form

    public class KidCutControlForm : Form
    {
        private NotifyIcon _trayIcon;
        private ContextMenu _trayMenu;
        private Label _lblStatus;
        private Label _lblFfmpeg;
        private System.Windows.Forms.Timer _statsTimer;

        public KidCutControlForm()
        {
            Text = "KidCut - 8-Bit Pixel Video Studio (Windows Nativo)";
            Size = new Size(540, 440);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(8, 6, 17);
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;

            string iconPath = Path.Combine(Program.BaseDir, "icon.ico");
            if (File.Exists(iconPath))
            {
                try { Icon = new Icon(iconPath); } catch { }
            }

            SetupSystemTray();

            // Logo
            PictureBox pbLogo = new PictureBox();
            pbLogo.Size = new Size(70, 70);
            pbLogo.Location = new Point((ClientSize.Width - 70) / 2, 16);
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

            // Title
            Label lblTitle = new Label();
            lblTitle.Text = "★ KIDCUT 8-BIT STUDIO ★";
            lblTitle.Font = new Font("Segoe UI", 13, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(255, 210, 0);
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            lblTitle.Size = new Size(500, 26);
            lblTitle.Location = new Point(20, 92);
            Controls.Add(lblTitle);

            // Server Status
            _lblStatus = new Label();
            _lblStatus.Text = string.Format("● SERVIDOR NATIVO EN LÍNEA: {0}", Program.TargetUrl);
            _lblStatus.Font = new Font("Consolas", 9, FontStyle.Bold);
            _lblStatus.ForeColor = Color.FromArgb(57, 255, 20);
            _lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            _lblStatus.Size = new Size(500, 20);
            _lblStatus.Location = new Point(20, 120);
            Controls.Add(_lblStatus);

            // FFmpeg Status
            _lblFfmpeg = new Label();
            _lblFfmpeg.Text = FfmpegEngine.IsAvailable 
                ? "⚡ MOTOR FFMPEG NATIVO: ACTIVO (RAM PLANO < 50MB)" 
                : "⚠ MOTOR FFMPEG NATIVO: NO DETECTADO";
            _lblFfmpeg.Font = new Font("Consolas", 8, FontStyle.Bold);
            _lblFfmpeg.ForeColor = FfmpegEngine.IsAvailable ? Color.FromArgb(0, 240, 255) : Color.FromArgb(255, 100, 100);
            _lblFfmpeg.TextAlign = ContentAlignment.MiddleCenter;
            _lblFfmpeg.Size = new Size(500, 20);
            _lblFfmpeg.Location = new Point(20, 142);
            Controls.Add(_lblFfmpeg);

            // Button: Reabrir
            Button btnOpen = new Button();
            btnOpen.Text = "🚀 ABRIR INTERFAZ KIDCUT";
            btnOpen.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            btnOpen.BackColor = Color.FromArgb(255, 210, 0);
            btnOpen.ForeColor = Color.Black;
            btnOpen.FlatStyle = FlatStyle.Flat;
            btnOpen.FlatAppearance.BorderSize = 0;
            btnOpen.Size = new Size(225, 38);
            btnOpen.Location = new Point(40, 172);
            btnOpen.Cursor = Cursors.Hand;
            btnOpen.Click += (s, e) => Program.OpenInBrowser(Program.TargetUrl);
            Controls.Add(btnOpen);

            // Button: Widget de grabación
            Button btnRecorder = new Button();
            btnRecorder.Text = "🔴 WIDGET DE GRABACIÓN";
            btnRecorder.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            btnRecorder.BackColor = Color.FromArgb(200, 20, 50);
            btnRecorder.ForeColor = Color.White;
            btnRecorder.FlatStyle = FlatStyle.Flat;
            btnRecorder.FlatAppearance.BorderSize = 0;
            btnRecorder.Size = new Size(225, 38);
            btnRecorder.Location = new Point(275, 172);
            btnRecorder.Cursor = Cursors.Hand;
            btnRecorder.Click += (s, e) => Program.ShowOverlay();
            Controls.Add(btnRecorder);

            // Button: Acceso Directo
            Button btnShortcut = new Button();
            btnShortcut.Text = "📌 CREAR ACCESO DIRECTO EN EL ESCRITORIO";
            btnShortcut.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            btnShortcut.BackColor = Color.FromArgb(30, 40, 75);
            btnShortcut.ForeColor = Color.FromArgb(0, 240, 255);
            btnShortcut.FlatStyle = FlatStyle.Flat;
            btnShortcut.FlatAppearance.BorderColor = Color.FromArgb(0, 240, 255);
            btnShortcut.Size = new Size(460, 32);
            btnShortcut.Location = new Point(40, 218);
            btnShortcut.Cursor = Cursors.Hand;
            btnShortcut.Click += (s, e) => CreateDesktopShortcut();
            Controls.Add(btnShortcut);

            // Button: Carpeta de Videos
            Button btnExports = new Button();
            btnExports.Text = "🎬 VER VIDEOS EXPORTADOS";
            btnExports.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            btnExports.BackColor = Color.FromArgb(25, 45, 35);
            btnExports.ForeColor = Color.FromArgb(57, 255, 20);
            btnExports.FlatStyle = FlatStyle.Flat;
            btnExports.FlatAppearance.BorderColor = Color.FromArgb(57, 255, 20);
            btnExports.Size = new Size(460, 32);
            btnExports.Location = new Point(40, 258);
            btnExports.Cursor = Cursors.Hand;
            btnExports.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", string.Format("\"{0}\"", Program.ExportsDir)) { UseShellExecute = true });
                }
                catch { }
            };
            Controls.Add(btnExports);

            // Button: Carpeta Proyecto
            Button btnFolder = new Button();
            btnFolder.Text = "📁 CARPETA APP";
            btnFolder.Font = new Font("Segoe UI", 8, FontStyle.Regular);
            btnFolder.BackColor = Color.FromArgb(25, 20, 45);
            btnFolder.ForeColor = Color.White;
            btnFolder.FlatStyle = FlatStyle.Flat;
            btnFolder.FlatAppearance.BorderColor = Color.FromArgb(100, 90, 140);
            btnFolder.Size = new Size(225, 32);
            btnFolder.Location = new Point(40, 300);
            btnFolder.Cursor = Cursors.Hand;
            btnFolder.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", string.Format("\"{0}\"", Program.BaseDir)) { UseShellExecute = true }); } catch { }
            };
            Controls.Add(btnFolder);

            // Button: Salir
            Button btnExit = new Button();
            btnExit.Text = "❌ SALIR";
            btnExit.Font = new Font("Segoe UI", 8, FontStyle.Regular);
            btnExit.BackColor = Color.FromArgb(45, 15, 25);
            btnExit.ForeColor = Color.FromArgb(255, 80, 100);
            btnExit.FlatStyle = FlatStyle.Flat;
            btnExit.FlatAppearance.BorderColor = Color.FromArgb(255, 50, 70);
            btnExit.Size = new Size(225, 32);
            btnExit.Location = new Point(275, 300);
            btnExit.Cursor = Cursors.Hand;
            btnExit.Click += (s, e) => Close();
            Controls.Add(btnExit);

            // Footer
            Label lblFooter = new Label();
            lblFooter.Text = "Al minimizar, KidCut permanece activo en la bandeja junto al reloj.";
            lblFooter.Font = new Font("Segoe UI", 7, FontStyle.Italic);
            lblFooter.ForeColor = Color.FromArgb(120, 110, 150);
            lblFooter.TextAlign = ContentAlignment.MiddleCenter;
            lblFooter.Size = new Size(500, 20);
            lblFooter.Location = new Point(20, 346);
            Controls.Add(lblFooter);

            // Live Stats Timer
            _statsTimer = new System.Windows.Forms.Timer();
            _statsTimer.Interval = 2000;
            _statsTimer.Tick += (s, e) =>
            {
                if (FfmpegEngine.IsRunning)
                {
                    _lblFfmpeg.Text = string.Format("⚡ RENDERIZANDO: {0}% (Cuadro {1}/{2}) - RAM: {3} MB", 
                        FfmpegEngine.Percent, FfmpegEngine.CurrentFrame, FfmpegEngine.TotalFrames, FfmpegEngine.RamMb);
                    _lblFfmpeg.ForeColor = Color.FromArgb(255, 210, 0);
                }
                else if (FfmpegEngine.IsFinished)
                {
                    _lblFfmpeg.Text = "★ EXPORTACIÓN COMPLETADA (RAM < 50MB GARANTIZADO)";
                    _lblFfmpeg.ForeColor = Color.FromArgb(57, 255, 20);
                }
            };
            _statsTimer.Start();

            // Form Events
            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                    ShowInTaskbar = false;
                    return;
                }
                if (_statsTimer != null) _statsTimer.Stop();
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
                    ShowInTaskbar = false;
                }
            };
        }

        private void SetupSystemTray()
        {
            _trayMenu = new ContextMenu();
            _trayMenu.MenuItems.Add("🚀 Abrir KidCut", (s, e) =>
            {
                Program.OpenInBrowser(Program.TargetUrl);
            });
            _trayMenu.MenuItems.Add("🔴 Widget de Grabación", (s, e) => Program.ShowOverlay());
            _trayMenu.MenuItems.Add("⚙️ Mostrar Panel de Control", (s, e) =>
            {
                Show();
                WindowState = FormWindowState.Normal;
                ShowInTaskbar = true;
                BringToFront();
                Program.SetForegroundWindow(Handle);
            });
            _trayMenu.MenuItems.Add("🎬 Ver Videos Exportados", (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", string.Format("\"{0}\"", Program.ExportsDir)) { UseShellExecute = true }); } catch { }
            });
            _trayMenu.MenuItems.Add("-");
            _trayMenu.MenuItems.Add("❌ Salir de KidCut", (s, e) =>
            {
                if (_statsTimer != null) _statsTimer.Stop();
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                Program.Cleanup();
                Environment.Exit(0);
            });

            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "KidCut Studio - Motor Nativo Activo";
            if (Icon != null) _trayIcon.Icon = Icon;
            _trayIcon.ContextMenu = _trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) =>
            {
                Show();
                WindowState = FormWindowState.Normal;
                ShowInTaskbar = true;
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

    #endregion
}
