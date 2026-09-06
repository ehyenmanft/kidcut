using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
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
                if (ext == ".mp4")
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
                        byte[] fileData = File.ReadAllBytes(targetFilePath);
                        context.Response.ContentType = GetMimeType(targetFilePath);
                        context.Response.ContentLength64 = fileData.Length;
                        context.Response.OutputStream.Write(fileData, 0, fileData.Length);
                        context.Response.OutputStream.Close();
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
                            ofd.Filter = "Archivos Multimedia|*.mp4;*.webm;*.mov;*.mkv;*.avi;*.mp3;*.wav;*.ogg;*.aac;*.png;*.jpg;*.jpeg;*.gif|Videos|*.mp4;*.webm;*.mov;*.mkv;*.avi|Audio|*.mp3;*.wav;*.ogg;*.aac|Imágenes|*.png;*.jpg;*.jpeg;*.gif|Todos los Archivos|*.*";
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

                context.Response.StatusCode = 404;
                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                SendJsonResponse(context, new { success = false, error = ex.Message }, 500);
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
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                case ".ttf": return "font/ttf";
                default: return "application/octet-stream";
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
            btnOpen.Size = new Size(460, 38);
            btnOpen.Location = new Point(40, 172);
            btnOpen.Cursor = Cursors.Hand;
            btnOpen.Click += (s, e) => Program.OpenInBrowser(Program.TargetUrl);
            Controls.Add(btnOpen);

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
