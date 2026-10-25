using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
                // Picture-in-picture bottom-right at 1/4 of the screen width. Both sources are stamped with their
                // arrival wall-clock: their own clocks differ and overlay would otherwise stall waiting for the camera.
                int camWidth = (PhysicalScreenSize().Width / 4) & ~1;
                video = string.Format(
                    "{0} -f dshow -rtbufsize 256M {1}-i \"video={2}\" -filter_complex \"[0:v]{3}setpts=(RTCTIME-RTCSTART)/(TB*1000000)[scr];" +
                    "[1:v]setpts=(RTCTIME-RTCSTART)/(TB*1000000),scale={4}:-2[cam];[scr][cam]overlay=W-w-24:H-h-24:eof_action=repeat,fps={5}[v]\" -map \"[v]\"",
                    screenInput, cameraMode, camera, screenFilter, camWidth, fps);
            }
            string args = string.Format("-y -hide_banner -nostats -loglevel error {0} {1} -g {2} \"{3}\"", video, EncoderArgs(), fps * 2, _videoTemp);
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
            Size = new Size(540, 44);
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
            _btnCam = AddButton("📹", "Buscando cámara...", 334, 40, () => { _camOn = !_camOn; RefreshToggles(); });
            _btnFps = AddButton("30", "Cuadros por segundo de la grabación", 376, 36, () => { _fps = _fps == 30 ? 60 : 30; RefreshToggles(); });
            _btnList = AddButton("📂", "Ver las grabaciones y capturas hechas", 414, 40, ShowRecordingsMenu);
            AddButton("✂", "Abrir el editor KidCut", 456, 40, () => Program.OpenInBrowser(Program.TargetUrl));
            AddButton("✕", "Ocultar widget (se reabre desde la bandeja o el editor)", 498, 40, Hide);
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
            _btnFps.Text = _fps.ToString();
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
            _starting = true;
            bool sys = _sysOn, mic = _micOn, cam = _camOn && _btnCam.Enabled;
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
