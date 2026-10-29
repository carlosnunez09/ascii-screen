// ASCII Saver - a Windows screen saver that turns your live desktop, your photos,
// or generated animations into ASCII art (in the spirit of asciify.org).
//
// Build:  see build.ps1        (C# 5 / .NET Framework 4, no SDK needed)
// Flags:  /s  run    /c  settings    /p <hwnd>  preview pane
//         /w  windowed demo (Esc to close)
//         /test <out.png> [key=value ...]   render one frame headlessly

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("ASCII Saver")]
[assembly: System.Reflection.AssemblyProduct("ASCII Saver")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace AsciiSaver
{
    enum SourceKind { Desktop = 0, Images = 1, Animation = 2 }
    enum ColorStyle { Original = 0, Green = 1, Amber = 2, White = 3, Cyan = 4, Rainbow = 5 }

    // ------------------------------------------------------------------ settings

    class Settings
    {
        public SourceKind Source = SourceKind.Desktop;
        public int Animation = 4;              // 0 plasma, 1 tunnel, 2 fire, 3 ripples, 4 cycle all
        public ColorStyle Style = ColorStyle.Original;
        public int Charset = 0;
        public string CustomChars = " .oO@";
        public string FontName = "Consolas";
        public int FontSize = 12;              // pixels
        public int Fps = 20;
        public int Contrast = 130;             // percent
        public int Brightness = 0;             // -50..50 (percent)
        public bool Invert = false;
        public bool Scanline = true;
        public bool AutoLevels = true;
        public string ImageFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        public int SlideSeconds = 8;

        public static readonly string[] CharsetNames = { "Classic", "Detailed", "Blocks", "Binary", "Fish", "Custom" };
        public static readonly string[] CharsetRamps =
        {
            " .:-=+*#%@",
            " .'`^\",:;Il!i><~+_-?][}{1)(|\\/tfjrxnuvczXYUJCLQ0OZmwqpdbkhao*#MW&8%B@$",
            " ░▒▓█",
            " 10",
            " .',:;-toxXS%#8@",
            null
        };
        public static readonly string[] AnimationNames = { "Plasma", "Tunnel", "Fire", "Ocean", "Cycle through all" };

        public string Ramp()
        {
            string r = CharsetRamps[Math.Max(0, Math.Min(Charset, CharsetRamps.Length - 1))];
            if (r == null)
            {
                r = CustomChars;
                if (string.IsNullOrEmpty(r) || r.Length < 2) r = " .oO@";
            }
            return r;
        }

        static string FilePath()
        {
            return Path.Combine(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AsciiSaver"), "settings.ini");
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                string p = FilePath();
                if (File.Exists(p))
                    foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) s.Apply(line.Substring(0, eq).Trim(), line.Substring(eq + 1));
                    }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            string p = FilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            List<string> l = new List<string>();
            l.Add("Source=" + Source);
            l.Add("Animation=" + Animation);
            l.Add("Style=" + Style);
            l.Add("Charset=" + Charset);
            l.Add("CustomChars=" + CustomChars);
            l.Add("FontName=" + FontName);
            l.Add("FontSize=" + FontSize);
            l.Add("Fps=" + Fps);
            l.Add("Contrast=" + Contrast);
            l.Add("Brightness=" + Brightness);
            l.Add("Invert=" + Invert);
            l.Add("Scanline=" + Scanline);
            l.Add("AutoLevels=" + AutoLevels);
            l.Add("ImageFolder=" + ImageFolder);
            l.Add("SlideSeconds=" + SlideSeconds);
            File.WriteAllLines(p, l.ToArray(), Encoding.UTF8);
        }

        // Applies one key=value pair. Used for the ini file and the /test overrides.
        public void Apply(string key, string val)
        {
            try
            {
                switch (key.ToLowerInvariant())
                {
                    case "source":
                        string v = val.Trim().ToLowerInvariant();
                        if (v == "desktop") Source = SourceKind.Desktop;
                        else if (v == "images") Source = SourceKind.Images;
                        else if (v == "animation") Source = SourceKind.Animation;
                        else if (v == "plasma" || v == "tunnel" || v == "fire" || v == "ocean" || v == "ripples")
                        {
                            Source = SourceKind.Animation;
                            Animation = v == "ripples" ? 3 : Array.IndexOf(new string[] { "plasma", "tunnel", "fire", "ocean" }, v);
                        }
                        break;
                    case "animation": Animation = Clamp(int.Parse(val), 0, 4); break;
                    case "style": Style = (ColorStyle)Enum.Parse(typeof(ColorStyle), val.Trim(), true); break;
                    case "charset": Charset = Clamp(int.Parse(val), 0, CharsetRamps.Length - 1); break;
                    case "customchars": CustomChars = val; break;
                    case "fontname": FontName = val.Trim(); break;
                    case "fontsize": FontSize = Clamp(int.Parse(val), 4, 40); break;
                    case "fps": Fps = Clamp(int.Parse(val), 5, 60); break;
                    case "contrast": Contrast = Clamp(int.Parse(val), 50, 300); break;
                    case "brightness": Brightness = Clamp(int.Parse(val), -50, 50); break;
                    case "invert": Invert = bool.Parse(val.Trim()); break;
                    case "scanline": Scanline = bool.Parse(val.Trim()); break;
                    case "autolevels": AutoLevels = bool.Parse(val.Trim()); break;
                    case "imagefolder": ImageFolder = val.Trim(); break;
                    case "slideseconds": SlideSeconds = Clamp(int.Parse(val), 2, 600); break;
                }
            }
            catch { }
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }

    // ------------------------------------------------------------------ native

    static class Native
    {
        public const int GWL_STYLE = -16;
        public const int WS_CHILD = 0x40000000;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int value);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    }

    // ------------------------------------------------------------------ cell grid

    // A small cols x rows bitmap. Sources paint into it (or write Px directly);
    // the engine then turns each pixel into one character cell.
    class Grid : IDisposable
    {
        public readonly int Cols, Rows;
        public readonly Bitmap Bmp;
        public readonly Graphics G;
        public readonly int[] Px;

        public Grid(int cols, int rows)
        {
            Cols = cols; Rows = rows;
            Bmp = new Bitmap(cols, rows, PixelFormat.Format32bppRgb);
            G = Graphics.FromImage(Bmp);
            G.InterpolationMode = InterpolationMode.HighQualityBicubic;
            G.PixelOffsetMode = PixelOffsetMode.HighQuality;
            G.CompositingMode = CompositingMode.SourceCopy;
            Px = new int[cols * rows];
        }

        public void Read()
        {
            BitmapData d = Bmp.LockBits(new Rectangle(0, 0, Cols, Rows), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try { Marshal.Copy(d.Scan0, Px, 0, Px.Length); }
            finally { Bmp.UnlockBits(d); }
        }

        public void Dispose() { G.Dispose(); Bmp.Dispose(); }
    }

    // ------------------------------------------------------------------ sources

    abstract class Source : IDisposable
    {
        // Fill grid.Px. aspect = output width / height.
        public abstract void Fill(Grid grid, double t, double aspect);
        // True when the source is already tuned for the ramp and should skip auto-levels.
        public virtual bool NaturalLevels { get { return false; } }
        // Draw bright cells as shaded tiles with a dark letter set in (the ocean's look).
        public virtual bool InlayTiles { get { return false; } }
        // Letters the source looks best in; used when the Classic set is selected.
        public virtual string PreferredRamp { get { return null; } }
        public virtual void Dispose() { }
    }

    // The live desktop. The saver window is excluded from screen capture
    // (Windows 10 2004+), so we see the real desktop behind it, updating live.
    // Without that, we fall back to a snapshot taken before the window appeared.
    class DesktopSource : Source
    {
        readonly Rectangle bounds;
        readonly Bitmap shot;
        readonly Graphics shotG;
        public bool Live;

        public DesktopSource(Rectangle b)
        {
            bounds = b;
            shot = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppRgb);
            shotG = Graphics.FromImage(shot);
            Capture();
        }

        void Capture()
        {
            // BitBlt with CAPTUREBLT also grabs layered/transparent windows.
            bool ok = false;
            try
            {
                IntPtr screenDc = Native.GetDC(IntPtr.Zero);
                IntPtr dst = shotG.GetHdc();
                try { ok = Native.BitBlt(dst, 0, 0, bounds.Width, bounds.Height, screenDc, bounds.X, bounds.Y, 0x40CC0020); }
                finally { shotG.ReleaseHdc(dst); Native.ReleaseDC(IntPtr.Zero, screenDc); }
            }
            catch { }
            if (!ok)
            {
                try { shotG.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy); }
                catch { }
            }
        }

        public override void Fill(Grid grid, double t, double aspect)
        {
            if (Live) Capture();
            grid.G.DrawImage(shot, new Rectangle(0, 0, grid.Cols, grid.Rows));
            grid.Read();
        }

        public override void Dispose() { shotG.Dispose(); shot.Dispose(); }
    }

    class ImageSource : Source
    {
        readonly Settings s;
        readonly List<string> files = new List<string>();
        readonly Random rnd = new Random();
        readonly AnimSource fallback;
        int[] cur, nxt;
        float[] thresh;
        int index;
        double slideStart, transStart;
        const double TransitionSeconds = 1.6;

        public ImageSource(Settings settings)
        {
            s = settings;
            fallback = new AnimSource(0);
            try
            {
                string[] exts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                Queue<string> dirs = new Queue<string>();
                dirs.Enqueue(s.ImageFolder);
                int guard = 0;
                while (dirs.Count > 0 && files.Count < 3000 && guard++ < 400)
                {
                    string d = dirs.Dequeue();
                    try
                    {
                        foreach (string f in Directory.GetFiles(d))
                            if (Array.IndexOf(exts, Path.GetExtension(f).ToLowerInvariant()) >= 0) files.Add(f);
                        foreach (string sub in Directory.GetDirectories(d)) dirs.Enqueue(sub);
                    }
                    catch { }
                }
            }
            catch { }
            for (int i = files.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                string tmp = files[i]; files[i] = files[j]; files[j] = tmp;
            }
        }

        int[] LoadNext(Grid grid, double aspect)
        {
            for (int tries = 0; tries < 12 && files.Count > 0; tries++)
            {
                string path = files[index++ % files.Count];
                try
                {
                    using (Image img = Image.FromFile(path))
                    {
                        double ia = (double)img.Width / img.Height;
                        RectangleF src = new RectangleF(0, 0, img.Width, img.Height);
                        if (ia > aspect) { src.Width = (float)(img.Height * aspect); src.X = (img.Width - src.Width) / 2f; }
                        else { src.Height = (float)(img.Width / aspect); src.Y = (img.Height - src.Height) / 2f; }
                        grid.G.Clear(Color.Black);
                        grid.G.DrawImage(img, new RectangleF(0, 0, grid.Cols, grid.Rows), src, GraphicsUnit.Pixel);
                    }
                    grid.Read();
                    return (int[])grid.Px.Clone();
                }
                catch { }
            }
            return null;
        }

        public override void Fill(Grid grid, double t, double aspect)
        {
            if (cur == null || cur.Length != grid.Px.Length)
            {
                cur = LoadNext(grid, aspect);
                slideStart = t;
                if (cur == null) { fallback.Fill(grid, t, aspect); return; }
            }
            if (nxt == null && t - slideStart > s.SlideSeconds)
            {
                nxt = LoadNext(grid, aspect);
                transStart = t;
                if (nxt == null) slideStart = t;
                else if (thresh == null || thresh.Length != cur.Length)
                {
                    thresh = new float[cur.Length];
                    for (int i = 0; i < thresh.Length; i++) thresh[i] = (float)rnd.NextDouble();
                }
            }
            if (nxt != null)
            {
                double p = (t - transStart) / TransitionSeconds;
                if (p >= 1) { cur = nxt; nxt = null; slideStart = t; }
                else
                {
                    for (int i = 0; i < cur.Length; i++) grid.Px[i] = thresh[i] < p ? nxt[i] : cur[i];
                    return;
                }
            }
            Array.Copy(cur, grid.Px, cur.Length);
        }
    }

    class AnimSource : Source
    {
        readonly int mode;
        readonly Random rnd = new Random();
        float[] heat;
        OceanSource ocean;
        int lastMode;

        public AnimSource(int animation) { mode = animation; }

        public override bool NaturalLevels { get { return lastMode == 3; } }
        public override bool InlayTiles { get { return lastMode == 3; } }
        public override string PreferredRamp { get { return mode == 3 ? OceanSource.FishRamp : null; } }

        public static int Hsv(double h, double sat, double val)
        {
            h = h - Math.Floor(h);
            double r, g, b;
            int i = (int)(h * 6);
            double f = h * 6 - i, p = val * (1 - sat), q = val * (1 - f * sat), u = val * (1 - (1 - f) * sat);
            switch (i % 6)
            {
                case 0: r = val; g = u; b = p; break;
                case 1: r = q; g = val; b = p; break;
                case 2: r = p; g = val; b = u; break;
                case 3: r = p; g = q; b = val; break;
                case 4: r = u; g = p; b = val; break;
                default: r = val; g = p; b = q; break;
            }
            return Pack(r, g, b);
        }

        public static int Pack(double r, double g, double b)
        {
            int ri = (int)(Math.Max(0, Math.Min(1, r)) * 255);
            int gi = (int)(Math.Max(0, Math.Min(1, g)) * 255);
            int bi = (int)(Math.Max(0, Math.Min(1, b)) * 255);
            return (255 << 24) | (ri << 16) | (gi << 8) | bi;
        }

        public override void Fill(Grid grid, double t, double aspect)
        {
            int m = mode == 4 ? (int)(t / 25) % 4 : mode;
            lastMode = m;
            int cols = grid.Cols, rows = grid.Rows;
            int[] px = grid.Px;
            switch (m)
            {
                case 0:
                    for (int y = 0; y < rows; y++)
                        for (int x = 0; x < cols; x++)
                        {
                            double nx = (x / (double)cols - 0.5) * aspect, ny = y / (double)rows - 0.5;
                            double v = Math.Sin(nx * 6 + t) + Math.Sin(ny * 7 - t * 1.3)
                                     + Math.Sin((nx + ny) * 5 + t * 0.7)
                                     + Math.Sin(Math.Sqrt(nx * nx + ny * ny) * 10 - t * 1.1);
                            v = (v + 4) / 8;
                            double b = 0.5 + 0.5 * Math.Sin(v * Math.PI * 3 + t);
                            px[y * cols + x] = Hsv(v + t * 0.03, 0.85, 0.12 + 0.88 * b);
                        }
                    break;
                case 1:
                    for (int y = 0; y < rows; y++)
                        for (int x = 0; x < cols; x++)
                        {
                            double nx = (x / (double)cols - 0.5) * aspect, ny = y / (double)rows - 0.5;
                            double r = Math.Sqrt(nx * nx + ny * ny) + 1e-4, a = Math.Atan2(ny, nx);
                            double u = 0.25 / r + t * 0.6, v = a / Math.PI * 3 + t * 0.1;
                            double pat = Math.Sin(u * 6) * Math.Sin(v * Math.PI);
                            double lum = Math.Min(1, r * 2.5) * (0.5 + 0.5 * pat);
                            px[y * cols + x] = Hsv(u * 0.05, 0.8, lum);
                        }
                    break;
                case 2:
                    if (heat == null || heat.Length != cols * rows) heat = new float[cols * rows];
                    for (int x = 0; x < cols; x++)
                        heat[(rows - 1) * cols + x] = (float)rnd.NextDouble();
                    float decay = 1.15f / rows;
                    for (int y = 0; y < rows - 1; y++)
                        for (int x = 0; x < cols; x++)
                        {
                            int xl = x > 0 ? x - 1 : x, xr = x < cols - 1 ? x + 1 : x;
                            int below = (y + 1) * cols;
                            float h = heat[below + xl] + heat[below + x] + heat[below + xr];
                            h += y + 2 < rows ? heat[(y + 2) * cols + x] : heat[below + x];
                            h = h / 4f - decay * (0.4f + (float)rnd.NextDouble());
                            heat[y * cols + x] = h < 0 ? 0 : h;
                        }
                    for (int i = 0; i < heat.Length; i++)
                    {
                        double h3 = Math.Min(1, heat[i] * 1.6) * 3;
                        px[i] = Pack(Math.Min(1, h3), Math.Max(0, Math.Min(1, h3 - 1)), Math.Max(0, Math.Min(1, h3 - 2)));
                    }
                    break;
                default:
                    if (ocean == null) ocean = new OceanSource();
                    ocean.Fill(grid, t, aspect);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ ocean

    // Mouse state for the ocean. Only set in the windowed demo: as a real saver any mouse
    // movement exits, so there the ocean drives a virtual cursor instead.
    static class Pointer
    {
        public static bool Has;
        public static double Nx, Ny;   // 0..1 across the window
        public static int Clicks;
    }

    // Ocean tuning from the AsciiScreen settings window (defaults match the saver).
    static class OceanOptions
    {
        public static double Speed = 1.0;   // swim speed multiplier
        public static double Fish = 1.0;    // multiplies each school's size
        public static int Sharks = 4;       // 0..4 (big, hammerhead, medium, small)
        public static bool Extras = true;   // a manta ray and a jellyfish
        public static bool ClickRipples = true;   // a mouse click sends a ripple through the water
    }

    // A top-down ocean: sharks, a manta and six kinds of fish, from bait balls to
    // wandering puffers. Far things are smaller, fainter and slower.
    // Fish scatter from the cursor, sharks turn to investigate it, a click sends a ripple.
    class OceanSource : Source
    {
        class Shark
        {
            public double X, Y, Heading, Speed, Phase, Len, Z, Turn, GoalX, GoalY, WanderT, Bored, Ignore;
            public bool Hammer, Manta;
            public double WakeT, TrailLength, Drive = 1, DriveT, Beat = 1, HuntT, HuntWait;
            public int Prey = -1;
            public List<double> TX = new List<double>(), TY = new List<double>();
        }
        enum FishKind { Sardine, Angel, Needle, Puffer, Clown, Mackerel }
        class Species
        {
            public FishKind Kind;
            public int Count, Group;
            public double Len, Z, Speed, Space, Cohesion, Align, Flee;
            public double[] Tint;
        }
        class Fish { public double X, Y, VX, VY, AX, AY, Phase, Size, Panic, Heading, Turn, Gait = 1; }
        class Mote { public double X, Y, Z, Phase; }
        class Jelly { public double X, Y, VX, VY, Phase, R, Z; }
        class Wake { public double X, Y, Age, Life, Dm; public double[] Tint; }
        class Ripple { public double X, Y, T0; }
        class Layer { public bool IsShark, IsJelly; public int Index; public double Z; }
        static readonly double[] DarkTint = Tint(0.30, 0.35, 0.45);   // eyes and gill slits
        double[] rad = new double[512];
        double[] invRad2 = new double[512];
        static readonly double[] MantaTint = Tint(0.55, 0.70, 0.90);
        static readonly double[] JellyTint = Tint(1.0, 0.72, 0.92);
        Jelly[] jellies;
        readonly List<Wake> wakes = new List<Wake>();

        static double[] Tint(double r, double g, double b) { return new double[] { r, g, b, 0.299 * r + 0.587 * g + 0.114 * b }; }
        static readonly double[] WaterTint = Tint(0.15, 0.55, 1.0);
        static readonly double[] SharkTint = Tint(0.80, 0.88, 0.96);
        static readonly double[] HammerTint = Tint(0.96, 0.86, 0.72);
        static readonly double[] BellyTint = Tint(1.0, 0.96, 0.85);
        static readonly Species[] species =
        {
            new Species { Kind = FishKind.Sardine, Count = 20, Group = 20, Len = 6.5, Z = 0.64, Speed = 19, Space = 7.5, Cohesion = 0.65, Align = 2.0, Flee = 34, Tint = Tint(0.78, 0.92, 1.0) },
            new Species { Kind = FishKind.Angel, Count = 3, Group = 3, Len = 10, Z = 0.20, Speed = 7, Space = 16, Cohesion = 0.16, Align = 0.9, Flee = 23, Tint = Tint(0.80, 0.64, 1.0) },
            new Species { Kind = FishKind.Needle, Count = 4, Group = 4, Len = 16, Z = 0.38, Speed = 24, Space = 20, Cohesion = 0.18, Align = 1.5, Flee = 42, Tint = Tint(0.60, 0.93, 0.62) },
            new Species { Kind = FishKind.Puffer, Count = 3, Group = 1, Len = 8.5, Z = 0.10, Speed = 3.8, Space = 20, Cohesion = 0, Align = 0, Flee = 13, Tint = Tint(1.0, 0.83, 0.38) },
            new Species { Kind = FishKind.Clown, Count = 4, Group = 2, Len = 7.5, Z = 0.30, Speed = 9, Space = 11, Cohesion = 1.1, Align = 1.5, Flee = 19, Tint = Tint(1.0, 0.40, 0.12) },
            new Species { Kind = FishKind.Mackerel, Count = 9, Group = 9, Len = 11, Z = 0.50, Speed = 30, Space = 18, Cohesion = 0.38, Align = 2.4, Flee = 30, Tint = Tint(0.40, 0.78, 1.0) }
        };
        const double LX = -0.6, LY = -0.8;      // light direction for belly shading
        const double BodyWave = 5.65;           // radians of tail wave along the body
        public const string FishRamp = " .',:;-toxXS%#8@";   // the ocean's own letters

        readonly Random rnd = new Random(7);
        bool init, frozen;
        int cols, rows, lastClicks;
        double ca, WW, WH, S, fishScale, minR, simT, lastT, virtClickT;
        float[] cr, cg, cb;
        Shark[] sharks;
        Fish[][] schools;
        double[][] goals;
        double[] schoolZ;
        Species[] schoolSpecies;
        bool[] followManta;
        double[] schoolX, schoolY, schoolVX, schoolVY, schoolHeading, schoolFlash;
        Mote[] motes;
        readonly List<Ripple> ripples = new List<Ripple>();
        readonly List<Layer> layers = new List<Layer>();
        double[] bx = new double[512], by = new double[512], tnx = new double[512], tny = new double[512], cum = new double[2048];
        double[] fishBend = new double[33];

        public override bool NaturalLevels { get { return true; } }

        void Init(int c, int r, double aspect)
        {
            layers.Clear(); wakes.Clear(); ripples.Clear(); jellies = null;
            simT = 0; virtClickT = 0;
            cols = c; rows = r;
            ca = c / (r * aspect);          // cell height in cell-width units
            WW = c; WH = r * ca;            // world units: one unit = one cell width
            S = WW / 274.0;
            fishScale = 0.8 * (S <= 1.35 ? S : 1.35 + 0.5 * (Math.Sqrt(S - 0.35) - 1));
            minR = 0.5 * ca + 0.05;
            cr = new float[c * r]; cg = new float[c * r]; cb = new float[c * r];
            frozen = false;
            try { frozen = !SystemInformation.UIEffectsEnabled; } catch { }   // honour reduced motion

            double[] lens = { 0.27, 0.20, 0.19, 0.13 };
            double[] zs = { 0.0, 0.2, 0.35, 0.7 };
            int sharkCount = Math.Max(0, Math.Min(4, OceanOptions.Sharks));
            sharks = new Shark[sharkCount + (OceanOptions.Extras ? 1 : 0)];
            for (int i = 0; i < sharkCount; i++)
            {
                Shark s = new Shark();
                s.Hammer = i == 1;
                s.Z = zs[i];
                s.Len = lens[i] * WW * (1 - 0.3 * zs[i]);
                s.X = WW * (0.15 + 0.7 * rnd.NextDouble()); s.Y = WH * (0.15 + 0.7 * rnd.NextDouble());
                s.Heading = rnd.NextDouble() * 2 * Math.PI;
                s.Speed = s.Len * 0.2;
                s.Phase = rnd.NextDouble() * 2 * Math.PI;
                s.GoalX = WW * (0.1 + 0.8 * rnd.NextDouble()); s.GoalY = WH * (0.1 + 0.8 * rnd.NextDouble());
                s.WanderT = 3 + rnd.NextDouble() * 5;
                s.HuntWait = 2 + i * 2.7;
                InitTrail(s);
                sharks[i] = s;
                Layer l = new Layer(); l.IsShark = true; l.Index = i; l.Z = s.Z; layers.Add(l);
            }

            if (OceanOptions.Extras)
            {
                // a manta ray: wide wings, slow glide
                Shark m = new Shark();
                m.Manta = true;
                m.Z = 0.45;
                m.Len = 0.21 * WW * (1 - 0.3 * m.Z);
                m.X = WW * (0.15 + 0.7 * rnd.NextDouble()); m.Y = WH * (0.15 + 0.7 * rnd.NextDouble());
                m.Heading = rnd.NextDouble() * 2 * Math.PI;
                m.Speed = m.Len * 0.14;
                m.Phase = rnd.NextDouble() * 2 * Math.PI;
                m.GoalX = WW * (0.1 + 0.8 * rnd.NextDouble()); m.GoalY = WH * (0.1 + 0.8 * rnd.NextDouble());
                m.WanderT = 3 + rnd.NextDouble() * 5;
                InitTrail(m);
                sharks[sharkCount] = m;
                Layer ml = new Layer(); ml.IsShark = true; ml.Index = sharkCount; ml.Z = m.Z; layers.Add(ml);
            }

            int[] counts = new int[species.Length];
            int groups = 0;
            for (int i = 0; i < species.Length; i++)
            {
                Species sp = species[i];
                double count = sp.Count * Clamp(OceanOptions.Fish, 0.25, 2.0);
                counts[i] = sp.Kind == FishKind.Clown ? 2 * Math.Max(1, (int)Math.Round(count / 2)) : Math.Max(1, (int)Math.Round(count));
                groups += (counts[i] + sp.Group - 1) / sp.Group;
            }
            schools = new Fish[groups][]; goals = new double[groups][];
            schoolZ = new double[groups]; schoolSpecies = new Species[groups]; followManta = new bool[groups];
            schoolX = new double[groups]; schoolY = new double[groups]; schoolVX = new double[groups]; schoolVY = new double[groups];
            schoolHeading = new double[groups]; schoolFlash = new double[groups];
            int school = 0;
            for (int kind = 0; kind < species.Length; kind++)
            {
                Species sp = species[kind];
                int left = counts[kind], ng = (left + sp.Group - 1) / sp.Group;
                for (int g = 0; g < ng; g++, school++)
                {
                    int k = school, count = left / (ng - g); left -= count;
                    double z = sp.Z + (rnd.NextDouble() - 0.5) * 0.06;
                    double sx = WW * (0.12 + 0.76 * ((k * 0.618034 + rnd.NextDouble() * 0.2) % 1));
                    double sy = WH * (0.15 + 0.70 * ((k * 0.381966 + rnd.NextDouble() * 0.3) % 1));
                    double spread = sp.Space * fishScale * Math.Sqrt(count) * 0.6;
                    double heading = rnd.NextDouble() * 2 * Math.PI;
                    schoolHeading[k] = heading;
                    schools[k] = new Fish[count]; schoolZ[k] = z; schoolSpecies[k] = sp;
                    followManta[k] = sp.Kind == FishKind.Mackerel && g == 0;
                    for (int i = 0; i < count; i++)
                    {
                        Fish f = new Fish();
                        f.X = Clamp(sx + (rnd.NextDouble() - 0.5) * spread * 2, 4, WW - 4);
                        f.Y = Clamp(sy + (rnd.NextDouble() - 0.5) * spread, 4, WH - 4);
                        double a = heading + (rnd.NextDouble() - 0.5) * 0.7, speed = sp.Speed * S * (1 - 0.4 * z);
                        f.VX = Math.Cos(a) * speed; f.VY = Math.Sin(a) * speed;
                        f.Heading = a;
                        f.Phase = rnd.NextDouble() * 6.28;
                        f.Size = Math.Max(6, sp.Len * fishScale * (0.92 + rnd.NextDouble() * 0.16) * (1 - 0.3 * z));
                        schools[k][i] = f;
                    }
                    goals[k] = new double[] { WW * (0.1 + 0.8 * rnd.NextDouble()), WH * (0.1 + 0.8 * rnd.NextDouble()), 4 + rnd.NextDouble() * 6 };
                    Layer l = new Layer(); l.Index = k; l.Z = z; layers.Add(l);
                }
            }
            if (OceanOptions.Extras)
            {
                double[] jz = { 0.3 };
                jellies = new Jelly[jz.Length];
                for (int i = 0; i < jz.Length; i++)
                {
                    Jelly j = new Jelly();
                    j.Z = jz[i];
                    j.R = 0.027 * WW * (1 - 0.3 * j.Z);
                    j.X = WW * (0.2 + 0.6 * rnd.NextDouble()); j.Y = WH * (0.2 + 0.6 * rnd.NextDouble());
                    j.Phase = rnd.NextDouble() * 6.28;
                    jellies[i] = j;
                    Layer jl = new Layer(); jl.IsJelly = true; jl.Index = i; jl.Z = j.Z; layers.Add(jl);
                }
            }
            layers.Sort(delegate (Layer a, Layer b) { return b.Z.CompareTo(a.Z); });   // far first
            motes = new Mote[10];
            for (int i = 0; i < motes.Length; i++)
                motes[i] = new Mote { X = rnd.NextDouble() * WW, Y = rnd.NextDouble() * WH, Z = rnd.NextDouble(), Phase = rnd.NextDouble() * 6.28 };

            // let everything spread out before the first frame
            lastClicks = Pointer.Clicks;
            for (int i = 0; i < 160; i++) Step(0.05);
            ripples.Clear();
            virtClickT = simT;
        }

        public override void Fill(Grid grid, double t, double aspect)
        {
            if (!init || grid.Cols != cols || grid.Rows != rows)
            {
                Init(grid.Cols, grid.Rows, aspect);
                init = true;
                lastT = t;
            }
            double dt = Math.Min(0.1, Math.Max(0.0, t - lastT)) * OceanOptions.Speed;
            lastT = t;
            if (!frozen && dt > 0)
            {
                int sub = (int)Math.Ceiling(dt / 0.034);
                for (int i = 0; i < sub; i++) Step(dt / sub);
            }
            Render(grid);
        }

        // ------------------------------------------------------------ simulation

        static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        void InitTrail(Shark s)
        {
            double spacing = Math.Max(0.45, s.Len / 90);
            int count = (int)Math.Ceiling(s.Len * 1.6 / spacing);
            for (int i = count; i >= 0; i--)
            {
                s.TX.Add(s.X - Math.Cos(s.Heading) * i * spacing);
                s.TY.Add(s.Y - Math.Sin(s.Heading) * i * spacing);
            }
            s.TrailLength = count * spacing;
        }

        void UpdateTrail(Shark s)
        {
            int c = s.TX.Count;
            double ox = s.TX[c - 1] - s.TX[c - 2], oy = s.TY[c - 1] - s.TY[c - 2];
            double dx = s.X - s.TX[c - 2], dy = s.Y - s.TY[c - 2];
            double gap = Math.Sqrt(dx * dx + dy * dy);
            s.TrailLength += gap - Math.Sqrt(ox * ox + oy * oy);
            s.TX[c - 1] = s.X; s.TY[c - 1] = s.Y;
            // The final point moves; measure against the committed point so slow swimmers accumulate distance.
            if (gap >= Math.Max(0.45, s.Len / 90)) { s.TX.Add(s.X); s.TY.Add(s.Y); }
            while (s.TX.Count > 3 && s.TrailLength > s.Len * 1.6)
            {
                dx = s.TX[1] - s.TX[0]; dy = s.TY[1] - s.TY[0];
                s.TrailLength -= Math.Sqrt(dx * dx + dy * dy);
                s.TX.RemoveAt(0); s.TY.RemoveAt(0);
            }
        }

        void TrailPoint(Shark s, double distance, out double x, out double y)
        {
            for (int i = s.TX.Count - 1; i > 0; i--)
            {
                double dx = s.TX[i - 1] - s.TX[i], dy = s.TY[i - 1] - s.TY[i];
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 1e-6 && distance <= length)
                {
                    x = s.TX[i] + dx * distance / length; y = s.TY[i] + dy * distance / length;
                    return;
                }
                distance -= length;
            }
            x = s.TX[0]; y = s.TY[0];
        }

        void UpdateSchools(double dt)
        {
            for (int k = 0; k < schools.Length; k++)
            {
                double x = 0, y = 0, vx = 0, vy = 0;
                foreach (Fish f in schools[k]) { x += f.X; y += f.Y; vx += f.VX; vy += f.VY; }
                double inv = 1.0 / schools[k].Length;
                schoolX[k] = x * inv; schoolY[k] = y * inv; schoolVX[k] = vx * inv; schoolVY[k] = vy * inv;
                double heading = Math.Atan2(vy, vx);
                double turn = Math.Abs(Wrap(heading - schoolHeading[k])) / dt;
                schoolFlash[k] += (Math.Min(1, turn * 0.8) - schoolFlash[k]) * Math.Min(1, dt * 5);
                schoolHeading[k] = heading;
            }
        }

        int ChooseSchool(Shark s)
        {
            int best = -1;
            double score = double.MaxValue, hx = Math.Cos(s.Heading), hy = Math.Sin(s.Heading);
            for (int k = 0; k < schools.Length; k++)
            {
                if (schools[k].Length < 4) continue;
                FishKind kind = schoolSpecies[k].Kind;
                if (!s.Manta && kind != FishKind.Sardine && kind != FishKind.Mackerel) continue;
                double dx = schoolX[k] - s.X, dy = schoolY[k] - s.Y;
                double d = (dx * dx + dy * dy) * (1 + 3 * Math.Abs(s.Z - schoolZ[k]));
                if (dx * hx + dy * hy < 0) d *= 1.7;
                foreach (Shark o in sharks) if (o != s && o.Prey == k) d *= 3;   // spread the hunters over different schools
                if (d < score) { score = d; best = k; }
            }
            return best;
        }

        void AddRipple(double x, double y)
        {
            Ripple r = new Ripple(); r.X = x; r.Y = y; r.T0 = simT;
            ripples.Add(r);
            if (ripples.Count > 6) ripples.RemoveAt(0);
        }

        void Step(double dt)
        {
            simT += dt;
            double cx, cy; bool cur;
            if (Pointer.Has) { cx = Pointer.Nx * WW; cy = Pointer.Ny * WH; cur = true; }
            else
            {
                cx = WW * (0.5 + 0.46 * Math.Sin(simT * 0.17) * Math.Cos(simT * 0.053));
                cy = WH * (0.5 + 0.44 * Math.Sin(simT * 0.23 + 1.0));
                cur = Math.Sin(simT * 0.11) > 0.82;
            }
            if (Pointer.Has)
            {
                if (Pointer.Clicks != lastClicks) { lastClicks = Pointer.Clicks; if (OceanOptions.ClickRipples) AddRipple(cx, cy); }
            }
            else
            {
                lastClicks = Pointer.Clicks;
                if (simT - virtClickT > 11) { virtClickT = simT; if (OceanOptions.ClickRipples) AddRipple(cx, cy); }
            }
            for (int i = ripples.Count - 1; i >= 0; i--)
                if (simT - ripples[i].T0 > 4) ripples.RemoveAt(i);

            for (int i = wakes.Count - 1; i >= 0; i--)
            {
                wakes[i].Age += dt;
                if (wakes[i].Age > wakes[i].Life) wakes.RemoveAt(i);
            }

            foreach (Mote m in motes)
            {
                m.Phase += dt * 0.35;
                m.X += dt * S * (0.25 + 0.18 * Math.Sin(m.Phase)) * (1 - 0.5 * m.Z);
                m.Y -= dt * S * 0.22 * (1 - 0.5 * m.Z);
                if (m.X > WW) m.X -= WW;
                if (m.Y < 0) m.Y += WH;
            }
            UpdateSchools(dt);
            StepSharks(dt, cx, cy, cur);
            StepFish(dt, cx, cy, cur);
            StepJellies(dt, cx, cy, cur);
        }

        void StepSharks(double dt, double cx, double cy, bool cur)
        {
            // only the nearest willing shark goes to look at the cursor, so they do not all pile up on it
            Shark curious = null;
            double nearest = 0.4 * WW;
            if (cur)
                foreach (Shark o in sharks)
                {
                    if (o.Manta || o.Ignore > 0) continue;
                    double od = Math.Sqrt((cx - o.X) * (cx - o.X) + (cy - o.Y) * (cy - o.Y));
                    if (od < nearest) { nearest = od; curious = o; }
                }
            foreach (Shark s in sharks)
            {
                double dxC = cx - s.X, dyC = cy - s.Y, dC = Math.Sqrt(dxC * dxC + dyC * dyC);
                double gx, gy, speedMul = 1.0;
                if (s.Ignore > 0) s.Ignore -= dt;
                s.DriveT -= dt;
                if (s.DriveT <= 0)
                {
                    s.Drive = s.Manta ? 0.7 + rnd.NextDouble() * 0.35 : 0.60 + rnd.NextDouble() * 0.65;
                    s.DriveT = 2.5 + rnd.NextDouble() * 4.5;
                }
                if (s.HuntT > 0)
                {
                    s.HuntT -= dt;
                    if (s.HuntT <= 0) { s.Prey = -1; s.HuntWait = 7 + rnd.NextDouble() * 9; s.WanderT = 0; }
                }
                else s.HuntWait -= dt;
                bool investigating = cur && s == curious && (Pointer.Has || s.HuntT <= 0);
                if (!s.Manta && !investigating && s.HuntWait <= 0 && s.Prey < 0)
                {
                    s.Prey = ChooseSchool(s);
                    if (s.Prey >= 0) s.HuntT = 3.5 + rnd.NextDouble() * 1.5;
                    else s.HuntWait = 5;
                }
                if (investigating)
                {
                    if (dC < s.Len * 0.6)
                    {
                        // close enough: circle the cursor, then lose interest
                        double inv = 1.0 / Math.Max(dC, 1e-3);
                        gx = cx + (-dyC * inv) * s.Len * 0.9;
                        gy = cy + (dxC * inv) * s.Len * 0.9;
                        s.Bored += dt;
                        if (s.Bored > 5) { s.Ignore = 12; s.Bored = 0; }
                    }
                    else { gx = cx; gy = cy; s.Bored = Math.Max(0, s.Bored - dt); }
                    speedMul = 0.85;
                }
                else if (s.Prey >= 0 && s.HuntT > 0)
                {
                    if (s.HuntT > 1.2)
                    {
                        double dx = schoolX[s.Prey] - s.X, dy = schoolY[s.Prey] - s.Y;
                        double d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                        s.GoalX = schoolX[s.Prey] + schoolVX[s.Prey] * 0.6 + dx / d * s.Len * 0.45;
                        s.GoalY = schoolY[s.Prey] + schoolVY[s.Prey] * 0.6 + dy / d * s.Len * 0.45;
                    }
                    gx = s.GoalX; gy = s.GoalY;   // commit to the final part of the pass instead of circling the prey
                    speedMul = 1.75;
                }
                else
                {
                    s.WanderT -= dt;
                    double gdx = s.GoalX - s.X, gdy = s.GoalY - s.Y;
                    if (s.WanderT <= 0 || Math.Sqrt(gdx * gdx + gdy * gdy) < s.Len * 0.4)
                    {
                        s.GoalX = WW * (0.08 + 0.84 * rnd.NextDouble());
                        s.GoalY = WH * (0.1 + 0.8 * rnd.NextDouble());
                        s.WanderT = 7 + rnd.NextDouble() * 8;
                        if (s.Manta && rnd.NextDouble() < 0.65)
                        {
                            int k = ChooseSchool(s);
                            if (k >= 0) { s.GoalX = schoolX[k] + schoolVX[k]; s.GoalY = schoolY[k] + schoolVY[k]; }
                        }
                    }
                    gx = s.GoalX; gy = s.GoalY;
                }
                if (s.X < -0.3 * s.Len || s.X > WW + 0.3 * s.Len || s.Y < -0.3 * s.Len || s.Y > WH + 0.3 * s.Len)
                { gx = WW * 0.5; gy = WH * 0.5; }

                // Only neighbours at a similar depth need to turn aside.
                foreach (Shark o in sharks)
                {
                    if (o == s) continue;
                    double odx = s.X - o.X, ody = s.Y - o.Y, od = Math.Sqrt(odx * odx + ody * ody);
                    double oR = (s.Len + o.Len) * 0.55 * Math.Max(0, 1 - Math.Abs(s.Z - o.Z) * 1.5);
                    if (od < oR && od > 1e-3)
                    {
                        gx += odx / od * (oR - od) * 4;
                        gy += ody / od * (oR - od) * 4;
                    }
                }

                double des = Math.Atan2(gy - s.Y, gx - s.X);
                double diff = Wrap(des - s.Heading);
                double maxTurn = Clamp(0.85 - 0.45 * s.Len / (0.27 * WW), 0.4, 0.85);   // wide, shark-like arcs
                double turn = Clamp(diff * 1.2, -maxTurn, maxTurn);
                s.Turn += (turn - s.Turn) * Math.Min(1, dt * 3.5);
                s.Heading += s.Turn * dt;

                double drive = s.HuntT > 0 && !investigating ? 1 : s.Drive;
                double cruise = s.Len * (s.Manta ? 0.14 : 0.2) * speedMul * drive * (1 - 0.35 * s.Z) * (1 - 0.5 * Math.Min(1.0, Math.Abs(diff)));
                s.Speed += (cruise - s.Speed) * Math.Min(1.0, dt * (cruise > s.Speed ? 1.8 : 0.8));
                s.Beat += (Clamp(drive * speedMul, 0.25, 1.6) - s.Beat) * Math.Min(1, dt * 2);
                s.X += Math.Cos(s.Heading) * s.Speed * dt;
                s.Y += Math.Sin(s.Heading) * s.Speed * dt;
                s.Phase += dt * 2 * Math.PI * (0.22 + 0.32 * s.Beat + 0.9 * s.Speed / s.Len);
                UpdateTrail(s);

                // a faint wake of specks off the tail
                s.WakeT += dt;
                if (!s.Manta && s.WakeT > 0.09 && s.Speed > s.Len * 0.08 && wakes.Count < 500)
                {
                    s.WakeT = 0;
                    Wake wk = new Wake();
                    TrailPoint(s, s.Len * 0.82, out wk.X, out wk.Y);
                    wk.X += (rnd.NextDouble() - 0.5) * 1.2; wk.Y += (rnd.NextDouble() - 0.5) * 1.2;
                    wk.Life = 1.4 + rnd.NextDouble() * 0.8;
                    wk.Dm = 1 - 0.6 * s.Z;
                    wk.Tint = s.Hammer ? HammerTint : SharkTint;
                    wakes.Add(wk);
                }
            }
        }

        void StepFish(double dt, double cx, double cy, bool cur)
        {
            Shark manta = null;
            foreach (Shark s in sharks) if (s.Manta) manta = s;
            for (int k = 0; k < schools.Length; k++)
            {
                Fish[] fs = schools[k];
                Species spec = schoolSpecies[k];
                bool bait = spec.Kind == FishKind.Sardine;
                bool slow = spec.Kind == FishKind.Puffer || spec.Kind == FishKind.Angel;
                double mx = schoolX[k], my = schoolY[k], mvx = schoolVX[k], mvy = schoolVY[k];
                double repX = 0, repY = 0;   // schools keep out of each other's way
                for (int j = 0; j < schools.Length; j++)
                {
                    if (j == k) continue;
                    double rdx = mx - schoolX[j], rdy = my - schoolY[j], rd = Math.Sqrt(rdx * rdx + rdy * rdy), rr = 0.16 * WW;
                    if (rd < rr && rd > 1e-3) { repX += rdx / rd * (1 - rd / rr); repY += rdy / rd * (1 - rd / rr); }
                }

                double[] g = goals[k];
                double gdx = g[0] - mx, gdy = g[1] - my;
                if (simT > g[2] || gdx * gdx + gdy * gdy < 144 * S * S)
                {
                    g[0] = WW * (0.08 + 0.84 * rnd.NextDouble()); g[1] = WH * (0.1 + 0.8 * rnd.NextDouble());
                    g[2] = simT + 7 + rnd.NextDouble() * 10;
                }
                if (followManta[k] && manta != null)
                {
                    double hx = Math.Cos(manta.Heading), hy = Math.Sin(manta.Heading);
                    g[0] = manta.X - hx * manta.Len * 0.85 - hy * manta.Len * 0.18;
                    g[1] = manta.Y - hy * manta.Len * 0.85 + hx * manta.Len * 0.18;
                }
                gdx = g[0] - mx; gdy = g[1] - my;
                double gd = Math.Max(1e-3, Math.Sqrt(gdx * gdx + gdy * gdy));
                double ux = gdx / gd, uy = gdy / gd;
                double baseSpeed = spec.Speed * S * (1 - 0.4 * schoolZ[k]);
                double alarm = 0;
                if (bait)
                {
                    foreach (Shark s in sharks)
                    {
                        if (s.Manta) continue;
                        double dx = mx - s.X + Math.Cos(s.Heading) * s.Len * 0.35;
                        double dy = my - s.Y + Math.Sin(s.Heading) * s.Len * 0.35;
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        alarm = Math.Max(alarm, Clamp(1 - (d - s.Len * 0.35) / (spec.Flee * S * 1.6), 0, 1));
                    }
                }
                double scatter = Clamp((alarm - 0.68) / 0.32, 0, 1);
                double cohesion = spec.Cohesion * (1 + 3 * alarm) * (1 - 0.95 * scatter);
                double space = spec.Space * fishScale * (1 - 0.3 * schoolZ[k]);
                space *= 1 - 0.28 * alarm + 0.8 * scatter;

                for (int i = 0; i < fs.Length; i++)
                {
                    Fish f = fs[i];
                    double stroke = Math.Max(0, Math.Sin(simT * (slow ? 0.7 : 1.35) + k * 0.9 + i * 0.17));
                    stroke *= stroke; stroke *= stroke;
                    double gait = slow ? 0.8 + 0.3 * stroke : 0.65 + 0.75 * stroke;
                    f.Gait += (gait - f.Gait) * Math.Min(1, dt * 4);
                    double together = 1 - 0.85 * f.Panic;
                    double ax = (mx - f.X) * cohesion * together + (mvx - f.VX) * spec.Align * together;
                    double ay = (my - f.Y) * cohesion * together + (mvy - f.VY) * spec.Align * together;
                    ax += (ux * baseSpeed * f.Gait - f.VX) * 0.8 * (1 - 0.8 * alarm);
                    ay += (uy * baseSpeed * f.Gait - f.VY) * 0.8 * (1 - 0.8 * alarm);
                    ax += repX * 14 * S; ay += repY * 14 * S;
                    double wander = fs.Length == 1 ? 3.0 : 0.7;
                    ax += Math.Cos(f.Phase * 0.13 + i) * wander * S;
                    ay += Math.Sin(f.Phase * 0.11 + i * 1.7) * wander * S;
                    if (alarm > 0 && scatter < 1)
                    {
                        // A threatened bait ball contracts and mills; a close pass breaks it apart.
                        double dx = f.X - mx, dy = f.Y - my, d = Math.Max(3 * S, Math.Sqrt(dx * dx + dy * dy));
                        double swirl = alarm * (1 - scatter) * together * 1.8, side = k % 2 == 0 ? 1 : -1;
                        ax += (-dy / d * baseSpeed * side - f.VX) * swirl;
                        ay += (dx / d * baseSpeed * side - f.VY) * swirl;
                    }
                    double sepR = space * (1 + 0.5 * f.Panic), sepR2 = sepR * sepR;
                    // Schools stay bounded at 26 fish, even at the highest density.
                    foreach (Fish o in fs)
                    {
                        if (o == f) continue;
                        double dx = f.X - o.X, dy = f.Y - o.Y, d2 = dx * dx + dy * dy;
                        if (d2 < sepR2 && d2 > 1e-4)
                        {
                            double d = Math.Sqrt(d2), push = (1 - d / sepR) * 36 * S / d;
                            ax += dx * push; ay += dy * push;
                        }
                    }
                    foreach (Shark s in sharks)
                    {
                        double hx = Math.Cos(s.Heading), hy = Math.Sin(s.Heading);
                        double along = Clamp((s.X - f.X) * hx + (s.Y - f.Y) * hy, 0, s.Len * 0.75);
                        double dx = f.X - s.X + hx * along, dy = f.Y - s.Y + hy * along;
                        double d = Math.Sqrt(dx * dx + dy * dy), R = s.Len * 0.12 + spec.Flee * S;
                        if (s.Manta)
                        {
                            R = s.Len * 0.32;
                            double depth = Math.Max(0, 1 - Math.Abs(s.Z - schoolZ[k]) * 3);
                            if (d < R && d > 1e-3) { ax += dx / d * 18 * S * (1 - d / R) * depth; ay += dy / d * 18 * S * (1 - d / R) * depth; }
                            continue;
                        }
                        if (d < R && d > 1e-3)
                        {
                            double w = (1 - d / R) * (1 - 0.35 * Math.Abs(s.Z - schoolZ[k]));
                            double flee = (bait ? 150 : 85) * S * w;
                            ax += dx / d * flee; ay += dy / d * flee;
                            f.Panic = Math.Max(f.Panic, w);
                        }
                    }
                    if (cur)
                    {
                        double dx = f.X - cx, dy = f.Y - cy, d = Math.Sqrt(dx * dx + dy * dy);
                        double R = 52 * S;
                        if (d < R && d > 1e-3)
                        {
                            double w = 1 - d / R;
                            ax += dx / d * 260 * S * w; ay += dy / d * 260 * S * w;
                            f.Panic = Math.Max(f.Panic, 0.5 + 0.5 * w);
                        }
                    }
                    foreach (Ripple rp in ripples)
                    {
                        double age = simT - rp.T0, rad = age * 55 * S;
                        double dx = f.X - rp.X, dy = f.Y - rp.Y, d = Math.Sqrt(dx * dx + dy * dy);
                        if (age < 3 && Math.Abs(d - rad) < 7 * S && d > 1e-3)
                        {
                            ax += dx / d * 90 * S; ay += dy / d * 90 * S;
                            f.Panic = Math.Max(f.Panic, 0.6);
                        }
                    }
                    double margin = 10 * S + f.Size * 0.5;
                    ax += Clamp((margin - f.X) * 3, 0, 100 * S) - Clamp((f.X - (WW - margin)) * 3, 0, 100 * S);
                    ay += Clamp((margin - f.Y) * 3, 0, 100 * S) - Clamp((f.Y - (WH - margin)) * 3, 0, 100 * S);
                    f.AX = ax; f.AY = ay;
                }

                foreach (Fish f in fs)   // integrate together, so neighbour order does not steer the school
                {
                    f.VX += f.AX * dt; f.VY += f.AY * dt;
                    double sp = Math.Sqrt(f.VX * f.VX + f.VY * f.VY);
                    double maxS = baseSpeed * f.Gait * (1 + 1.6 * f.Panic), minS = baseSpeed * 0.32;
                    if (sp > maxS) { f.VX *= maxS / sp; f.VY *= maxS / sp; sp = maxS; }
                    else if (sp < minS && sp > 1e-4) { f.VX *= minS / sp; f.VY *= minS / sp; sp = minS; }
                    else if (sp <= 1e-4) { f.VX = ux * minS; f.VY = uy * minS; sp = minS; }
                    double rate = (slow ? 1.8 : 3.2) + 2.8 * f.Panic;
                    double turn = Clamp(Wrap(Math.Atan2(f.VY, f.VX) - f.Heading) / dt, -rate, rate);
                    f.Turn += (turn - f.Turn) * Math.Min(1, dt * 8);
                    f.Heading += turn * dt;
                    f.VX = Math.Cos(f.Heading) * sp; f.VY = Math.Sin(f.Heading) * sp;
                    f.Panic = Math.Max(0, f.Panic - 0.7 * dt);
                    f.X += f.VX * dt; f.Y += f.VY * dt;
                    f.Phase += dt * (1.7 + sp / f.Size * 4) * (0.55 + 0.45 * f.Gait);
                }
            }
        }

        // ------------------------------------------------------------ painting

        static double DepthTone(double tone, double z) { return 0.34 + (tone - 0.34) * (1 - 0.6 * z); }

        static double SkinShade(double across, double light, double ridge)
        {
            double a = Math.Min(1, Math.Abs(across)), flank = 4 * a * (1 - a), rim = a * a;
            return 0.58 + 0.30 * flank + 0.12 * rim * rim + 0.12 * light * a - ridge * 0.72 * Math.Max(0, 1 - a * 4);
        }

        void Blend(int i, double L, double[] tint, double cover)
        {
            double k = L / tint[3];
            cr[i] = (float)(cr[i] * (1 - cover) + Math.Min(1.0, tint[0] * k) * cover);
            cg[i] = (float)(cg[i] * (1 - cover) + Math.Min(1.0, tint[1] * k) * cover);
            cb[i] = (float)(cb[i] * (1 - cover) + Math.Min(1.0, tint[2] * k) * cover);
        }

        void Disc(double x, double y, double r, double L, double[] tint, bool shade)
        {
            int c0 = Math.Max(0, (int)Math.Floor(x - r - 1)), c1 = Math.Min(cols - 1, (int)Math.Ceiling(x + r + 1));
            int r0 = Math.Max(0, (int)Math.Floor((y - r) / ca - 1)), r1 = Math.Min(rows - 1, (int)Math.Ceiling((y + r) / ca + 1));
            double rr = Math.Max(r, 0.3);
            for (int row = r0; row <= r1; row++)
            {
                double dy = (row + 0.5) * ca - y;
                for (int col = c0; col <= c1; col++)
                {
                    double dx = col + 0.5 - x;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    double cov = rr - d + 0.5;
                    if (cov <= 0) continue;
                    if (cov > 1) cov = 1;
                    double l = L;
                    if (shade)
                    {
                        double q = Clamp(1 - d / rr, 0, 1);
                        double lit = -(dx * LX + dy * LY) / rr;
                        l = L * (0.62 + 0.28 * q + 0.14 * lit);
                    }
                    Blend(row * cols + col, Clamp(l, 0, 1), tint, cov);
                }
            }
        }

        void Seg(double x0, double y0, double x1, double y1, double ra, double rb, double L, double[] tint)
        {
            double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
            int n = Math.Max(2, (int)(len / 0.9));
            double lo = minR * 0.8;
            for (int i = 0; i <= n; i++)
            {
                double f = i / (double)n;
                Disc(x0 + dx * f, y0 + dy * f, Math.Max(lo, ra + (rb - ra) * f), L, tint, false);
            }
        }

        void Fin(double ax, double ay, double bx, double by, double cx, double cy, double la, double lb, double lc, double[] tint)
        {
            double ux = bx - ax, uy = by - ay, vx = cx - ax, vy = cy - ay;
            double area = ux * vy - uy * vx;
            if (Math.Abs(area) < 1e-6) return;
            double inv = 1 / area, magnitude = Math.Abs(area);
            double ha = magnitude / Math.Max(1e-3, Math.Sqrt((bx - cx) * (bx - cx) + (by - cy) * (by - cy)));
            double hb = magnitude / Math.Max(1e-3, Math.Sqrt(vx * vx + vy * vy));
            double hc = magnitude / Math.Max(1e-3, Math.Sqrt(ux * ux + uy * uy));
            int c0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)) - 1));
            int c1 = Math.Min(cols - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx)) + 1));
            int r0 = Math.Max(0, (int)Math.Floor((Math.Min(ay, Math.Min(by, cy)) - 1) / ca));
            int r1 = Math.Min(rows - 1, (int)Math.Ceiling((Math.Max(ay, Math.Max(by, cy)) + 1) / ca));
            for (int row = r0; row <= r1; row++)
            {
                double px = c0 + 0.5 - ax, py = (row + 0.5) * ca - ay;
                double u = (px * vy - py * vx) * inv, v = (ux * py - uy * px) * inv;
                for (int col = c0; col <= c1; col++, u += vy * inv, v -= uy * inv)
                {
                    double w = 1 - u - v, edge = Math.Min(w * ha, Math.Min(u * hb, v * hc));
                    double cover = Clamp(edge + 0.65, 0, 1);
                    if (cover <= 0) continue;
                    double L = la * w + lb * u + lc * v;
                    L += 0.06 * Clamp(1 - edge, 0, 1);
                    Blend(row * cols + col, Clamp(L, 0.355, 1), tint, cover);
                }
            }
        }

        static double Profile(double s)
        {
            if (s < 0.28) return Math.Pow(Math.Sin(s / 0.28 * Math.PI / 2), 0.6);
            double q = (s - 0.28) / 0.72;
            return Math.Max(0.1, 1 - 0.9 * Math.Pow(q, 1.3));
        }

        // Samples the body spine along the trail behind the head and adds the tail-beat wave.
        void Spine(Shark s, int n, double lb)
        {
            int cnt = s.TX.Count;
            if (cnt > cum.Length) cnt = cum.Length;
            cum[0] = 0;
            for (int k = 1; k < cnt; k++)
            {
                double ax = s.TX[s.TX.Count - k] - s.TX[s.TX.Count - 1 - k], ay = s.TY[s.TX.Count - k] - s.TY[s.TX.Count - 1 - k];
                cum[k] = cum[k - 1] + Math.Sqrt(ax * ax + ay * ay);
            }
            int idx = 1;
            for (int i = 0; i < n; i++)
            {
                double want = lb * i / (n - 1);
                while (idx < cnt - 1 && cum[idx] < want) idx++;
                if (cnt < 2 || cum[idx] < want)
                {
                    bx[i] = s.TX[s.TX.Count - cnt]; by[i] = s.TY[s.TX.Count - cnt];
                }
                else
                {
                    double c0 = cum[idx - 1], c1 = cum[idx], f = (want - c0) / Math.Max(c1 - c0, 1e-9);
                    double px0 = s.TX[s.TX.Count - idx], py0 = s.TY[s.TX.Count - idx];
                    double px1 = s.TX[s.TX.Count - 1 - idx], py1 = s.TY[s.TX.Count - 1 - idx];
                    bx[i] = px0 + (px1 - px0) * f; by[i] = py0 + (py1 - py0) * f;
                }
            }
            SpineTangents(n, s.Heading);
            double amp = (s.Manta ? 0.012 : 0.065) * s.Len * (0.35 + 0.65 * s.Beat) * (1 + 0.3 * Math.Min(1, Math.Abs(s.Turn)));
            for (int i = 0; i < n; i++)
            {
                double sv = i / (double)(n - 1);
                double off = amp * (0.035 + 0.965 * sv * sv) * Math.Sin(s.Phase - BodyWave * sv);
                bx[i] += -tny[i] * off; by[i] += tnx[i] * off;
            }
            SpineTangents(n, s.Heading);
        }

        void SpineTangents(int n, double heading)
        {
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(i - 1, 0), b = Math.Min(i + 1, n - 1);
                double tx = bx[a] - bx[b], ty = by[a] - by[b], tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-9) { tx = Math.Cos(heading); ty = Math.Sin(heading); tl = 1; }
                tnx[i] = tx / tl; tny[i] = ty / tl;
            }
        }

        // Fills a body along the spine (bx/by, radius rad[i]) shading each cell by where it sits across the body:
        // a darker ridge down the middle (the back), lighter flanks, and light from the upper left.
        void BodyFill(int n, double L, double[] tint, double ridge, double bank)
        {
            double minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9;
            for (int i = 0; i < n; i++)
            {
                invRad2[i] = 1 / (rad[i] * rad[i]);
                if (bx[i] - rad[i] < minX) minX = bx[i] - rad[i];
                if (bx[i] + rad[i] > maxX) maxX = bx[i] + rad[i];
                if (by[i] - rad[i] < minY) minY = by[i] - rad[i];
                if (by[i] + rad[i] > maxY) maxY = by[i] + rad[i];
            }
            int c0 = Math.Max(0, (int)Math.Floor(minX - 1)), c1 = Math.Min(cols - 1, (int)Math.Ceiling(maxX + 1));
            int r0 = Math.Max(0, (int)Math.Floor(minY / ca - 1)), r1 = Math.Min(rows - 1, (int)Math.Ceiling(maxY / ca + 1));
            for (int row = r0; row <= r1; row++)
            {
                double cy = (row + 0.5) * ca;
                for (int col = c0; col <= c1; col++)
                {
                    double cx = col + 0.5, best = 1e9, bdx = 0, bdy = 0;
                    int bi = -1;
                    for (int i = 0; i < n; i++)
                    {
                        double dx = cx - bx[i], dy = cy - by[i], u2 = (dx * dx + dy * dy) * invRad2[i];
                        if (u2 < best) { best = u2; bi = i; bdx = dx; bdy = dy; }
                    }
                    if (bi < 0 || best > 1.8) continue;
                    double r = rad[bi], u = Math.Sqrt(best), dist = u * r;
                    double cov = r - dist + 0.5;
                    if (cov <= 0) continue;
                    if (cov > 1) cov = 1;
                    double across = (bdx * -tny[bi] + bdy * tnx[bi]) / r + bank * 0.10;
                    double lit = (-(-tny[bi] * LX + tnx[bi] * LY) + bank * 0.35) * Math.Sign(across);
                    double l = L * SkinShade(across, Clamp(lit, -1, 1), ridge);
                    Blend(row * cols + col, Clamp(l, 0.355, 1), tint, cov);
                }
            }
        }

        void DrawShark(Shark s)
        {
            double len = s.Len, lb = len * 0.82;
            int n = Math.Max(20, Math.Min(96, (int)(lb / 2.2)));
            Spine(s, n, lb);
            double[] tint = s.Hammer ? HammerTint : SharkTint;
            double bank = Clamp(s.Turn * 0.8, -0.6, 0.6);
            double wmax = (s.Hammer ? 0.065 : 0.095) * len * (1 - 0.12 * Math.Abs(bank));
            double root = DepthTone(0.45, s.Z), rim = DepthTone(0.88, s.Z), mid = DepthTone(0.63, s.Z);

            // pectoral and pelvic fins
            int ip = (int)(0.30 * (n - 1)), iq = (int)(0.62 * (n - 1));
            double flap = 1 + 0.10 * Math.Sin(s.Phase * 0.65);
            for (int side = -1; side <= 1; side += 2)
            {
                double rx = bx[ip], ry = by[ip], tx = tnx[ip], ty = tny[ip];
                double px = -ty * side, py = tx * side;
                double reach = 0.19 * len * flap * (1 - bank * side * 0.4);
                double fx = rx + px * reach - tx * 0.15 * len, fy = ry + py * reach - ty * 0.15 * len;
                Fin(rx + tx * 0.055 * len + px * 0.025 * len, ry + ty * 0.055 * len + py * 0.025 * len,
                    rx - tx * 0.15 * len + px * 0.025 * len, ry - ty * 0.15 * len + py * 0.025 * len,
                    fx, fy, mid, root, rim + bank * side * 0.06, tint);
                double qx = bx[iq], qy = by[iq], qtx = tnx[iq], qty = tny[iq];
                Fin(qx, qy, qx - qtx * 0.09 * len, qy - qty * 0.09 * len,
                    qx - qty * side * 0.085 * len - qtx * 0.07 * len, qy + qtx * side * 0.085 * len - qty * 0.07 * len,
                    mid, root, rim, tint);
            }

            // tail fin: two lobes that swing with the beat
            double ex = bx[n - 1], ey = by[n - 1];
            double sw = 0.30 * Math.Sin(s.Phase - BodyWave - 0.9);
            double cs = Math.Cos(sw), sn = Math.Sin(sw);
            double bkx = -tnx[n - 1], bky = -tny[n - 1];
            double rkx = bkx * cs - bky * sn, rky = bkx * sn + bky * cs;
            double ppx = -rky, ppy = rkx;
            double notchX = ex + rkx * 0.075 * len, notchY = ey + rky * 0.075 * len;
            Fin(ex, ey, notchX, notchY, ex + rkx * 0.19 * len + ppx * 0.18 * len, ey + rky * 0.19 * len + ppy * 0.18 * len,
                root, mid, rim, tint);
            Fin(ex, ey, notchX, notchY, ex + rkx * 0.14 * len - ppx * 0.13 * len, ey + rky * 0.14 * len - ppy * 0.13 * len,
                root, mid, rim, tint);

            // body: shaded by lateral position (dark back, light flanks, lit from the upper left)
            for (int i = 0; i < n; i++) rad[i] = Math.Max(minR * 0.9, wmax * Profile(i / (double)(n - 1)));
            BodyFill(n, DepthTone(0.98, s.Z), tint, 0.28, bank);

            if (!s.Hammer)
            {
                // eyes and gill slits show as dark gaps in the tiles
                int ie = (int)(0.075 * (n - 1));
                for (int side = -1; side <= 1; side += 2)
                {
                    Disc(bx[ie] - tny[ie] * side * rad[ie] * 0.62, by[ie] + tnx[ie] * side * rad[ie] * 0.62, Math.Max(minR * 0.5, 0.45), 0.10, DarkTint, false);
                    for (int g = 0; g < 3; g++)
                    {
                        int ig = (int)((0.19 + 0.035 * g) * (n - 1));
                        Disc(bx[ig] - tny[ig] * side * rad[ig] * 0.78, by[ig] + tnx[ig] * side * rad[ig] * 0.78, Math.Max(minR * 0.45, 0.4), 0.12, DarkTint, false);
                    }
                }
            }
            else
            {
                // hammerhead cephalofoil, with an eye at each end
                double hx = bx[0] + tnx[0] * 0.01 * len, hy = by[0] + tny[0] * 0.01 * len;
                double px = -tny[0] * 0.11 * len, py = tnx[0] * 0.11 * len;
                Seg(hx - px, hy - py, hx + px, hy + py, 0.02 * len, 0.02 * len, DepthTone(0.80, s.Z), tint);
                Disc(hx - px * 0.92, hy - py * 0.92, Math.Max(minR * 0.5, 0.45), 0.10, DarkTint, false);
                Disc(hx + px * 0.92, hy + py * 0.92, Math.Max(minR * 0.5, 0.45), 0.10, DarkTint, false);
            }
        }

        void StepJellies(double dt, double cx, double cy, bool cur)
        {
            if (jellies == null) return;
            foreach (Jelly j in jellies)
            {
                j.Phase += dt * 2.0;
                double pulse = Math.Max(0.0, Math.Sin(j.Phase));            // each contraction pushes it along
                double ang = j.Phase * 0.07 + j.Z * 5;
                double ax = Math.Cos(ang) * pulse * 22 * S + (WW * 0.5 - j.X) * 0.02;
                double ay = Math.Sin(ang) * pulse * 22 * S + (WH * 0.5 - j.Y) * 0.02;
                if (cur)
                {
                    double dx = j.X - cx, dy = j.Y - cy, d = Math.Sqrt(dx * dx + dy * dy), R = 42 * S;
                    if (d < R && d > 1e-3) { ax += dx / d * 60 * S * (1 - d / R); ay += dy / d * 60 * S * (1 - d / R); }
                }
                j.VX = (j.VX + ax * dt) * (1 - 1.1 * dt);
                j.VY = (j.VY + ay * dt) * (1 - 1.1 * dt);
                j.X += j.VX * dt; j.Y += j.VY * dt;
            }
        }

        void DrawManta(Shark s)
        {
            double len = s.Len, lb = len * 0.5;
            int n = Math.Max(16, Math.Min(64, (int)(lb / 2)));
            Spine(s, n, lb);
            double[] tint = MantaTint;
            double flap = Math.Sin(s.Phase * 0.65), bank = Clamp(s.Turn * 0.9, -0.6, 0.6);

            int ir = (int)(0.42 * (n - 1));
            double rx = bx[ir], ry = by[ir], tx = tnx[ir], ty = tny[ir];
            for (int side = -1; side <= 1; side += 2)
            {
                double px = -ty * side, py = tx * side;
                double reach = len * (0.48 + 0.065 * flap) * (1 - 0.23 * bank * side);
                double sweep = (0.12 + 0.07 * flap) * len;
                double tipX = rx + px * reach - tx * sweep, tipY = ry + py * reach - ty * sweep;
                double leadX = rx + px * len * 0.25 + tx * len * 0.06, leadY = ry + py * len * 0.25 + ty * len * 0.06;
                double trail = 0.25 + 0.04 * Math.Sin(s.Phase * 0.65 - 0.8);
                double trailX = rx + px * len * 0.23 - tx * len * trail, trailY = ry + py * len * 0.23 - ty * len * trail;
                double root = DepthTone(0.43, s.Z), lead = DepthTone(0.91 + bank * side * 0.08, s.Z);
                double tip = DepthTone(0.80 + flap * 0.06, s.Z), rear = DepthTone(0.50 - bank * side * 0.05, s.Z);
                Fin(bx[n - 2], by[n - 2], bx[2], by[2], leadX, leadY, root, DepthTone(0.66, s.Z), lead, tint);
                Fin(bx[n - 2], by[n - 2], leadX, leadY, tipX, tipY, root, lead, tip, tint);
                Fin(bx[n - 2], by[n - 2], tipX, tipY, trailX, trailY, root, tip, rear, tint);
            }

            // whip tail
            double ex = bx[n - 1], ey = by[n - 1], bkx = -tnx[n - 1], bky = -tny[n - 1];
            double wob = 0.12 * Math.Sin(s.Phase - 2.0);
            Seg(ex, ey, ex + (bkx - bky * wob) * 0.55 * len, ey + (bky + bkx * wob) * 0.55 * len, 0.012 * len, 0.004 * len, DepthTone(0.58, s.Z), tint);

            // body, shaded like the sharks
            for (int i = 0; i < n; i++)
            {
                double sv = i / (double)(n - 1);
                rad[i] = Math.Max(minR * 0.9, 0.17 * len * Math.Pow(Math.Sin(Math.PI * Math.Min(1.0, 0.1 + sv * 0.9)), 0.8));
            }
            BodyFill(n, DepthTone(0.96, s.Z), tint, 0.25, bank);

            // cephalic fins
            for (int side = -1; side <= 1; side += 2)
                Seg(bx[0], by[0], bx[0] + tnx[0] * 0.07 * len - tny[0] * side * 0.05 * len, by[0] + tny[0] * 0.07 * len + tnx[0] * side * 0.05 * len,
                    0.016 * len, 0.006 * len, DepthTone(0.76, s.Z), tint);
        }

        void DrawJelly(Jelly j)
        {
            double dm = 1 - 0.6 * j.Z;
            double r = j.R * (0.88 + 0.12 * Math.Sin(j.Phase));
            for (int k = 0; k < 8; k++)   // trailing tentacles
            {
                double a = k / 8.0 * 2 * Math.PI + j.Phase * 0.1;
                double wig = Math.Sin(j.Phase * 1.3 + k * 1.7) * 0.9;
                double ux = Math.Cos(a), uy = Math.Sin(a);
                Seg(j.X + ux * r * 1.05, j.Y + uy * r * 1.05, j.X + ux * r * 1.8 - uy * wig, j.Y + uy * r * 1.8 + ux * wig,
                    minR * 0.7, minR * 0.5, 0.36 * dm, JellyTint);
            }
            Disc(j.X, j.Y, r * 1.25, 0.26 * dm, JellyTint, false);   // soft halo
            Disc(j.X, j.Y, r, 0.82 * dm, JellyTint, true);           // bell with a lit rim
            Disc(j.X, j.Y, r * 0.55, 0.40 * dm, JellyTint, false);   // darker core, like a dome seen from above
        }

        static double FinProfile(double u, double start, double peak, double end)
        {
            if (u <= start || u >= end) return 0;
            return u < peak ? (u - start) / (peak - start) : (end - u) / (end - peak);
        }

        static double FishWidth(FishKind kind, double u)
        {
            double q;
            switch (kind)
            {
                case FishKind.Angel: q = (u - 0.36) / 0.35; return 0.27 * Math.Sqrt(Math.Max(0, 1 - q * q));
                case FishKind.Puffer: q = (u - 0.38) / 0.35; return 0.33 * Math.Sqrt(Math.Max(0, 1 - q * q));
                case FishKind.Clown: q = (u - 0.37) / 0.36; return 0.21 * Math.Sqrt(Math.Max(0, 1 - q * q)) * (1 - 0.4 * u);
                case FishKind.Needle:
                    if (u < 0 || u > 0.86) return 0;
                    return u < 0.18 ? 0.022 : 0.025 + 0.032 * FinProfile(u, 0.12, 0.34, 0.86);
                default:
                    if (u < 0 || u > 0.84) return 0;
                    q = u < 0.26 ? (u - 0.26) / 0.26 : (u - 0.26) / 0.58;
                    return Math.Max(0.022, (kind == FishKind.Mackerel ? 0.14 : 0.105) * Math.Sqrt(Math.Max(0, 1 - q * q)));
            }
        }

        double FishSpine(Fish f, FishKind kind, double speed)
        {
            // Each species swims its own way: sardines and mackerel whip the tail, needlefish stay stiff and only flick the tip,
            // angelfish sway slowly, pufferfish scull with an almost still body, clownfish wobble.
            double amp, reach, waves, extent = 0;
            switch (kind)
            {
                case FishKind.Puffer: amp = 0.03; reach = 1.4; waves = 0.6; break;
                case FishKind.Angel: amp = 0.07; reach = 1.6; waves = 0.7; break;
                case FishKind.Needle: amp = 0.05; reach = 3.2; waves = 0.9; break;
                case FishKind.Clown: amp = 0.12; reach = 1.8; waves = 1.0; break;
                default: amp = 0.11; reach = 2.2; waves = 1.0; break;
            }
            amp *= 0.35 + 0.65 * f.Gait + 0.35 * f.Panic;
            double curve = Clamp(f.Turn * f.Size / Math.Max(speed, 1), -0.65, 0.65) * 0.4;
            for (int i = 0; i < fishBend.Length; i++)
            {
                double u = i / (double)(fishBend.Length - 1);
                fishBend[i] = curve * u * u + amp * (0.02 + 0.98 * Math.Pow(u, reach)) * Math.Sin(f.Phase - BodyWave * waves * u);
                extent = Math.Max(extent, Math.Abs(fishBend[i]));
            }
            return extent;
        }

        double FishCover(FishKind kind, double u, double av, double len, double phase, double gait, double bank, out double body, out double width)
        {
            double flutter = Math.Sin(phase * 0.65), spread = 1 + (0.10 + 0.20 * gait) * Math.Cos(phase);   // the tail fans open and closes with each beat
            width = FishWidth(kind, u) * (1 - 0.16 * Math.Abs(bank));
            body = width > 0 ? Math.Min(Math.Max(minR * 1.08, width * len) - av * len + 0.65, u * len + 0.65) : 0;
            double fin;
            switch (kind)
            {
                case FishKind.Angel: fin = 0.53 * FinProfile(u, 0.12, 0.45, 0.86); break;
                case FishKind.Puffer: fin = 0.41 * FinProfile(u, 0.29, 0.45, 0.60) * (1 + 0.25 * Math.Sin(phase * 2.2)); break;   // sculling fins
                case FishKind.Clown: fin = 0.25 * FinProfile(u, 0.18, 0.38, 0.69); break;
                case FishKind.Needle: fin = 0.14 * FinProfile(u, 0.56, 0.70, 0.82); break;
                case FishKind.Mackerel: fin = 0.25 * FinProfile(u, 0.23, 0.40, 0.59); break;
                default: fin = 0.14 * FinProfile(u, 0.28, 0.40, 0.59); break;
            }
            double cover = fin > 0 ? (fin * (1 + flutter * 0.1) - av) * len + 0.5 : 0;
            if (u >= 0.74 && u <= 1.04)
            {
                double tail = (u - 0.74) / 0.30, outer, inner = 0;
                if (kind == FishKind.Puffer || kind == FishKind.Clown)
                {
                    double q = (u - 0.90) / 0.15;
                    outer = (kind == FishKind.Puffer ? 0.13 : 0.20) * Math.Sqrt(Math.Max(0, 1 - q * q));
                }
                else if (kind == FishKind.Angel) outer = 0.025 + tail * 0.15;
                else
                {
                    outer = 0.025 + tail * (kind == FishKind.Mackerel ? 0.23 : (kind == FishKind.Needle ? 0.14 : 0.18));
                    inner = Math.Max(0, tail - 0.32) * (kind == FishKind.Mackerel ? 0.30 : 0.22);
                }
                outer *= spread;
                double edge = Math.Min((outer - av) * len + 0.55, (1.04 - u) * len + 0.55);
                if (inner > 0) edge = Math.Min(edge, (av - inner) * len + 0.5);
                cover = Math.Max(cover, edge);
            }
            if (kind == FishKind.Angel && u > 0.45 && u < 1.08)
            {
                double trail = 0.51 - (u - 0.45) * 0.50 + flutter * 0.025 + 0.018 * Math.Sin(u * 10 - phase * 1.4);   // a ripple runs down the trailing fins
                cover = Math.Max(cover, 0.65 - Math.Abs(av - trail) * len);
            }
            return Math.Max(cover, body);
        }

        static void FishPattern(FishKind kind, double u, double across, double flank, double z, ref double L, ref double[] tint)
        {
            double av = Math.Abs(across);
            switch (kind)
            {
                case FishKind.Sardine:
                    if (flank > 0.4 && flank < 0.85) L += 0.09;
                    break;
                case FishKind.Angel:
                    if (Math.Abs(u - 0.20) < 0.04 || Math.Abs(u - 0.40) < 0.045 || Math.Abs(u - 0.60) < 0.04) L *= 0.64;
                    break;
                case FishKind.Needle:
                    L += av < 0.022 ? 0.10 : -0.04;
                    break;
                case FishKind.Puffer:
                    double spotX = u * 7, spotY = (across + 0.45) * 9 + Math.Floor(spotX) * 0.5;
                    spotX = spotX - Math.Floor(spotX) - 0.5; spotY = spotY - Math.Floor(spotY) - 0.5;
                    if (spotX * spotX + spotY * spotY < 0.085) L *= 0.60;
                    else if (flank > 0.6) L += 0.07;
                    break;
                case FishKind.Clown:
                    L *= 0.91;
                    if (Math.Abs(u - 0.19) < 0.05 || Math.Abs(u - 0.43) < 0.055 || Math.Abs(u - 0.67) < 0.04)
                    { L = DepthTone(0.90, z); tint = BellyTint; }
                    break;
                case FishKind.Mackerel:
                    double stripe = u * 9 + av * 6;
                    if (stripe - Math.Floor(stripe) < 0.28 && flank < 0.9) L *= 0.70;
                    else if (flank > 0.65) L += 0.08;
                    break;
            }
        }

        void DrawFish(Fish f, Species spec, double z, double flash)
        {
            FishKind kind = spec.Kind;
            double sp = Math.Sqrt(f.VX * f.VX + f.VY * f.VY);
            double dx = Math.Cos(f.Heading), dy = Math.Sin(f.Heading), bank = Clamp(f.Turn * 0.35, -0.7, 0.7);
            double len = f.Size, inv = 1 / len;
            double span = kind == FishKind.Angel ? 0.58 : (kind == FishKind.Puffer ? 0.44 : 0.29);
            span += FishSpine(f, kind, sp);
            double mx = f.X - dx * len * 0.55, my = f.Y - dy * len * 0.55;
            double ex = (Math.Abs(dx) * 0.6 + Math.Abs(dy) * span) * len + 1;
            double ey = (Math.Abs(dy) * 0.6 + Math.Abs(dx) * span) * len + 1;
            int c0 = Math.Max(0, (int)Math.Floor(mx - ex)), c1 = Math.Min(cols - 1, (int)Math.Ceiling(mx + ex));
            int r0 = Math.Max(0, (int)Math.Floor((my - ey) / ca)), r1 = Math.Min(rows - 1, (int)Math.Ceiling((my + ey) / ca));
            double eyeU = kind == FishKind.Needle ? 0.22 : 0.13;
            double eyeV = FishWidth(kind, eyeU) * 0.55, eyeR = Math.Max(0.42, len * 0.028);
            double light = -(-dy * LX + dx * LY) + bank * 0.45, peak = DepthTone(0.98, z);
            if (kind != FishKind.Sardine && kind != FishKind.Mackerel && kind != FishKind.Needle) flash *= 0.25;
            for (int row = r0; row <= r1; row++)
            {
                double ry = (row + 0.5) * ca - f.Y, rx = c0 + 0.5 - f.X;
                double u = -(rx * dx + ry * dy) * inv, v = (-rx * dy + ry * dx) * inv;
                for (int col = c0; col <= c1; col++, u -= dx * inv, v -= dy * inv)
                {
                    if (u < 0 || u > 1.09) continue;
                    double sample = Math.Min(31.999999, u * 32);
                    int index = (int)sample;
                    double bend = fishBend[index] + (fishBend[index + 1] - fishBend[index]) * (sample - index);
                    double across = v - bend, av = Math.Abs(across), body, width;
                    double cover = FishCover(kind, u, av, len, f.Phase, f.Gait, bank, out body, out width);
                    if (cover <= 0) continue;
                    double L = DepthTone(0.60 + 0.20 * Math.Min(1, av / 0.20), z);
                    double[] tint = spec.Tint;
                    if (body > 0)
                    {
                        double flank = Math.Min(1, av / Math.Max(width, 0.025));
                        L = peak * SkinShade(flank, Clamp(light * Math.Sign(across), -1, 1), 0.23) + flash * 0.16 * flank;
                        FishPattern(kind, u, across, flank, z, ref L, ref tint);
                        L = Clamp(L, 0.355, 1);   // keep the darkest skin in tiles; eyes and feathered edges may become specks
                        double eu = (u - eyeU) * len, ev = (av - eyeV) * len;
                        if (kind != FishKind.Sardine && eu * eu + ev * ev < eyeR * eyeR)
                        { L = 0.10; tint = DarkTint; }
                    }
                    Blend(row * cols + col, L, tint, Math.Min(1, cover));
                }
            }
        }

        void DrawSchool(int k)
        {
            foreach (Fish f in schools[k]) DrawFish(f, schoolSpecies[k], schoolZ[k], schoolFlash[k]);
        }

        void Render(Grid grid)
        {
            Array.Clear(cr, 0, cr.Length); Array.Clear(cg, 0, cg.Length); Array.Clear(cb, 0, cb.Length);
            foreach (Ripple rp in ripples)   // black water; visit only the band swept by each ripple
            {
                double age = simT - rp.T0, radius = age * 55 * S, band = 3.2 * S;
                double fade = 1 - age / 4, outer = radius + band * 3, inner = Math.Max(0, radius * 0.8 - band * 3);
                double outer2 = outer * outer, inner2 = inner * inner;
                int c0 = Math.Max(0, (int)Math.Floor(rp.X - outer)), c1 = Math.Min(cols - 1, (int)Math.Ceiling(rp.X + outer));
                int r0 = Math.Max(0, (int)Math.Floor((rp.Y - outer) / ca)), r1 = Math.Min(rows - 1, (int)Math.Ceiling((rp.Y + outer) / ca));
                for (int row = r0; row <= r1; row++)
                {
                    double dy = (row + 0.5) * ca - rp.Y;
                    for (int col = c0; col <= c1; col++)
                    {
                        double dx = col + 0.5 - rp.X, d2 = dx * dx + dy * dy;
                        if (d2 < inner2 || d2 > outer2) continue;
                        double d = Math.Sqrt(d2), q = (d - radius) / band, q2 = (d - radius * 0.8) / band;
                        double L = (0.30 * Math.Exp(-q * q) + 0.12 * Math.Exp(-q2 * q2)) * fade;
                        double tint = L / WaterTint[3];
                        int i = row * cols + col;
                        cr[i] = (float)Math.Min(1.0, cr[i] + WaterTint[0] * tint);
                        cg[i] = (float)Math.Min(1.0, cg[i] + WaterTint[1] * tint);
                        cb[i] = (float)Math.Min(1.0, cb[i] + WaterTint[2] * tint);
                    }
                }
            }

            foreach (Mote m in motes)
                Disc(m.X, m.Y, Math.Max(0.35, minR * 0.45), (0.23 + 0.035 * Math.Sin(m.Phase)) * (1 - 0.25 * m.Z), WaterTint, false);

            foreach (Wake wk in wakes)   // below everything else; grows and fades
            {
                double f = 1 - wk.Age / wk.Life;
                Disc(wk.X, wk.Y, minR * (0.55 + 0.6 * (1 - f)), 0.29 * f * wk.Dm, wk.Tint, false);
            }

            foreach (Layer l in layers)
            {
                if (l.IsJelly) DrawJelly(jellies[l.Index]);
                else if (l.IsShark) { if (sharks[l.Index].Manta) DrawManta(sharks[l.Index]); else DrawShark(sharks[l.Index]); }
                else DrawSchool(l.Index);
            }

            int[] px = grid.Px;
            for (int i = 0; i < px.Length; i++) px[i] = AnimSource.Pack(cr[i], cg[i], cb[i]);
        }
    }

    // ------------------------------------------------------------------ engine

    // Turns grid pixels into a rendered frame of ASCII glyphs (software blitter).
    class Engine : IDisposable
    {
        readonly Settings s;
        public readonly int W, H, Cols, Rows, Cw, Ch;
        public readonly Bitmap Frame;
        readonly int[] buf;
        readonly byte[][] glyphs;
        readonly int n;
        readonly int ox, oy;
        readonly Grid grid;
        readonly Source src;
        readonly double aspect;
        float lvLo, lvHi;
        bool levelsInit;

        public Source Src { get { return src; } }

        public Engine(Settings settings, int w, int h, Source source, int fontPx)
        {
            s = settings; W = w; H = h; src = source;
            aspect = (double)w / h;
            string ramp = (s.Charset == 0 && source.PreferredRamp != null) ? source.PreferredRamp : s.Ramp();
            n = ramp.Length;
            glyphs = BuildGlyphs(ramp, s.FontName, fontPx, out Cw, out Ch);
            Cols = Math.Max(1, W / Cw);
            Rows = Math.Max(1, H / Ch);
            ox = (W - Cols * Cw) / 2;
            oy = (H - Rows * Ch) / 2;
            grid = new Grid(Cols, Rows);
            buf = new int[W * H];
            Frame = new Bitmap(W, H, PixelFormat.Format32bppRgb);
        }

        static byte[][] BuildGlyphs(string chars, string fontName, float px, out int cw, out int ch)
        {
            Font font;
            try { font = new Font(fontName, px, FontStyle.Regular, GraphicsUnit.Pixel); }
            catch { font = new Font(FontFamily.GenericMonospace, px, FontStyle.Regular, GraphicsUnit.Pixel); }
            using (font)
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                float w10;
                using (Bitmap tmp = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(tmp))
                    w10 = g.MeasureString("MMMMMMMMMM", font, 10000, sf).Width;
                cw = Math.Max(2, (int)Math.Ceiling(w10 / 10f));
                ch = Math.Max(3, font.Height);
                int cwl = cw, chl = ch;

                byte[][] result = new byte[chars.Length][];
                using (Bitmap bmp = new Bitmap(cwl, chl, PixelFormat.Format32bppArgb))
                using (Graphics gg = Graphics.FromImage(bmp))
                {
                    gg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    for (int i = 0; i < chars.Length; i++)
                    {
                        byte[] mask = new byte[cwl * chl];
                        if (chars[i] != ' ')
                        {
                            gg.Clear(Color.Black);
                            gg.DrawString(chars[i].ToString(), font, Brushes.White, 0, 0, sf);
                            BitmapData d = bmp.LockBits(new Rectangle(0, 0, cwl, chl), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                            byte[] raw = new byte[d.Stride * chl];
                            Marshal.Copy(d.Scan0, raw, 0, raw.Length);
                            bmp.UnlockBits(d);
                            for (int y = 0; y < chl; y++)
                                for (int x = 0; x < cwl; x++)
                                    mask[y * cwl + x] = raw[y * d.Stride + x * 4 + 1];
                        }
                        result[i] = mask;
                    }
                }
                return result;
            }
        }

        static void Tint(ColorStyle st, out int r, out int g, out int b)
        {
            switch (st)
            {
                case ColorStyle.Green: r = 51; g = 255; b = 70; break;
                case ColorStyle.Amber: r = 255; g = 176; b = 0; break;
                case ColorStyle.Cyan: r = 0; g = 225; b = 255; break;
                default: r = 235; g = 235; b = 235; break;
            }
        }

        public void Render(double t)
        {
            src.Fill(grid, t, aspect);
            int[] cells = grid.Px;

            float contrast = s.Contrast / 100f, bright = s.Brightness / 100f;
            double sweep = ((t * 0.12) % 1.4 - 0.2) * Rows;
            double sw = Math.Max(2.0, Rows * 0.05);
            int tr, tg, tb;
            Tint(s.Style, out tr, out tg, out tb);
            bool original = s.Style == ColorStyle.Original, rainbow = s.Style == ColorStyle.Rainbow;
            bool tiles = src.InlayTiles;

            // Auto-levels: stretch the 2nd..98th brightness percentile to the full range,
            // smoothed over time so the picture doesn't pump.
            float lo = 0f, span = 1f;
            if (s.AutoLevels && !src.NaturalLevels)
            {
                int[] hist = new int[64];
                for (int i = 0; i < cells.Length; i++)
                {
                    int q = cells[i];
                    float l = (0.299f * ((q >> 16) & 255) + 0.587f * ((q >> 8) & 255) + 0.114f * (q & 255)) / 255f;
                    hist[Math.Min(63, (int)(l * 64f))]++;
                }
                int acc = 0, loBin = 0, hiBin = 63, cut = cells.Length / 50;
                for (int i = 0; i < 64; i++) { acc += hist[i]; if (acc > cut) { loBin = i; break; } }
                acc = 0;
                for (int i = 63; i >= 0; i--) { acc += hist[i]; if (acc > cut) { hiBin = i; break; } }
                float nlo = loBin / 64f, nhi = (hiBin + 1) / 64f;
                if (nhi - nlo < 0.2f) nhi = Math.Min(1f, nlo + 0.2f);
                if (!levelsInit) { lvLo = nlo; lvHi = nhi; levelsInit = true; }
                else { lvLo += (nlo - lvLo) * 0.08f; lvHi += (nhi - lvHi) * 0.08f; }
                lo = lvLo; span = Math.Max(0.05f, lvHi - lvLo);
            }

            Array.Clear(buf, 0, buf.Length);
            for (int cy = 0; cy < Rows; cy++)
            {
                for (int cx = 0; cx < Cols; cx++)
                {
                    int p = cells[cy * Cols + cx];
                    int r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255;

                    float lum = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
                    lum = (lum - lo) / span;
                    lum = (lum - 0.5f) * contrast + 0.5f + bright;
                    if (s.Invert) lum = 1f - lum;

                    float glow = 0f;
                    if (s.Scanline && !src.NaturalLevels)
                    {
                        double d = (cy - sweep) / sw;
                        glow = (float)Math.Exp(-d * d);
                        lum += glow * 0.25f;
                    }
                    if (lum < 0f) lum = 0f; else if (lum > 1f) lum = 1f;

                    int idx = (int)(lum * (n - 1) + 0.5f);
                    if (idx == 0) continue;

                    int fr, fg, fb;
                    if (original)
                    {
                        fr = Math.Min(255, (int)(r * 1.25f) + 25);
                        fg = Math.Min(255, (int)(g * 1.25f) + 25);
                        fb = Math.Min(255, (int)(b * 1.25f) + 25);
                    }
                    else
                    {
                        float k = 0.4f + 0.6f * lum;
                        if (rainbow)
                        {
                            int c = AnimSource.Hsv(cx / (double)Cols * 0.6 + cy / (double)Rows * 0.4 + t * 0.08, 0.85, 1.0);
                            fr = (int)(((c >> 16) & 255) * k); fg = (int)(((c >> 8) & 255) * k); fb = (int)((c & 255) * k);
                        }
                        else { fr = (int)(tr * k); fg = (int)(tg * k); fb = (int)(tb * k); }
                    }
                    if (glow > 0.01f)
                    {
                        float gm = 1f + glow * 0.6f;
                        fr = Math.Min(255, (int)(fr * gm)); fg = Math.Min(255, (int)(fg * gm)); fb = Math.Min(255, (int)(fb * gm));
                    }

                    byte[] mask = glyphs[idx];
                    int bx = ox + cx * Cw, by = oy + cy * Ch;
                    if (tiles && lum >= 0.28f)
                    {
                        // inlay look: a tile shaded by tone with a dark letter set into it,
                        // a hairline gap between tiles; fainter cells stay loose specks
                        float tk = 0.30f + 0.55f * lum;
                        int tileR = (int)(fr * tk), tileG = (int)(fg * tk), tileB = (int)(fb * tk);
                        int inkR = fr / 8, inkG = fg / 8, inkB = fb / 8;
                        for (int y = 0; y < Ch - 1; y++)
                        {
                            int o = (by + y) * W + bx, mo = y * Cw;
                            for (int x = 0; x < Cw - 1; x++)
                            {
                                int a = mask[mo + x];
                                int pr = tileR + ((inkR - tileR) * a) / 255;
                                int pg = tileG + ((inkG - tileG) * a) / 255;
                                int pb = tileB + ((inkB - tileB) * a) / 255;
                                buf[o + x] = (255 << 24) | (pr << 16) | (pg << 8) | pb;
                            }
                        }
                        continue;
                    }
                    for (int y = 0; y < Ch; y++)
                    {
                        int o = (by + y) * W + bx, mo = y * Cw;
                        for (int x = 0; x < Cw; x++)
                        {
                            int a = mask[mo + x];
                            if (a == 0) continue;
                            buf[o + x] = (255 << 24) | (((fr * a + 127) / 255) << 16) | (((fg * a + 127) / 255) << 8) | ((fb * a + 127) / 255);
                        }
                    }
                }
            }

            BitmapData bd = Frame.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try { Marshal.Copy(buf, 0, bd.Scan0, buf.Length); }
            finally { Frame.UnlockBits(bd); }
        }

        public void Dispose()
        {
            grid.Dispose();
            Frame.Dispose();
            src.Dispose();
        }
    }

    static class Factory
    {
        public static Source MakeSource(Settings s, Rectangle screenBounds, bool allowDesktop)
        {
            if (s.Source == SourceKind.Desktop && allowDesktop) return new DesktopSource(screenBounds);
            if (s.Source == SourceKind.Images) return new ImageSource(s);
            return new AnimSource(s.Source == SourceKind.Animation ? s.Animation : 0);
        }
    }

    // ------------------------------------------------------------------ saver window

    enum RunMode { Saver, Windowed, Preview }

    class SaverForm : Form
    {
        readonly Settings s;
        readonly RunMode mode;
        readonly Rectangle screenBounds;
        readonly IntPtr previewParent;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Timer timer = new Timer();
        Engine engine;
        Source preSource;
        Point? mouseStart;
        int dumpIdx;

        public SaverForm(Settings settings, RunMode runMode, Rectangle screen, Size clientSize, IntPtr parent)
        {
            s = settings; mode = runMode; screenBounds = screen; previewParent = parent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            DoubleBuffered = true;
            BackColor = Color.Black;
            FormBorderStyle = runMode == RunMode.Windowed ? FormBorderStyle.FixedSingle : FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = runMode == RunMode.Windowed;
            KeyPreview = true;
            Text = "ASCII Saver";

            // Desktop snapshot is taken now, before this window is on screen.
            preSource = Factory.MakeSource(s, screen, runMode != RunMode.Preview);

            if (runMode == RunMode.Saver)
            {
                Bounds = screen;
                TopMost = true;
            }
            else if (runMode == RunMode.Windowed)
            {
                MaximizeBox = false;
                ClientSize = clientSize;
                Point c = new Point(screen.X + (screen.Width - Width) / 2, screen.Y + (screen.Height - Height) / 2);
                Location = c;
            }
            else
            {
                ClientSize = clientSize;
                Location = Point.Empty;
            }

            timer.Interval = Math.Max(10, 1000 / s.Fps);
            timer.Tick += OnTick;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (mode == RunMode.Preview && previewParent != IntPtr.Zero)
            {
                Native.SetParent(Handle, previewParent);
                Native.SetWindowLong(Handle, Native.GWL_STYLE, Native.GetWindowLong(Handle, Native.GWL_STYLE) | Native.WS_CHILD);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DesktopSource ds = preSource as DesktopSource;
            if (ds != null)
            {
                try { ds.Live = Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE); }
                catch { ds.Live = false; }
            }
            if (mode == RunMode.Saver) Activate();
            Rebuild();
            timer.Start();
        }

        void Rebuild()
        {
            Source src = preSource;
            if (engine != null) { src = engine.Src; engine.Dispose(); }
            preSource = null;
            int font = s.FontSize;
            if (mode == RunMode.Preview)
                font = Math.Max(3, (int)Math.Round(s.FontSize * (double)ClientSize.Width / Math.Max(1, Screen.PrimaryScreen.Bounds.Width)));
            else if (mode == RunMode.Windowed)
                font = Math.Max(4, (int)Math.Round(s.FontSize * (double)ClientSize.Width / Math.Max(1, screenBounds.Width) * 1.6));
            engine = new Engine(s, ClientSize.Width, ClientSize.Height, src, font);
        }

        void OnTick(object sender, EventArgs e)
        {
            if (mode == RunMode.Preview && previewParent != IntPtr.Zero && !Native.IsWindow(previewParent))
            {
                Application.Exit();
                return;
            }
            if (engine == null) return;
            engine.Render(clock.Elapsed.TotalSeconds);
            Invalidate();

            // Debug hook: ASCIISAVER_DUMP=<prefix> saves this window's frame at 3s and again at 8s.
            string dump = Environment.GetEnvironmentVariable("ASCIISAVER_DUMP");
            if (!string.IsNullOrEmpty(dump) && dumpIdx < 2 && clock.Elapsed.TotalSeconds > (dumpIdx == 0 ? 3.0 : 8.0))
            {
                DesktopSource ds = engine.Src as DesktopSource;
                string tag = screenBounds.X.ToString(CultureInfo.InvariantCulture) + "_" + dumpIdx;
                dumpIdx++;
                try
                {
                    engine.Frame.Save(dump + "_" + tag + ".png", ImageFormat.Png);
                    File.WriteAllText(dump + "_" + tag + ".txt", "bounds " + Bounds + ", cells " + engine.Cols + "x" + engine.Rows +
                        ", live=" + (ds != null ? ds.Live.ToString() : "n/a"));
                }
                catch { }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (engine == null) { e.Graphics.Clear(Color.Black); return; }
            e.Graphics.CompositingMode = CompositingMode.SourceCopy;
            e.Graphics.DrawImageUnscaled(engine.Frame, 0, 0);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (mode == RunMode.Saver || (mode == RunMode.Windowed && e.KeyCode == Keys.Escape)) Application.Exit();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (mode == RunMode.Saver) Application.Exit();
            else if (mode == RunMode.Windowed) Pointer.Clicks++;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Pointer.Has = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (mode == RunMode.Windowed)
            {
                Pointer.Has = true;
                Pointer.Nx = (double)e.X / Math.Max(1, ClientSize.Width);
                Pointer.Ny = (double)e.Y / Math.Max(1, ClientSize.Height);
            }
            if (mode != RunMode.Saver) return;
            if (!mouseStart.HasValue) { mouseStart = e.Location; return; }
            if (Math.Abs(e.X - mouseStart.Value.X) > 6 || Math.Abs(e.Y - mouseStart.Value.Y) > 6) Application.Exit();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            if (engine != null) engine.Dispose();
            if (preSource != null) preSource.Dispose();
            base.OnFormClosed(e);
        }
    }

    // ------------------------------------------------------------------ settings dialog

    class ConfigForm : Form
    {
        readonly Settings s;
        ComboBox cmbSource, cmbAnim, cmbStyle, cmbCharset;
        TextBox txtCustom, txtFolder;
        NumericUpDown numFont, numFps, numSlide;
        TrackBar tbContrast, tbBright;
        CheckBox chkInvert, chkScan, chkAuto;
        int y = 14;

        public ConfigForm(Settings settings)
        {
            s = settings;
            Text = "ASCII Saver Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(470, 566);
            Font = new Font("Segoe UI", 9f);

            cmbSource = Combo("Source", new string[] { "Live desktop", "Photo slideshow", "Generated animation" });
            cmbSource.SelectedIndex = (int)s.Source;
            cmbAnim = Combo("Animation", Settings.AnimationNames);
            cmbAnim.SelectedIndex = s.Animation;
            txtFolder = Text2("Photo folder", s.ImageFolder);
            Button browse = new Button();
            browse.Text = "...";
            browse.SetBounds(425, y - 31, 32, 25);
            browse.Click += delegate
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                {
                    fb.SelectedPath = txtFolder.Text;
                    if (fb.ShowDialog(this) == DialogResult.OK) txtFolder.Text = fb.SelectedPath;
                }
            };
            Controls.Add(browse);
            txtFolder.Width = 245;
            numSlide = Num("Seconds per photo", s.SlideSeconds, 2, 600);

            cmbStyle = Combo("Colour", new string[] { "Original colours", "Green terminal", "Amber terminal", "White", "Cyan", "Rainbow" });
            cmbStyle.SelectedIndex = (int)s.Style;
            cmbCharset = Combo("Characters", new string[] { "Classic  .:-=+*#%@", "Detailed (70 levels)", "Blocks  ░▒▓█", "Binary  1 0", "Fish  .,:;-toxXS%#8@", "Custom (dark to light)" });
            cmbCharset.SelectedIndex = s.Charset;
            txtCustom = Text2("Custom characters", s.CustomChars);
            numFont = Num("Character size (px)", s.FontSize, 4, 40);
            numFps = Num("Frames per second", s.Fps, 5, 60);
            tbContrast = Track("Contrast", 50, 250, s.Contrast);
            tbBright = Track("Brightness", -50, 50, s.Brightness);
            chkInvert = Check("Invert brightness", s.Invert);
            chkScan = Check("Scanline sweep effect", s.Scanline);
            chkAuto = Check("Auto-adjust contrast", s.AutoLevels);

            Label hint = new Label();
            hint.SetBounds(14, y + 4, 440, 34);
            hint.Text = "Live desktop needs Windows 10 (2004) or later; older versions show a still picture of the desktop.";
            hint.ForeColor = SystemColors.GrayText;
            Controls.Add(hint);

            Button prev = new Button(); prev.Text = "Preview"; prev.SetBounds(14, 526, 90, 28);
            prev.Click += delegate
            {
                Apply(); s.Save();
                try { Process.Start(Application.ExecutablePath, "/w"); } catch { }
            };
            Button ok = new Button(); ok.Text = "OK"; ok.SetBounds(285, 526, 85, 28);
            ok.Click += delegate { Apply(); try { s.Save(); } catch (Exception ex) { MessageBox.Show(ex.Message); } Close(); };
            Button cancel = new Button(); cancel.Text = "Cancel"; cancel.SetBounds(377, 526, 80, 28);
            cancel.Click += delegate { Close(); };
            Controls.Add(prev); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }

        Label Lbl(string text)
        {
            Label l = new Label();
            l.Text = text; l.AutoSize = false;
            l.SetBounds(14, y + 3, 150, 22);
            Controls.Add(l);
            return l;
        }

        ComboBox Combo(string label, string[] items)
        {
            Lbl(label);
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Items.AddRange(items);
            c.SetBounds(170, y, 287, 25);
            Controls.Add(c);
            y += 33;
            return c;
        }

        TextBox Text2(string label, string value)
        {
            Lbl(label);
            TextBox t = new TextBox();
            t.Text = value;
            t.SetBounds(170, y, 287, 25);
            Controls.Add(t);
            y += 33;
            return t;
        }

        NumericUpDown Num(string label, int value, int min, int max)
        {
            Lbl(label);
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min; n.Maximum = max; n.Value = Math.Max(min, Math.Min(max, value));
            n.SetBounds(170, y, 80, 25);
            Controls.Add(n);
            y += 33;
            return n;
        }

        TrackBar Track(string label, int min, int max, int value)
        {
            Lbl(label);
            TrackBar t = new TrackBar();
            t.Minimum = min; t.Maximum = max; t.Value = Math.Max(min, Math.Min(max, value));
            t.TickFrequency = (max - min) / 10;
            t.SetBounds(165, y - 3, 297, 40);
            Controls.Add(t);
            y += 38;
            return t;
        }

        CheckBox Check(string label, bool value)
        {
            CheckBox c = new CheckBox();
            c.Text = label; c.Checked = value;
            c.SetBounds(170, y, 287, 24);
            Controls.Add(c);
            y += 28;
            return c;
        }

        void Apply()
        {
            s.Source = (SourceKind)cmbSource.SelectedIndex;
            s.Animation = cmbAnim.SelectedIndex;
            s.ImageFolder = txtFolder.Text;
            s.SlideSeconds = (int)numSlide.Value;
            s.Style = (ColorStyle)cmbStyle.SelectedIndex;
            s.Charset = cmbCharset.SelectedIndex;
            s.CustomChars = txtCustom.Text;
            s.FontSize = (int)numFont.Value;
            s.Fps = (int)numFps.Value;
            s.Contrast = tbContrast.Value;
            s.Brightness = tbBright.Value;
            s.Invert = chkInvert.Checked;
            s.Scanline = chkScan.Checked;
            s.AutoLevels = chkAuto.Checked;
        }
    }

    // ------------------------------------------------------------------ entry point

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch { try { Native.SetProcessDPIAware(); } catch { } }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string a0 = args.Length > 0 ? args[0].ToLowerInvariant() : "/c";
            Settings s = a0 == "/test" ? new Settings() : Settings.Load();
            string a1 = args.Length > 1 ? args[1] : null;
            if (a0.Length > 2 && a0[2] == ':') { a1 = args[0].Substring(3); a0 = a0.Substring(0, 2); }
            if (a0 == "-s" || a0 == "-c" || a0 == "-p" || a0 == "-w") a0 = "/" + a0.Substring(1);

            switch (a0)
            {
                case "/s":
                    {
                        Cursor.Hide();
                        List<SaverForm> forms = new List<SaverForm>();
                        foreach (Screen sc in Screen.AllScreens)
                            forms.Add(new SaverForm(s, RunMode.Saver, sc.Bounds, sc.Bounds.Size, IntPtr.Zero));
                        foreach (SaverForm f in forms) f.Show();
                        Application.Run();
                        Cursor.Show();
                        return 0;
                    }
                case "/p":
                    {
                        long h;
                        if (a1 == null || !long.TryParse(a1, out h)) return 1;
                        IntPtr parent = new IntPtr(h);
                        Native.RECT r;
                        if (!Native.GetClientRect(parent, out r)) return 1;
                        Settings ps = Load(s);
                        Size sz = new Size(Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));
                        SaverForm f = new SaverForm(ps, RunMode.Preview, Screen.PrimaryScreen.Bounds, sz, parent);
                        f.Show();
                        Application.Run();
                        return 0;
                    }
                case "/w":
                    {
                        Rectangle sb = Screen.PrimaryScreen.Bounds;
                        SaverForm f = new SaverForm(s, RunMode.Windowed, sb, new Size(1280, 720), IntPtr.Zero);
                        Application.Run(f);
                        return 0;
                    }
                case "/test":
                    return RunTest(s, args);
                default:
                    Application.Run(new ConfigForm(s));
                    return 0;
            }
        }

        // The preview pane is tiny, so use generated animation rather than a screen grab.
        static Settings Load(Settings s)
        {
            if (s.Source == SourceKind.Desktop) s.Source = SourceKind.Animation;
            return s;
        }

        // /test out.png [key=value ...] : render a frame without any window.
        static int RunTest(Settings s, string[] args)
        {
            string output = args.Length > 1 ? args[1] : "frame.png";
            int w = 1280, h = 720, frames = 8;
            bool ripple = false;
            int fontPx = 0, tail = 0;
            for (int i = 2; i < args.Length; i++)
            {
                int eq = args[i].IndexOf('=');
                if (eq <= 0) continue;
                string k = args[i].Substring(0, eq), v = args[i].Substring(eq + 1);
                if (k == "w") w = int.Parse(v);
                else if (k == "h") h = int.Parse(v);
                else if (k == "frames") frames = int.Parse(v);
                else if (k == "font") fontPx = int.Parse(v);
                else if (k == "tail") tail = int.Parse(v);
                else if (k == "clicks") OceanOptions.ClickRipples = v != "0";
                else if (k == "px") { Pointer.Has = true; Pointer.Nx = double.Parse(v, CultureInfo.InvariantCulture); }
                else if (k == "py") Pointer.Ny = double.Parse(v, CultureInfo.InvariantCulture);
                else if (k == "ripple") ripple = v == "1";
                else s.Apply(k, v);
            }
            Rectangle sb = Screen.PrimaryScreen.Bounds;
            Source src = Factory.MakeSource(s, sb, true);
            int font = Math.Max(4, (int)Math.Round(s.FontSize * (double)w / sb.Width * 1.6));
            if (fontPx > 0) font = fontPx;
            using (Engine e = new Engine(s, w, h, src, font))
            {
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < frames; i++)
                {
                    if (ripple && i == frames - 12) Pointer.Clicks++;
                    e.Render(3.0 + i * 0.05);
                    if (tail > 0 && i >= frames - tail) e.Frame.Save(Path.ChangeExtension(output, null) + "_" + (i - (frames - tail)) + ".png", ImageFormat.Png);
                }
                File.WriteAllText(output + ".txt", "cells " + e.Cols + "x" + e.Rows + ", cell " + e.Cw + "x" + e.Ch +
                                  ", " + (sw.ElapsedMilliseconds / frames) + " ms/frame");
                e.Frame.Save(output, ImageFormat.Png);
            }
            return 0;
        }
    }
}
