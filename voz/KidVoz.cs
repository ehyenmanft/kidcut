using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// KidVoz: real-time microphone enhancer, independent from KidCut. Captures a microphone, cleans it (whine notches,
// spectral noise suppression, gate, automatic level, compressor, limiter; or the ASMR voicing) and plays it into an
// output device: a virtual cable (other apps then use its other end as their microphone) or headphones to listen.
namespace KidVoz
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Self-check / offline use: KidVoz.exe --test in.wav out.wav [asmr]
            if (args.Length >= 3 && args[0] == "--test")
            {
                Environment.ExitCode = OfflineTest(args[1], args[2], args.Length > 3 && args[3] == "asmr");
                return;
            }
            bool created;
            using (new Mutex(true, "KidVoz.SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }

        static int OfflineTest(string input, string output, bool asmr)
        {
            float[] x;
            int rate;
            if (!Wav.ReadMono(input, out x, out rate) || rate != VoiceDsp.Rate) return 1;
            VoiceDsp dsp = new VoiceDsp { Enabled = true, Asmr = asmr };
            for (int i = 0; i + 480 <= x.Length; i += 480) dsp.Process(x, i, 480);
            Wav.WriteMono16(output, x, rate);
            File.WriteAllText(output + ".txt", "tones: " + string.Join(" ", dsp.Tones.Select(t => t.ToString("0"))) +
                "\r\ngainDb: " + dsp.GainDb.ToString("0.0") + "\r\n");
            return 0;
        }
    }

    #region DSP

    class Biquad
    {
        double b0, b1, b2, a1, a2, z1, z2;

        public static Biquad Make(string type, double f, double q, double gainDb)
        {
            double w = 2 * Math.PI * f / VoiceDsp.Rate, cs = Math.Cos(w), sn = Math.Sin(w), alpha = sn / (2 * q);
            double A = Math.Pow(10, gainDb / 40), b0, b1, b2, a0, a1, a2;
            switch (type)
            {
                case "hp": b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = b0; a0 = 1 + alpha; a1 = -2 * cs; a2 = 1 - alpha; break;
                case "lp": b0 = (1 - cs) / 2; b1 = 1 - cs; b2 = b0; a0 = 1 + alpha; a1 = -2 * cs; a2 = 1 - alpha; break;
                case "notch": b0 = 1; b1 = -2 * cs; b2 = 1; a0 = 1 + alpha; a1 = -2 * cs; a2 = 1 - alpha; break;
                case "peak": b0 = 1 + alpha * A; b1 = -2 * cs; b2 = 1 - alpha * A; a0 = 1 + alpha / A; a1 = -2 * cs; a2 = 1 - alpha / A; break;
                case "hshelf":
                    {
                        double s = 2 * Math.Sqrt(A) * alpha;
                        b0 = A * ((A + 1) + (A - 1) * cs + s); b1 = -2 * A * ((A - 1) + (A + 1) * cs); b2 = A * ((A + 1) + (A - 1) * cs - s);
                        a0 = (A + 1) - (A - 1) * cs + s; a1 = 2 * ((A - 1) - (A + 1) * cs); a2 = (A + 1) - (A - 1) * cs - s;
                        break;
                    }
                default: // "lshelf"
                    {
                        double s = 2 * Math.Sqrt(A) * alpha;
                        b0 = A * ((A + 1) - (A - 1) * cs + s); b1 = 2 * A * ((A - 1) - (A + 1) * cs); b2 = A * ((A + 1) - (A - 1) * cs - s);
                        a0 = (A + 1) + (A - 1) * cs + s; a1 = -2 * ((A - 1) + (A + 1) * cs); a2 = (A + 1) + (A - 1) * cs - s;
                        break;
                    }
            }
            return new Biquad { b0 = b0 / a0, b1 = b1 / a0, b2 = b2 / a0, a1 = a1 / a0, a2 = a2 / a0 };
        }

        public double Run(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    /// <summary>
    /// Mono 48 kHz voice chain. Normal: highpass, whine notches, spectral noise suppression, gate, automatic level,
    /// presence, compressor, limiter. ASMR: same cleanup but no gate and a gentler suppressor (whispers and breaths
    /// stay), warmth + air shelves and stronger compression so soft sounds come forward.
    /// </summary>
    class VoiceDsp
    {
        public const int Rate = 48000;
        const int N = 512, Hop = N / 2, Bins = N / 2 + 1;

        public volatile bool Enabled = true;
        public volatile bool Asmr;
        public float InPeak, OutPeak;          // for the meters
        public double GainDb;                  // automatic level
        public List<double> Tones = new List<double>();

        readonly Biquad _hp = Biquad.Make("hp", 90, 0.707, 0);
        Biquad[] _notches = new Biquad[0];
        readonly Biquad _presence = Biquad.Make("peak", 3000, 1, 2);
        readonly Biquad _lp = Biquad.Make("lp", 12000, 0.707, 0);
        readonly Biquad _warm = Biquad.Make("lshelf", 150, 0.707, 2);
        readonly Biquad _air = Biquad.Make("hshelf", 6000, 0.707, 5);

        // STFT (sqrt-Hann analysis + synthesis, 50 % overlap: perfect reconstruction)
        readonly double[] _win = new double[N];
        readonly double[] _frame = new double[N], _ola = new double[N];
        readonly double[] _re = new double[N], _im = new double[N];
        readonly double[] _ps = new double[Bins], _noise = new double[Bins], _g = new double[Bins];
        readonly float[] _hopIn = new float[Hop], _hopOut = new float[Hop], _rawDelay = new float[Hop], _rawOut = new float[Hop];
        int _hopPos, _frames;

        const int DetN = 4096;
        readonly double[] _det = new double[DetN], _dre = new double[DetN], _dim = new double[DetN];
        readonly double[] _dps = new double[DetN / 2 + 1], _dnoise = new double[DetN / 2 + 1];
        int _detPos, _detFrames;

        // _speechDb starts at the target: no boost until real speech has been heard
        double _env = 1e-6, _floorDb = -60, _gate = 1, _speechDb = -18, _comp = 0, _lim = 1;
        int _hold;

        public VoiceDsp()
        {
            for (int i = 0; i < N; i++) _win[i] = Math.Sqrt(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N));
            for (int k = 0; k < Bins; k++) _g[k] = 1;
        }

        public void Process(float[] buf, int offset, int count)
        {
            float inPeak = 0, outPeak = 0;
            bool on = Enabled, asmr = Asmr;
            for (int i = offset; i < offset + count; i++)
            {
                float x = buf[i];
                inPeak = Math.Max(inPeak, Math.Abs(x));
                // The STFT always runs (keeps the noise estimate and whine detector warm); its 512-sample delay applies
                // to the bypass too, so switching on/off never jumps in time
                double h = _hp.Run(x);
                _hopIn[_hopPos] = (float)h;
                _det[_detPos] = h;
                if (++_detPos == DetN) { _detPos = 0; DetectFrame(); }
                double clean = _hopOut[_hopPos];
                double raw = _rawOut[_hopPos]; // untouched input, delayed exactly like the STFT output
                _rawOut[_hopPos] = _rawDelay[_hopPos];
                _rawDelay[_hopPos] = x;
                if (++_hopPos == Hop) { _hopPos = 0; Frame(asmr); }
                float o = on ? (float)Post(clean, asmr) : (float)raw;
                buf[i] = o;
                outPeak = Math.Max(outPeak, Math.Abs(o));
            }
            InPeak = inPeak;
            OutPeak = outPeak;
        }

        void Frame(bool asmr)
        {
            Array.Copy(_frame, Hop, _frame, 0, Hop);
            for (int i = 0; i < Hop; i++) _frame[N - Hop + i] = _hopIn[i];
            for (int i = 0; i < N; i++) { _re[i] = _frame[i] * _win[i]; _im[i] = 0; }
            Fft.Run(_re, _im, false);

            // Noise per bin: minimum tracking (falls fast, rises ~1 dB/s), so voice never becomes "noise" but a
            // steady hum, fan or whine does
            double over = asmr ? 1.3 : 1.5, floor = asmr ? 0.3 : 0.15;
            for (int k = 0; k < Bins; k++)
            {
                double p = _re[k] * _re[k] + _im[k] * _im[k];
                _ps[k] = _frames == 0 ? p : 0.7 * _ps[k] + 0.3 * p;
                if (_frames == 0) _noise[k] = p;
                else if (_ps[k] < _noise[k]) _noise[k] = 0.85 * _noise[k] + 0.15 * _ps[k];
                else _noise[k] *= 1.0012;
                double g = Math.Max(floor, 1 - over * Math.Sqrt(_noise[k] / (_ps[k] + 1e-20)));
                _g[k] = 0.5 * _g[k] + 0.5 * g;
                int m = k == 0 || k == N / 2 ? k : N - k;
                _re[k] *= _g[k]; _im[k] *= _g[k];
                if (m != k) { _re[m] *= _g[k]; _im[m] *= _g[k]; }
            }
            Fft.Run(_re, _im, true);
            for (int i = 0; i < N; i++) _ola[i] += _re[i] * _win[i];
            for (int i = 0; i < Hop; i++) _hopOut[i] = (float)_ola[i];
            Array.Copy(_ola, Hop, _ola, 0, N - Hop);
            Array.Clear(_ola, N - Hop, Hop);
            _frames++;
        }

        // Whine detector on its own 4096-point FFT (12 Hz bins): at the suppressor's 94 Hz bins a whine barely clears
        // the noise around it. Same minimum tracking, so voice and music never count as steady.
        void DetectFrame()
        {
            for (int i = 0; i < DetN; i++) { _dre[i] = _det[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / DetN)); _dim[i] = 0; }
            Fft.Run(_dre, _dim, false);
            for (int k = 0; k <= DetN / 2; k++)
            {
                double p = _dre[k] * _dre[k] + _dim[k] * _dim[k];
                if (_detFrames == 0) { _dps[k] = p; _dnoise[k] = p; continue; }
                _dps[k] = 0.7 * _dps[k] + 0.3 * p;
                if (_dps[k] < _dnoise[k]) _dnoise[k] = 0.85 * _dnoise[k] + 0.15 * _dps[k];
                else _dnoise[k] *= 1.01; // ~1 dB/s at 11.7 frames/s
            }
            if (++_detFrames % 24 == 0) DetectWhine(); // every ~2 s
        }

        // A whine is a bin whose noise estimate stands 12 dB above its +-750 Hz neighbourhood: notch it (one per tone)
        void DetectWhine()
        {
            double bw = (double)Rate / DetN;
            double[] db = _dnoise.Select(p => 10 * Math.Log10(p + 1e-20)).ToArray();
            List<KeyValuePair<double, double>> found = new List<KeyValuePair<double, double>>();
            const int R = 64;
            double[] around = new double[2 * R + 1];
            for (int k = (int)(1000 / bw); k < Math.Min(db.Length - R - 1, (int)(16000 / bw)); k++)
            {
                if (db[k] < db[k - 1] || db[k] < db[k + 1]) continue;
                Array.Copy(db, k - R, around, 0, around.Length);
                Array.Sort(around);
                double prom = db[k] - around[R];
                if (prom < 12) continue;
                double d = (db[k - 1] - db[k + 1]) / (2 * (db[k - 1] - 2 * db[k] + db[k + 1])); // parabolic peak
                found.Add(new KeyValuePair<double, double>(prom, (k + d) * bw));
            }
            List<double> tones = new List<double>();
            foreach (KeyValuePair<double, double> t in found.OrderByDescending(t => t.Key))
            {
                if (tones.Count == 6) break;
                if (tones.All(hz => Math.Abs(hz - t.Value) > 300)) tones.Add(t.Value);
            }
            tones.Sort();
            // Rebuilt only when the set really changes: new filters restart from zero state (a tiny click)
            bool same = tones.Count == Tones.Count && tones.Zip(Tones, (a, b) => Math.Abs(a - b) < 60).All(s => s);
            if (same) return;
            Tones = tones;
            _notches = tones.Select(hz => Biquad.Make("notch", hz, 25, 0)).ToArray();
        }

        double Post(double x, bool asmr)
        {
            const double ms = Rate / 1000.0;
            // Notches go after the STFT: the detector must keep seeing the whine it removes, or it would drop the notch
            foreach (Biquad n in _notches) x = n.Run(x);
            x = asmr ? _air.Run(_warm.Run(x)) : _lp.Run(_presence.Run(x));
            double a = x * x;
            _env += (a - _env) / (10 * ms);
            double envDb = 10 * Math.Log10(_env + 1e-12);

            // Room floor: follows drops in 0.5 s, rises 0.5 dB/s; gate opens 6 dB above it (normal mode only)
            if (envDb < _floorDb) _floorDb += (envDb - _floorDb) / (500 * ms);
            else _floorDb += 0.5 / Rate;
            double thr = Math.Max(_floorDb + 6, -70);
            bool open = envDb > thr;
            if (open) _hold = (int)(150 * ms); else if (_hold > 0) _hold--;
            double gateTarget = asmr || open || _hold > 0 ? 1 : 0.125; // -18 dB
            _gate += (gateTarget - _gate) / ((gateTarget > _gate ? 5 : 250) * ms);

            // Automatic level: learns only from sound well above the room floor (speech), up to +18 dB
            if (envDb > thr + 10) _speechDb += (envDb - _speechDb) / (3000 * ms);
            double target = Math.Max(0, Math.Min(18, (asmr ? -22 : -18) - _speechDb));
            GainDb += (target - GainDb) / (2000 * ms);
            x *= _gate * Math.Pow(10, GainDb / 20);

            // Compressor (ASMR: lower threshold and higher ratio + makeup bring soft sounds forward)
            double T = asmr ? -28 : -16, R = asmr ? 4 : 2.5, makeup = asmr ? 6 : 0;
            double lvl = 20 * Math.Log10(Math.Abs(x) + 1e-9);
            double grTarget = lvl > T ? (lvl - T) * (1 - 1 / R) : 0;
            _comp += (grTarget - _comp) / ((grTarget > _comp ? 10 : 150) * ms);
            x *= Math.Pow(10, (makeup - _comp) / 20);

            // Limiter at -1 dBFS: instant attack, 50 ms release
            double ceil = 0.89, need = Math.Abs(x) > ceil ? ceil / Math.Abs(x) : 1;
            if (need < _lim) _lim = need; else _lim += (need - _lim) / (50 * ms);
            return x * _lim;
        }
    }

    static class Fft
    {
        public static void Run(double[] re, double[] im, bool inverse)
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
                double ang = (inverse ? 2 : -2) * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
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
            if (inverse)
                for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
        }
    }

    static class Wav
    {
        // 32-bit float or 16-bit PCM, channels averaged
        public static bool ReadMono(string path, out float[] samples, out int rate)
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
                        tag = r.ReadUInt16(); channels = r.ReadUInt16(); rate = r.ReadInt32();
                        r.ReadInt32(); r.ReadUInt16(); bits = r.ReadUInt16();
                        if (tag == 0xFFFE && size >= 40) { r.ReadBytes(8); tag = r.ReadUInt16(); }
                    }
                    else if (id == "data")
                    {
                        bool isFloat = tag == 3 && bits == 32, isPcm = tag == 1 && bits == 16;
                        if (channels == 0 || !(isFloat || isPcm)) return false;
                        int frameBytes = channels * bits / 8;
                        long avail = s.Length - s.Position;
                        if (size > 0 && size < avail) avail = size;
                        byte[] data = r.ReadBytes((int)(avail / frameBytes * frameBytes));
                        samples = new float[data.Length / frameBytes];
                        for (int i = 0; i < samples.Length; i++)
                        {
                            float sum = 0;
                            for (int c = 0; c < channels; c++)
                            {
                                int o = i * frameBytes + c * bits / 8;
                                sum += isFloat ? BitConverter.ToSingle(data, o) : BitConverter.ToInt16(data, o) / 32768f;
                            }
                            samples[i] = sum / channels;
                        }
                        return true;
                    }
                    s.Position = next;
                }
                return false;
            }
        }

        public static void WriteMono16(string path, float[] x, int rate)
        {
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + x.Length * 2);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(x.Length * 2);
                foreach (float v in x) w.Write((short)Math.Max(-32768, Math.Min(32767, Math.Round(v * 32767))));
            }
        }
    }

    #endregion

    #region WASAPI

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        void GetCount(out int count);
        void Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        void OpenPropertyStore(int access, out IPropertyStore store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid Fmt; public int Pid; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant { [FieldOffset(0)] public short Vt; [FieldOffset(8)] public IntPtr Ptr; }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        void GetBufferSize(out int frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out int padding);
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
        void GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
        void ReleaseBuffer(int frames);
        void GetNextPacketSize(out int frames);
    }

    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioRenderClient
    {
        void GetBuffer(int frames, out IntPtr data);
        void ReleaseBuffer(int frames, int flags);
    }

    class AudioDevice
    {
        public string Id, Name;
        public override string ToString() { return Name; }

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant pv);

        // dataFlow 0 = outputs, 1 = inputs; only active devices
        public static List<AudioDevice> List(int dataFlow)
        {
            List<AudioDevice> list = new List<AudioDevice>();
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDeviceCollection col;
            en.EnumAudioEndpoints(dataFlow, 1, out col);
            int count;
            col.GetCount(out count);
            PropertyKey friendly = new PropertyKey { Fmt = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 14 };
            for (int i = 0; i < count; i++)
            {
                IMMDevice d;
                col.Item(i, out d);
                string id;
                d.GetId(out id);
                IPropertyStore ps;
                d.OpenPropertyStore(0, out ps);
                PropVariant v;
                ps.GetValue(ref friendly, out v);
                string name = v.Ptr != IntPtr.Zero ? Marshal.PtrToStringUni(v.Ptr) : id;
                PropVariantClear(ref v);
                list.Add(new AudioDevice { Id = id, Name = name });
            }
            return list;
        }

        public static string DefaultId(int dataFlow)
        {
            try
            {
                IMMDevice d;
                ((IMMDeviceEnumerator)new MMDeviceEnumeratorCom()).GetDefaultAudioEndpoint(dataFlow, 0, out d);
                string id;
                d.GetId(out id);
                return id;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Microphone -> VoiceDsp -> output device, on one real-time thread. Both streams are opened as 48 kHz float with
    /// Windows doing any rate/channel conversion; a small FIFO absorbs the two devices' clock drift.
    /// </summary>
    class AudioEngine
    {
        public readonly VoiceDsp Dsp = new VoiceDsp();
        public string Error;
        Thread _thread;
        volatile bool _stop;

        [DllImport("avrt.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

        public bool Running { get { return _thread != null && _thread.IsAlive; } }

        public void Start(string inputId, string outputId)
        {
            Stop();
            Error = null;
            _stop = false;
            _thread = new Thread(() => Run(inputId, outputId)) { IsBackground = true, Name = "KidVoz audio" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            if (_thread != null) _thread.Join(2000);
            _thread = null;
        }

        static IntPtr FloatFormat(int channels)
        {
            IntPtr f = Marshal.AllocCoTaskMem(18);
            Marshal.WriteInt16(f, 0, 3);                                   // WAVE_FORMAT_IEEE_FLOAT
            Marshal.WriteInt16(f, 2, (short)channels);
            Marshal.WriteInt32(f, 4, VoiceDsp.Rate);
            Marshal.WriteInt32(f, 8, VoiceDsp.Rate * 4 * channels);
            Marshal.WriteInt16(f, 12, (short)(4 * channels));
            Marshal.WriteInt16(f, 14, 32);
            Marshal.WriteInt16(f, 16, 0);
            return f;
        }

        static IAudioClient Open(string id)
        {
            IMMDevice d;
            ((IMMDeviceEnumerator)new MMDeviceEnumeratorCom()).GetDevice(id, out d);
            Guid iid = typeof(IAudioClient).GUID;
            object o;
            d.Activate(ref iid, 0x17, IntPtr.Zero, out o);
            return (IAudioClient)o;
        }

        void Run(string inputId, string outputId)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
            uint task = 0;
            try { AvSetMmThreadCharacteristics("Pro Audio", ref task); } catch { }
            IAudioClient cap = null, ren = null;
            IntPtr fmtIn = FloatFormat(1), fmtOut = FloatFormat(2);
            try
            {
                const int AutoConvert = unchecked((int)0x80000000) | 0x08000000; // AUTOCONVERTPCM | SRC_DEFAULT_QUALITY
                cap = Open(inputId);
                cap.Initialize(0, AutoConvert, 200000, 0, fmtIn, IntPtr.Zero);   // 20 ms
                ren = Open(outputId);
                ren.Initialize(0, AutoConvert, 600000, 0, fmtOut, IntPtr.Zero);  // 60 ms
                Guid iidCap = typeof(IAudioCaptureClient).GUID, iidRen = typeof(IAudioRenderClient).GUID;
                object o;
                cap.GetService(ref iidCap, out o);
                IAudioCaptureClient capture = (IAudioCaptureClient)o;
                ren.GetService(ref iidRen, out o);
                IAudioRenderClient render = (IAudioRenderClient)o;
                int renFrames;
                ren.GetBufferSize(out renFrames);

                float[] fifo = new float[VoiceDsp.Rate];   // 1 s ring
                int head = 0, count = 0;
                float[] block = new float[VoiceDsp.Rate / 10];
                int target = Math.Min(renFrames - 1, VoiceDsp.Rate * 30 / 1000); // keep ~30 ms queued in the output
                cap.Start();
                ren.Start();
                while (!_stop)
                {
                    Thread.Sleep(3);
                    int packet;
                    capture.GetNextPacketSize(out packet);
                    while (packet > 0)
                    {
                        IntPtr data;
                        int frames, flags;
                        long dp, qp;
                        capture.GetBuffer(out data, out frames, out flags, out dp, out qp);
                        if (block.Length < frames) block = new float[frames];
                        if ((flags & 2) != 0) Array.Clear(block, 0, frames);
                        else Marshal.Copy(data, block, 0, frames);
                        capture.ReleaseBuffer(frames);
                        Dsp.Process(block, 0, frames);
                        for (int i = 0; i < frames; i++)
                        {
                            fifo[(head + count) % fifo.Length] = block[i];
                            if (count < fifo.Length) count++; else head = (head + 1) % fifo.Length;
                        }
                        capture.GetNextPacketSize(out packet);
                    }
                    // Drift: if the queue grows past 120 ms (output clock slower), drop back to 40 ms
                    if (count > VoiceDsp.Rate * 120 / 1000)
                    {
                        int drop = count - VoiceDsp.Rate * 40 / 1000;
                        head = (head + drop) % fifo.Length;
                        count -= drop;
                    }
                    int padding;
                    ren.GetCurrentPadding(out padding);
                    int n = Math.Min(count, target - padding);
                    if (n <= 0) continue;
                    IntPtr buf;
                    render.GetBuffer(n, out buf);
                    float[] stereo = new float[n * 2];
                    for (int i = 0; i < n; i++)
                    {
                        float v = fifo[head];
                        head = (head + 1) % fifo.Length;
                        stereo[2 * i] = stereo[2 * i + 1] = v;
                    }
                    count -= n;
                    Marshal.Copy(stereo, 0, buf, n * 2);
                    render.ReleaseBuffer(n, 0);
                }
                cap.Stop();
                ren.Stop();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                Marshal.FreeCoTaskMem(fmtIn);
                Marshal.FreeCoTaskMem(fmtOut);
                if (cap != null) Marshal.ReleaseComObject(cap);
                if (ren != null) Marshal.ReleaseComObject(ren);
            }
        }
    }

    #endregion

    #region UI

    class RoundToggle : Control
    {
        bool _on;
        public event EventHandler Toggled;
        public bool On { get { return _on; } set { _on = value; Invalidate(); } }

        public RoundToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(0, 0, Width, Height);
                Region = new Region(p);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            On = !On;
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Color.Black);
            Color c1 = _on ? Color.FromArgb(70, 230, 120) : Color.FromArgb(240, 70, 80);
            Color c2 = _on ? Color.FromArgb(10, 120, 60) : Color.FromArgb(130, 10, 30);
            Rectangle r = new Rectangle(4, 4, Width - 9, Height - 9);
            using (LinearGradientBrush b = new LinearGradientBrush(r, c1, c2, 70f)) g.FillEllipse(b, r);
            using (Pen p = new Pen(Color.FromArgb(220, 255, 255, 255), 4)) g.DrawEllipse(p, r);
            using (Font f = new Font("Segoe UI", Width / 7f, FontStyle.Bold))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(_on ? "ON" : "OFF", f, Brushes.White, ClientRectangle, sf);
            if (Focused) using (Pen p = new Pen(Color.FromArgb(255, 210, 0), 2) { DashStyle = DashStyle.Dot }) g.DrawEllipse(p, 9, 9, Width - 19, Height - 19);
        }
    }

    class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(14, 10, 28), Panel = Color.FromArgb(30, 26, 55), Text1 = Color.FromArgb(235, 230, 250);
        readonly AudioEngine _engine = new AudioEngine();
        readonly RoundToggle _toggle = new RoundToggle();
        readonly CheckBox _asmr = new CheckBox();
        readonly ComboBox _input = new ComboBox(), _output = new ComboBox();
        readonly Label _status = new Label(), _state = new Label();
        readonly ProgressBar _meterIn = new ProgressBar(), _meterOut = new ProgressBar();
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 60 };
        readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KidVoz.ini");
        bool _loading;

        public MainForm()
        {
            Text = "KidVoz";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Bg;
            ForeColor = Text1;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(360, 500);
            StartPosition = FormStartPosition.CenterScreen;

            Label title = new Label { Text = "KidVoz · mejora de voz", Font = new Font("Segoe UI", 13, FontStyle.Bold), AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(0, 12, 360, 30) };
            _toggle.Bounds = new Rectangle(100, 52, 160, 160);
            _toggle.Toggled += (s, e) => Apply();
            _state.Bounds = new Rectangle(0, 216, 360, 22);
            _state.TextAlign = ContentAlignment.MiddleCenter;

            _asmr.Text = "Modo ASMR (susurros y detalle cercano)";
            _asmr.Bounds = new Rectangle(24, 244, 320, 24);
            _asmr.ForeColor = Text1;
            _asmr.CheckedChanged += (s, e) => Apply();

            Label li = new Label { Text = "Micrófono", Bounds = new Rectangle(24, 276, 312, 18) };
            _input.Bounds = new Rectangle(24, 294, 312, 24);
            Label lo = new Label { Text = "Enviar la voz a", Bounds = new Rectangle(24, 324, 312, 18) };
            _output.Bounds = new Rectangle(24, 342, 312, 24);
            foreach (ComboBox c in new[] { _input, _output })
            {
                c.DropDownStyle = ComboBoxStyle.DropDownList;
                c.BackColor = Panel;
                c.ForeColor = Text1;
                c.FlatStyle = FlatStyle.Flat;
                c.SelectedIndexChanged += (s, e) => { if (!_loading) Restart(); };
            }

            Label lmi = new Label { Text = "Entrada", Bounds = new Rectangle(24, 376, 60, 16) };
            _meterIn.Bounds = new Rectangle(86, 378, 250, 12);
            Label lmo = new Label { Text = "Salida", Bounds = new Rectangle(24, 396, 60, 16) };
            _meterOut.Bounds = new Rectangle(86, 398, 250, 12);
            _status.Bounds = new Rectangle(16, 418, 328, 76);
            _status.ForeColor = Color.FromArgb(190, 180, 220);

            Controls.AddRange(new Control[] { title, _toggle, _state, _asmr, li, _input, lo, _output, lmi, _meterIn, lmo, _meterOut, _status });

            LoadDevicesAndSettings();
            _timer.Tick += (s, e) => Tick();
            _timer.Start();
            FormClosing += (s, e) => { SaveSettings(); _engine.Stop(); };
        }

        void LoadDevicesAndSettings()
        {
            _loading = true;
            Dictionary<string, string> st = new Dictionary<string, string>();
            try
            {
                foreach (string line in File.ReadAllLines(_settingsPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) st[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch { }
            string v;
            try
            {
                Fill(_input, AudioDevice.List(1), st.TryGetValue("input", out v) ? v : AudioDevice.DefaultId(1), null);
                // A virtual cable is the natural destination: other apps then take its other end as their microphone
                Fill(_output, AudioDevice.List(0), st.TryGetValue("output", out v) ? v : null, AudioDevice.DefaultId(0));
            }
            catch (Exception ex) { _status.Text = "No se pudieron listar los dispositivos: " + ex.Message; }
            _toggle.On = !st.TryGetValue("on", out v) || v == "1";
            _asmr.Checked = st.TryGetValue("asmr", out v) && v == "1";
            _loading = false;
            Restart();
        }

        static bool IsVirtual(AudioDevice d)
        {
            string n = d.Name.ToLowerInvariant();
            return n.Contains("cable") || n.Contains("virtual") || n.Contains("voicemeeter");
        }

        static void Fill(ComboBox box, List<AudioDevice> devices, string preferredId, string fallbackId)
        {
            box.Items.Clear();
            foreach (AudioDevice d in devices) box.Items.Add(d);
            AudioDevice pick = devices.FirstOrDefault(d => d.Id == preferredId)
                ?? (fallbackId != null ? devices.FirstOrDefault(IsVirtual) : null)
                ?? devices.FirstOrDefault(d => d.Id == fallbackId)
                ?? devices.FirstOrDefault();
            if (pick != null) box.SelectedItem = pick;
        }

        void Restart()
        {
            AudioDevice i = _input.SelectedItem as AudioDevice, o = _output.SelectedItem as AudioDevice;
            if (i == null || o == null) { _status.Text = "Falta un micrófono o una salida de audio."; return; }
            _engine.Start(i.Id, o.Id);
            Apply();
        }

        void Apply()
        {
            _engine.Dsp.Enabled = _toggle.On;
            _engine.Dsp.Asmr = _asmr.Checked;
            _state.Text = _toggle.On ? (_asmr.Checked ? "Mejora activa · ASMR" : "Mejora activa · voz limpia") : "Desactivada · el micrófono pasa tal cual";
            _state.ForeColor = _toggle.On ? Color.FromArgb(110, 235, 150) : Color.FromArgb(250, 120, 130);
            AudioDevice o = _output.SelectedItem as AudioDevice;
            if (o != null)
                _status.Text = IsVirtual(o)
                    ? "En Discord, OBS o el juego elige como micrófono el otro extremo de \"" + o.Name + "\" (p. ej. CABLE Output)."
                    : "Sin micrófono virtual: te escuchas en \"" + o.Name + "\". Para que otras apps reciban la voz mejorada, instala un cable virtual (p. ej. VB-CABLE).";
            if (!_loading) SaveSettings();
        }

        void Tick()
        {
            _meterIn.Value = Meter(_engine.Dsp.InPeak);
            _meterOut.Value = Meter(_engine.Dsp.OutPeak);
            if (_engine.Error != null && !_engine.Running) _status.Text = "Error de audio: " + _engine.Error;
        }

        static int Meter(float peak)
        {
            double db = 20 * Math.Log10(peak + 1e-9);
            return (int)Math.Max(0, Math.Min(100, (db + 60) / 60 * 100));
        }

        void SaveSettings()
        {
            try
            {
                AudioDevice i = _input.SelectedItem as AudioDevice, o = _output.SelectedItem as AudioDevice;
                File.WriteAllLines(_settingsPath, new[]
                {
                    "on=" + (_toggle.On ? "1" : "0"),
                    "asmr=" + (_asmr.Checked ? "1" : "0"),
                    "input=" + (i != null ? i.Id : ""),
                    "output=" + (o != null ? o.Id : "")
                });
            }
            catch { }
        }
    }

    #endregion
}
