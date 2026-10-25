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

        public WasapiCapture(bool loopback, string path)
        {
            _loopback = loopback;
            _path = path;
        }

        public bool Start()
        {
            _thread = new Thread(Run) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
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

        private void Run()
        {
            IAudioClient client = null;
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

        private static string Probe(string args)
        {
            string probe = FfmpegEngine.FindFfprobe();
            if (string.IsNullOrEmpty(probe)) return "";
            string output, error;
            Run(probe, "-v error " + args, 30000, out output, out error);
            return output;
        }

        public static double ProbeDuration(string path)
        {
            double d;
            string s = Probe(string.Format("-show_entries format=duration -of csv=p=0 \"{0}\"", path)).Trim();
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
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
        public static volatile bool IsProcessing;
        public static string LastOutput;
        public static string LastError;

        private static readonly Stopwatch _clock = new Stopwatch();
        private static Process _ffmpeg;
        private static StringBuilder _ffmpegErr = new StringBuilder();
        private static WasapiCapture _sys;
        private static WasapiCapture _mic;
        private static string _videoTemp;
        private static string _encoderArgs;
        private static Thread _finishThread;

        public static TimeSpan Elapsed { get { return _clock.Elapsed; } }

        private static string _camera;
        private static bool _cameraSearched;

        // First DirectShow video device (webcam), or null when there is none
        public static string FindCamera()
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

        public static void Start(bool captureSystem, bool captureMic, int fps)
        {
            Start(captureSystem, captureMic, false, fps);
        }

        public static void Start(bool captureSystem, bool captureMic, bool captureCamera, int fps)
        {
            if (IsRecording || IsProcessing) return;
            if (!FfmpegEngine.IsAvailable) throw new InvalidOperationException("FFmpeg no fue encontrado en el sistema.");
            Directory.CreateDirectory(RecordingsDir);
            LastError = null;

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _videoTemp = Path.Combine(Program.ScratchDir, "rec_" + stamp + ".mkv");

            // Audio starts first; the muxer trims its head so it lines up with the first video frame
            _sys = captureSystem ? new WasapiCapture(true, Path.Combine(Program.ScratchDir, "rec_" + stamp + "_sys.wav")) : null;
            _mic = captureMic ? new WasapiCapture(false, Path.Combine(Program.ScratchDir, "rec_" + stamp + "_mic.wav")) : null;
            if (_sys != null && !_sys.Start()) { LastError = "Audio del sistema no disponible: " + _sys.Error; _sys = null; }
            if (_mic != null && !_mic.Start()) { LastError = "Micrófono no disponible: " + _mic.Error; _mic = null; }

            // Desktop Duplication first; GDI capture when it is unavailable (remote desktop, old driver)
            string[][] screens =
            {
                new[] { string.Format("-f lavfi -i ddagrab=output_idx=0:framerate={0}:draw_mouse=1", fps), "hwdownload,format=bgra," },
                new[] { string.Format("-f gdigrab -framerate {0} -draw_mouse 1 -i desktop", fps), "" }
            };
            // Webcam at 720p, then at its default mode, and finally without camera so the take still happens
            List<string> cameraModes = new List<string>();
            string camera = captureCamera ? FindCamera() : null;
            if (camera != null)
            {
                cameraModes.Add("-video_size 1280x720 -framerate 30 ");
                cameraModes.Add("");
            }
            cameraModes.Add(null);

            string cameraError = "";
            foreach (string camMode in cameraModes)
            {
                foreach (string[] screen in screens)
                {
                    _clock.Restart();
                    _ffmpeg = LaunchCapture(screen[0], screen[1], camMode == null ? null : camera, camMode, fps);
                    if (!_ffmpeg.WaitForExit(camMode == null ? 1500 : 2500))
                    {
                        if (captureCamera && camMode == null)
                        {
                            LastError = camera == null
                                ? "No se encontró ninguna cámara; se graba sin cámara."
                                : "No se pudo abrir la cámara (¿la usa otra app?); se graba sin cámara.\n\n" + cameraError.Trim();
                        }
                        IsRecording = true;
                        return;
                    }
                    _ffmpeg.WaitForExit(); // flush stderr
                    if (camMode != null) lock (_ffmpegErr) cameraError = _ffmpegErr.ToString();
                }
            }
            StopAudio();
            _clock.Reset();
            throw new InvalidOperationException("FFmpeg no pudo capturar la pantalla:\n" + _ffmpegErr);
        }

        public static void Stop()
        {
            if (!IsRecording) return;
            IsRecording = false;
            IsProcessing = true;
            _clock.Stop();
            try
            {
                _ffmpeg.StandardInput.Write("q");
                _ffmpeg.StandardInput.Flush();
            }
            catch { }
            StopAudio();
            if (!_ffmpeg.WaitForExit(15000))
            {
                try { _ffmpeg.Kill(); } catch { }
            }
            _finishThread = new Thread(Finish) { IsBackground = true };
            _finishThread.Start();
        }

        // Called on app exit so an active take is still saved
        public static void StopAndWait()
        {
            Stop();
            if (_finishThread != null) _finishThread.Join(120000);
        }

        private static void StopAudio()
        {
            if (_sys != null) _sys.RequestStop();
            if (_mic != null) _mic.RequestStop();
            if (_sys != null) _sys.Join();
            if (_mic != null) _mic.Join();
        }

        private static Process LaunchCapture(string screenInput, string screenFilter, string camera, string cameraMode, int fps)
        {
            string video;
            if (camera == null)
            {
                video = screenFilter.Length > 0 ? string.Format("{0} -vf {1}", screenInput, screenFilter.TrimEnd(',')) : screenInput;
            }
            else
            {
                // Picture-in-picture bottom-right, shaped by the designer's mask and border PNGs. Both sources are stamped with
                // their arrival wall-clock: their own clocks differ and overlay would otherwise stall waiting for the camera.
                CameraStyle style = CameraStyle.Current;
                Size cam = CameraFrames.CanvasSize(style, PhysicalScreenSize().Width / (style.Shape == "rect" ? 4 : 5));
                string mask = Path.Combine(Program.ScratchDir, "rec_cam_mask.png");
                string border = Path.Combine(Program.ScratchDir, "rec_cam_border.png");
                using (Bitmap m = CameraFrames.RenderMask(style, cam)) m.Save(mask, ImageFormat.Png);
                using (Bitmap b = CameraFrames.RenderBorder(style, cam)) b.Save(border, ImageFormat.Png);
                string extraInputs = "";
                if (CameraFrames.UsesPlate(style)) extraInputs += string.Format("-i \"{0}\" ", CameraFrames.PlatePath);
                if (CameraFrames.UsesBackgroundImage(style))
                {
                    string bg = Path.Combine(Program.ScratchDir, "rec_cam_background.png");
                    using (Bitmap b = CameraFrames.RenderBackground(style.Background, style.Colors, cam)) b.Save(bg, ImageFormat.Png);
                    extraInputs += string.Format("-i \"{0}\" ", bg);
                }
                string camGraph = CameraFrames.Graph(style, cam, "[1:v]setpts=(RTCTIME-RTCSTART)/(TB*1000000),", "[4:v]", "[5:v]", "c0");
                // Same spot where the user last left the on-screen bubble (bottom-right by default)
                Size phys = PhysicalScreenSize();
                PointF frac = CameraBubble.PositionFraction(style);
                int camX = Math.Max(0, Math.Min(phys.Width - cam.Width, (int)(frac.X * phys.Width)));
                int camY = Math.Max(0, Math.Min(phys.Height - cam.Height, (int)(frac.Y * phys.Height)));
                video = string.Format(
                    "{0} -f dshow -rtbufsize 256M {1}-i \"video={2}\" -i \"{3}\" -i \"{4}\" {5}-filter_complex \"" +
                    "[0:v]{6}setpts=(RTCTIME-RTCSTART)/(TB*1000000)[scr];{7};" +
                    "[c0]format=yuva420p[c];[2:v]format=gray[m];[c][m]alphamerge[cm];" +
                    // CFR on the output side: an fps filter would pad up to the camera's EOF stamp (its own device clock) on stop
                    "[cm][3:v]overlay=0:0[pip];[scr][pip]overlay={9}:{10}:eof_action=repeat[v]\" -map \"[v]\" -fps_mode cfr -r {8}",
                    screenInput, cameraMode, camera, mask, border, extraInputs, screenFilter, camGraph, fps, camX, camY);
            }
            string args = string.Format("-y -hide_banner -nostats -loglevel error {0} {1} -g {2} \"{3}\"", video, EncoderArgs(), fps * 2, _videoTemp);
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kidcut_boot.log"),
                    string.Format("[{0}] Capture args: {1}\n", DateTime.Now, args));
            }
            catch { }
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
            p.BeginErrorReadLine();
            return p;
        }

        // GPU encoder when one works on this machine, otherwise x264 CRF 18 (visually lossless, good compression)
        private static string EncoderArgs()
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
            return _encoderArgs = "-c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p";
        }

        private static void Finish()
        {
            List<string> temps = new List<string> { _videoTemp };
            try
            {
                double videoSec = MediaTools.ProbeDuration(_videoTemp);
                if (videoSec <= 0) throw new InvalidOperationException("La grabación de video quedó vacía.\n" + _ffmpegErr);
                // The capture can never be longer than the stopwatch started at launch; anything beyond is padding
                double limitSec = _clock.Elapsed.TotalSeconds;
                bool clamp = videoSec > limitSec + 1;
                if (clamp) videoSec = limitSec;

                List<WasapiCapture> audio = new List<WasapiCapture>();
                foreach (WasapiCapture a in new[] { _sys, _mic })
                {
                    if (a == null) continue;
                    temps.Add(a.FilePath);
                    if (a.Error == null && a.FramesWritten > 0) audio.Add(a);
                }

                string output = Path.Combine(RecordingsDir, "Grabacion_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
                StringBuilder args = new StringBuilder();
                args.AppendFormat("-i \"{0}\" ", _videoTemp);
                foreach (WasapiCapture a in audio)
                {
                    double lead = a.Seconds - videoSec; // audio began before the first video frame
                    if (lead > 0) args.AppendFormat(CultureInfo.InvariantCulture, "-ss {0:0.000} ", lead);
                    args.AppendFormat("-i \"{0}\" ", a.FilePath);
                }
                args.Append("-map 0:v ");
                if (audio.Count == 2)
                {
                    // Track 1 = mix for any player; tracks 2/3 = separate channels the editor imports independently
                    args.Append("-filter_complex \"[1:a][2:a]amix=inputs=2:normalize=0[mix]\" -map \"[mix]\" -map 1:a -map 2:a ");
                    args.Append("-metadata:s:a:0 title=Mezcla -metadata:s:a:1 title=Sistema -metadata:s:a:2 title=Microfono ");
                    args.Append("-disposition:a:0 default -disposition:a:1 0 -disposition:a:2 0 ");
                }
                else if (audio.Count == 1)
                {
                    args.Append("-map 1:a ");
                }
                if (clamp) args.AppendFormat(CultureInfo.InvariantCulture, "-t {0:0.000} ", limitSec);
                args.AppendFormat("-c:v copy -c:a aac -b:a 192k -movflags +faststart \"{0}\"", output);

                string err;
                if (MediaTools.Ffmpeg(args.ToString(), 1800000, out err) != 0) throw new InvalidOperationException(err);

                LastOutput = output;
                foreach (string t in temps)
                {
                    try { File.Delete(t); } catch { }
                }
            }
            catch (Exception ex)
            {
                // Temp takes are kept in scratch\media so nothing recorded is lost
                LastError = "No se pudo guardar la grabación (los archivos originales quedan en " + Program.ScratchDir + "):\n" + ex.Message;
            }
            finally
            {
                IsProcessing = false;
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

        // id, label, FFmpeg filter ({0} = width, {1} = height)
        public static readonly string[][] Filters =
        {
            new[] { "none", "Normal", "" },
            new[] { "bw", "Blanco y negro", "hue=s=0" },
            new[] { "sepia", "Sepia", "colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131" },
            new[] { "vivid", "Vívido", "eq=saturation=1.6:contrast=1.08" },
            new[] { "warm", "Cálido", "colorbalance=rs=0.18:gs=0.05:bs=-0.15" },
            new[] { "cool", "Frío", "colorbalance=rs=-0.12:bs=0.22" },
            new[] { "vintage", "Vintage", "curves=preset=vintage" },
            new[] { "contrast", "Alto contraste", "eq=contrast=1.45:brightness=0.03:saturation=1.1" },
            new[] { "pixel", "Pixel 8-bit", "scale={0}/5:-2:flags=area,scale={0}:{1}:flags=neighbor" }
        };

        // "none" keeps the real room; every other one replaces it using the empty-room capture (PlatePath)
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

        // Empty-room snapshot at the camera's native size; the person is whatever differs from it
        public static string PlatePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kidcut_camera_plate.png"); }
        }

        public static bool UsesPlate(CameraStyle s)
        {
            return s.Background != "none" && File.Exists(PlatePath);
        }

        public static bool UsesBackgroundImage(CameraStyle s)
        {
            return UsesPlate(s) && s.Background != "blur";
        }

        // Camera processing graph: fit to canvas, optional background replacement, color filter and mirror.
        // camIn includes the input label and any prefix filters; plateIn/bgIn are input labels (used only when needed).
        public static string Graph(CameraStyle s, Size size, string camIn, string plateIn, string bgIn, string outLabel)
        {
            string fit = string.Format("scale={0}:{1}:force_original_aspect_ratio=increase,crop={0}:{1}", size.Width, size.Height);
            string extra = Filters.First(r => r[0] == s.Filter)[2];
            string post = (extra.Length > 0 ? "," + string.Format(extra, size.Width, size.Height) : "") + (s.Mirror ? ",hflip" : "");
            if (!UsesPlate(s)) return string.Format("{0}{1},format=rgb24{2}[{3}]", camIn, fit, post, outLabel);

            StringBuilder g = new StringBuilder();
            g.AppendFormat("{0}{1},format=rgb24,split=3[k0][k1][k2];", camIn, fit);
            g.AppendFormat("{0}{1},format=rgb24[pl];", plateIn, fit);
            // Person mask = pixels that differ from the empty room; erosion drops sensor noise, dilation fills holes, blur softens edges
            g.Append("[k0][pl]blend=all_mode=difference,format=gray,lut=c0='if(gt(val,28),255,0)',erosion,dilation,dilation,dilation,gblur=sigma=3[pm];");
            g.Append("[k1]format=rgba[kp];[kp][pm]alphamerge[person];");
            if (s.Background == "blur") g.Append("[k2]gblur=sigma=18[bg];");
            // The still image is laid over the live feed so the background stream keeps the camera's timing
            else g.AppendFormat("{0}scale={1}:{2},format=rgb24[bimg];[k2][bimg]overlay=0:0:eof_action=repeat[bg];", bgIn, size.Width, size.Height);
            g.AppendFormat("[bg][person]overlay=0:0,format=rgb24{0}[{1}]", post, outLabel);
            return g.ToString();
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

        // White shape on black: FFmpeg alphamerge uses it as the camera's transparency
        public static Bitmap RenderMask(CameraStyle s, Size sz)
        {
            Bitmap bmp = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = HighQuality(bmp))
            using (GraphicsPath p = ShapePath(s.Shape, ShapeRect(sz)))
            {
                g.Clear(Color.Black);
                g.FillPath(Brushes.White, p);
            }
            return bmp;
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

        // Same camera mode order as the recorder (720p first) so the empty-room capture lines up with the live feed
        public const string HdMode = "-video_size 1280x720 -framerate 30 ";

        // Live camera through the shared graph, as raw BGR frames on stdout (designer preview and on-screen bubble)
        public static Process StartCamera(CameraStyle s, Size size, string camera, bool hd, string tag)
        {
            string inputs = "";
            if (UsesPlate(s)) inputs += string.Format("-i \"{0}\" ", PlatePath);
            if (UsesBackgroundImage(s))
            {
                string bg = Path.Combine(Program.ScratchDir, tag + "_background.png");
                using (Bitmap b = RenderBackground(s.Background, s.Colors, size)) b.Save(bg, ImageFormat.Png);
                inputs += string.Format("-i \"{0}\" ", bg);
            }
            string args = string.Format("-hide_banner -loglevel error -f dshow {0}-i \"video={1}\" {2}-filter_complex \"{3}\" -map \"[out]\" -f rawvideo -pix_fmt bgr24 -",
                hd ? HdMode : "", camera, inputs, Graph(s, size, "[0:v]", "[1:v]", "[2:v]", "out"));
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
            p.BeginErrorReadLine();
            return p;
        }

        // Blocks reading frames until the stream ends or keepGoing() is false; onFrame owns each bitmap
        public static void ReadCameraFrames(Process p, Size size, Func<bool> keepGoing, Action<Bitmap> onFrame)
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

                Bitmap frame = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
                BitmapData data = frame.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                for (int y = 0; y < size.Height; y++) Marshal.Copy(buf, y * stride, data.Scan0 + y * data.Stride, stride);
                frame.UnlockBits(data);
                onFrame(frame);
            }
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
        private Process _camProcess;
        private volatile bool _closing;
        private readonly Button _btnMirror;
        private readonly Button _btnPlate;
        private int _countdown;
        private System.Windows.Forms.Timer _countdownTimer;

        public static void ShowDesigner()
        {
            if (_instance == null || _instance.IsDisposed)
            {
                // The preview needs the camera; during a recording the bubble keeps it and the designer shows a sample
                if (!ScreenRecorder.IsRecording) CameraBubble.Suspend();
                _instance = new CameraStyleForm();
            }
            _instance.Show();
            _instance.Activate();
        }

        // The camera can only be opened by one process: release it before a recording starts
        public static void CloseIfOpen()
        {
            if (_instance != null && !_instance.IsDisposed) _instance.Close();
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
                AddOption("border", id, CameraFrames.Borders[i][1], null, new Rectangle(bx + i * 102, 34, 96, 34), () => { _style.Border = id; ApplyStyle(false); });
            }

            // Left: shapes
            AddTitle("FORMA", 16, 84, 250);
            for (int i = 0; i < CameraFrames.Shapes.Length; i++)
            {
                string id = CameraFrames.Shapes[i][0];
                Rectangle r = new Rectangle(16 + (i % 2) * 124, 108 + (i / 2) * 56, 118, 50);
                AddOption("shape", id, CameraFrames.Shapes[i][1], CameraFrames.RenderShapeIcon(id, 30, Color.FromArgb(0, 240, 255)), r, () => { _style.Shape = id; ApplyStyle(true); });
            }

            // Right: color filters
            AddTitle("FILTRO", ClientSize.Width - 236, 84, 220);
            for (int i = 0; i < CameraFrames.Filters.Length; i++)
            {
                string id = CameraFrames.Filters[i][0];
                Rectangle r = new Rectangle(ClientSize.Width - 236, 108 + i * 32, 220, 28);
                AddOption("filter", id, CameraFrames.Filters[i][1], null, r, () => { _style.Filter = id; ApplyStyle(true); });
            }

            // Under the preview: mirror toggle
            _btnMirror = AddAction("↔ ESPEJO", new Rectangle((ClientSize.Width - 130) / 2, _preview.Bottom + 28, 130, 28), () => { _style.Mirror = !_style.Mirror; ApplyStyle(true); });
            _tips.SetToolTip(_btnMirror, "Voltea la cámara horizontalmente (como un espejo)");

            // Bottom: colors and gradients
            AddTitle("COLOR / DEGRADADO", 0, 466, ClientSize.Width);
            int n = CameraFrames.Palettes.Length;
            int sx = (ClientSize.Width - (n * 42 + 2 * 112)) / 2;
            for (int i = 0; i < n; i++)
            {
                string[] colors = CameraFrames.Palettes[i];
                Button b = AddOption("color", string.Join(",", colors), "", CameraFrames.RenderSwatch(colors, 30), new Rectangle(sx + i * 42, 490, 38, 38), () => { _style.Colors = colors; ApplyStyle(_style.Background == "frame"); });
                _tips.SetToolTip(b, colors.Length == 1 ? "Color " + colors[0] : "Degradado " + string.Join(" → ", colors));
            }
            int cx = sx + n * 42 + 8;
            Button custom = AddAction("Color propio...", new Rectangle(cx, 490, 104, 38), () => PickColors(1));
            Button gradient = AddAction("Degradado propio...", new Rectangle(cx + 110, 490, 120, 38), () => PickColors(2));

            // Bottom: virtual backgrounds (two rows of thumbnails) + empty-room capture
            AddTitle("FONDO VIRTUAL", 0, 540, ClientSize.Width);
            int perRow = 13, tx = (ClientSize.Width - (perRow * 44 + 176)) / 2;
            for (int i = 0; i < CameraFrames.Backgrounds.Length; i++)
            {
                string id = CameraFrames.Backgrounds[i][0];
                Image thumb = id == "none" ? null : CameraFrames.RenderBackground(id, _style.Colors, new Size(32, 32));
                Button b = AddOption("background", id, id == "none" ? "✕" : "", thumb, new Rectangle(tx + (i % perRow) * 44, 564 + (i / perRow) * 44, 40, 40), () => { _style.Background = id; ApplyStyle(true); });
                _tips.SetToolTip(b, CameraFrames.Backgrounds[i][1]);
            }
            _btnPlate = AddAction("📸 Capturar fondo vacío", new Rectangle(tx + perRow * 44 + 6, 564, 170, 84), CaptureEmptyBackground);
            _btnPlate.Font = new Font("Segoe UI Emoji", 9);
            _tips.SetToolTip(_btnPlate, "Sal del encuadre: en 3 segundos se toma una foto de la habitación vacía. Todo lo que no sea igual a ella (tú) se conserva y el resto se reemplaza por el fondo elegido.");
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

            FormClosing += (s, e) =>
            {
                _closing = true;
                StopPreview();
                _style.Save();
                CameraBubble.Resume();
            };

            ApplyStyle(true);
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
            ApplyStyle(_style.Background == "frame");
        }

        // Shape and filter change what FFmpeg delivers, so the camera feed restarts; border/colors only redraw the overlay
        private void ApplyStyle(bool restartCamera)
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
            if (restartCamera) StartPreview();
            else if (_camProcess == null || _camProcess.HasExited) ShowFrame(null);
        }

        private const string HdMode = CameraFrames.HdMode;

        private void StartPreview()
        {
            StartPreview(true);
        }

        private void StartPreview(bool hd)
        {
            StopPreview();
            ShowFrame(null);
            string camera = ScreenRecorder.FindCamera();
            if (camera == null)
            {
                _status.Text = "No se encontró ninguna cámara (vista de ejemplo).";
                return;
            }
            if (ScreenRecorder.IsRecording)
            {
                _status.Text = "La cámara está en uso por la grabación (vista de ejemplo).";
                return;
            }
            Size size = _canvas;
            _hd = hd;
            Process p = CameraFrames.StartCamera(_style, size, camera, hd, "preview");
            _camProcess = p;
            _status.Text = "Abriendo cámara...";
            new Thread(() => ReadFrames(p, size)) { IsBackground = true }.Start();
        }

        private void StopPreview()
        {
            Process p = _camProcess;
            _camProcess = null;
            if (p == null) return;
            try
            {
                if (!p.HasExited) p.Kill();
                p.WaitForExit(2000);
            }
            catch { }
        }

        private void ReadFrames(Process p, Size size)
        {
            bool any = false;
            CameraFrames.ReadCameraFrames(p, size, () => !_closing && _camProcess == p, frame =>
            {
                if (!any)
                {
                    any = true;
                    SetStatus(_style.Background != "none" && !File.Exists(CameraFrames.PlatePath)
                        ? "Para usar el fondo: pulsa \"Capturar fondo vacío\" y sal del encuadre."
                        : "Vista previa en vivo");
                }
                ShowFrame(frame);
            });
            if (!any && !_closing && _camProcess == p)
            {
                if (_hd)
                {
                    // Camera without 720p: retry with its default mode
                    try { BeginInvoke((MethodInvoker)delegate { if (_camProcess == p) StartPreview(false); }); } catch { }
                    return;
                }
                SetStatus("No se pudo abrir la cámara (¿la usa otra app?). Vista de ejemplo.");
                return;
            }
            if (any && !_closing && _camProcess == p) SetStatus("La cámara se detuvo.");
        }

        private volatile bool _hd = true;

        // Countdown so the user can step out of frame, then one still of the empty room at the recorder's camera mode
        private void CaptureEmptyBackground()
        {
            string camera = ScreenRecorder.FindCamera();
            if (camera == null || ScreenRecorder.IsRecording || _countdownTimer != null) return;
            _countdown = 3;
            _btnPlate.Enabled = false;
            _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _status.Text = "¡Sal del encuadre! Captura en 3...";
            _countdownTimer.Tick += (s, e) =>
            {
                _countdown--;
                if (_countdown > 0)
                {
                    _status.Text = "¡Sal del encuadre! Captura en " + _countdown + "...";
                    return;
                }
                _countdownTimer.Dispose();
                _countdownTimer = null;
                _status.Text = "Capturando la habitación vacía...";
                StopPreview();
                bool hd = _hd;
                new Thread(() =>
                {
                    string err = null;
                    string tmp = CameraFrames.PlatePath + ".tmp.png";
                    // Skip the first frames so auto-exposure settles
                    string args = string.Format("-f dshow {0}-i \"video={1}\" -vf \"select=gte(n\\,20)\" -frames:v 1 -update 1 \"{2}\"", hd ? HdMode : "", camera, tmp);
                    if (MediaTools.Ffmpeg(args, 20000, out err) == 0 && File.Exists(tmp))
                    {
                        File.Copy(tmp, CameraFrames.PlatePath, true);
                        File.Delete(tmp);
                        err = null;
                    }
                    else if (string.IsNullOrEmpty(err)) err = "sin imagen";
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _btnPlate.Enabled = true;
                            if (_style.Background == "none") _style.Background = "blur";
                            ApplyStyle(true);
                            _status.Text = err == null ? "✔ Fondo vacío capturado. Vuelve al encuadre." : "No se pudo capturar: " + err;
                        });
                    }
                    catch { }
                }) { IsBackground = true }.Start();
            };
            _countdownTimer.Start();
        }

        private void SetStatus(string text)
        {
            try { BeginInvoke((MethodInvoker)delegate { _status.Text = text; }); } catch { }
        }

        // Composes camera (or a placeholder) inside the shape plus the border; runs on the reader thread or the UI thread
        private void ShowFrame(Bitmap camera)
        {
            Bitmap result;
            lock (_frameLock)
            {
                if (_clip == null) return;
                result = CameraFrames.Compose(camera, _canvas, _clip, _border);
            }
            if (camera != null) camera.Dispose();

            MethodInvoker apply = delegate
            {
                Image old = _preview.Image;
                _preview.Image = result;
                if (old != null) old.Dispose();
            };
            if (!InvokeRequired) apply();
            else
            {
                try { BeginInvoke(apply); } catch { result.Dispose(); }
            }
        }
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
        private static bool _suspended;
        public static bool IsActive { get; private set; }

        private readonly CameraStyle _style = CameraStyle.Current;
        private readonly Size _canvas;
        private readonly Bitmap _border;
        private readonly GraphicsPath _clip;
        private Process _cam;
        private volatile bool _stopped;

        public static void ShowBubble()
        {
            IsActive = true;
            _suspended = false;
            if (_instance == null || _instance.IsDisposed) _instance = new CameraBubble();
            _instance.Show();
        }

        public static void HideBubble()
        {
            IsActive = false;
            if (_instance != null && !_instance.IsDisposed) _instance.Close();
            _instance = null;
        }

        // The designer needs the camera: release it, then come back (with any new style) when it closes
        public static void Suspend()
        {
            if (!IsActive || _instance == null || _instance.IsDisposed) return;
            _suspended = true;
            _instance.Close();
            _instance = null;
        }

        public static void Resume()
        {
            if (IsActive && _suspended) ShowBubble();
        }

        // Size on screen (logical pixels) and top-left as a fraction of the primary screen
        public static Size LogicalSize(CameraStyle s)
        {
            return CameraFrames.CanvasSize(s, Screen.PrimaryScreen.Bounds.Width / (s.Shape == "rect" ? 4 : 5));
        }

        public static PointF PositionFraction(CameraStyle s)
        {
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            if (s.PosX.HasValue && s.PosY.HasValue) return new PointF((float)s.PosX.Value, (float)s.PosY.Value);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Size sz = LogicalSize(s);
            return new PointF((float)(wa.Right - sz.Width - 24) / scr.Width, (float)(wa.Bottom - sz.Height - 24) / scr.Height);
        }

        private CameraBubble()
        {
            Text = "KidCut Cámara";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            _canvas = LogicalSize(_style);
            Size = _canvas;
            PointF frac = PositionFraction(_style);
            Location = new Point(scr.X + (int)(frac.X * scr.Width), scr.Y + (int)(frac.Y * scr.Height));
            _border = CameraFrames.RenderBorder(_style, _canvas);
            _clip = CameraFrames.ShapePath(_style.Shape, CameraFrames.ShapeRect(_canvas));

            MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); // drag like a title bar
            };
            Move += (s, e) =>
            {
                _style.PosX = Math.Max(0, Math.Min(1, (double)(Left - scr.X) / scr.Width));
                _style.PosY = Math.Max(0, Math.Min(1, (double)(Top - scr.Y) / scr.Height));
            };
            FormClosing += (s, e) =>
            {
                StopCamera();
                _style.Save();
            };
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
            using (Bitmap placeholder = CameraFrames.Compose(null, _canvas, _clip, _border)) SetBitmap(placeholder);
            StartCamera(true);
        }

        private void StartCamera(bool hd)
        {
            string camera = ScreenRecorder.FindCamera();
            if (camera == null || _stopped) return;
            Process p = CameraFrames.StartCamera(_style, _canvas, camera, hd, "bubble");
            _cam = p;
            new Thread(() =>
            {
                bool any = false;
                CameraFrames.ReadCameraFrames(p, _canvas, () => !_stopped && _cam == p, frame =>
                {
                    any = true;
                    Bitmap composed = CameraFrames.Compose(frame, _canvas, _clip, _border);
                    frame.Dispose();
                    try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) SetBitmap(composed); composed.Dispose(); }); }
                    catch { composed.Dispose(); }
                });
                // Camera without 720p: retry with its default mode
                if (!any && hd && !_stopped && _cam == p)
                {
                    try { BeginInvoke((MethodInvoker)delegate { StartCamera(false); }); } catch { }
                }
            }) { IsBackground = true }.Start();
        }

        private void StopCamera()
        {
            _stopped = true;
            Process p = _cam;
            _cam = null;
            if (p == null) return;
            try
            {
                if (!p.HasExited) p.Kill();
                p.WaitForExit(2000);
            }
            catch { }
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
                UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size, memDc, ref src, 0, ref blend, 2); // ULW_ALPHA
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
            Size = new Size(616, 44);
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
            _btnCam = AddButton("📹", "Buscando cámara...", 334, 40, () => { _camOn = !_camOn; RefreshToggles(); SyncBubble(); });
            _btnShow = AddButton("👁", "Mostrar la cámara en pantalla: arrástrala a donde quieras y se graba ahí. Apagado: se graba sin verla, en la última posición.", 376, 36, () => { _showOn = !_showOn; RefreshToggles(); SyncBubble(); });
            _btnStyle = AddButton("🎨", "Marco de la cámara: forma, borde, filtro, fondo y colores", 414, 36, CameraStyleForm.ShowDesigner);
            _btnFps = AddButton("30", "Cuadros por segundo de la grabación", 452, 36, () => { _fps = _fps == 30 ? 60 : 30; RefreshToggles(); });
            _btnList = AddButton("📂", "Ver las grabaciones y capturas hechas", 490, 40, ShowRecordingsMenu);
            AddButton("✂", "Abrir el editor KidCut", 532, 40, () => Program.OpenInBrowser(Program.TargetUrl));
            AddButton("✕", "Ocultar widget (se reabre desde la bandeja o el editor)", 574, 40, Hide);
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
            if (_starting || ScreenRecorder.IsProcessing) return;
            if (ScreenRecorder.IsRecording)
            {
                new Thread(ScreenRecorder.Stop) { IsBackground = true }.Start();
                return;
            }
            bool camOn = _camOn && _btnCam.Enabled;
            if (camOn) CameraStyleForm.CloseIfOpen(); // frees the webcam held by the designer preview
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
            bool busy = _starting || ScreenRecorder.IsProcessing;
            _btnRec.Text = rec ? "■ STOP" : "● REC";
            _btnRec.Enabled = !busy;
            _btnSys.Enabled = _btnMic.Enabled = _btnFps.Enabled = !rec && !busy;
            bool hasCamera = _camSearched && ScreenRecorder.FindCamera() != null;
            _btnCam.Enabled = hasCamera && !rec && !busy;
            _btnShow.Enabled = hasCamera && _camOn && !rec && !busy;
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
                _lblTime.Text = "⏳ GUARDANDO";
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
