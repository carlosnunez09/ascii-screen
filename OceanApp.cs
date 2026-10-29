// AsciiScreen - a native Win32 desktop app (no WinForms): its own window class and message
// loop, GDI blitting, tray icon, popup menu and a global hotkey. Overlay mode turns the window
// into a click-through layer so the sharks and fish swim over your real desktop.
//
//   Ctrl+Alt+O        toggle the desktop overlay (works from anywhere)
//   Right-click       menu (overlay, always on top, colour, exit)
//   Esc               close the window
//   /overlay          start straight in overlay mode
//
// Shares the renderer and creature simulation with AsciiSaver.cs (see build.ps1).

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AsciiSaver
{
    static class Win
    {
        public delegate IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize, style;
            public WndProc lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string lpszMenuName, lpszClassName;
            public IntPtr hIconSm;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct PAINTSTRUCT
        {
            public IntPtr hdc; public bool fErase; public Native.RECT rcPaint; public bool fRestore, fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public uint cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX w);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h,
            IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr proc);
        [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr h, IntPtr id);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr h, IntPtr rect, bool erase);
        [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, IntPtr name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Native.RECT r);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(IntPtr m, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] public static extern uint TrackPopupMenu(IntPtr m, uint flags, int x, int y, int reserved, IntPtr h, IntPtr rect);
        [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr m);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT t);
        [DllImport("gdi32.dll")]
        public static extern int StretchDIBits(IntPtr hdc, int xd, int yd, int wd, int hd, int xs, int ys, int ws, int hs,
            IntPtr bits, ref BITMAPINFOHEADER bmi, uint usage, uint rop);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA d);

        // dark title bar / frame colours (Windows 11) and dark popup menus
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", EntryPoint = "#135")] public static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] public static extern void FlushMenuThemes();

        // live wallpaper: find the desktop window that sits behind the icons
        public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);

        // used by the settings window
        [StructLayout(LayoutKind.Sequential)] public struct INITCOMMONCONTROLSEX { public uint dwSize, dwICC; }
        [DllImport("comctl32.dll")] public static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX i);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
        [DllImport("user32.dll")] public static extern bool IsDialogMessage(IntPtr h, ref MSG m);
        [DllImport("user32.dll")] public static extern bool AdjustWindowRectEx(ref Native.RECT r, uint style, bool menu, uint ex);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowText(IntPtr h, string t);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    }

    static class OceanApp
    {
        const string ClassName = "AsciiScreenWindow";
        const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_VISIBLE = 0x10000000, WS_POPUP = 0x80000000;
        const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_LAYERED = 0x80000;
        const uint SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);
        const uint WM_APP_TRAY = 0x8001;
        const int HotkeyId = 1;

        static readonly string[] StyleNames = { "Original colours", "Green terminal", "Amber terminal", "White", "Cyan", "Rainbow" };

        static Win.WndProc proc = Proc;   // kept in a field so the GC never frees the delegate
        static IntPtr hwnd;
        static Settings settings;
        static Engine engine;
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static bool overlay, wallpaper, topmost, wasDown, trackingLeave, shotDone;
        static int paintCount;
        static int originX, originY;   // where the drawing surface starts on screen (cursor polling in overlay / wallpaper)
        static Native.RECT savedRect;
        static string shotPath;
        static Icon appIcon;
        static double shotDelay = 3.0;

        // the shark icon embedded in this exe (falls back to the stock application icon)
        public static IntPtr AppIconHandle()
        {
            try
            {
                if (appIcon == null) appIcon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                return appIcon.Handle;
            }
            catch { return SystemIcons.Application.Handle; }
        }

        // black title bar and border so the window reads as one seamless dark surface
        static void DarkChrome(IntPtr h)
        {
            int on = 1, black = 0x000000, light = 0x00F0F0F0;
            try
            {
                Win.DwmSetWindowAttribute(h, 20, ref on, 4);       // DWMWA_USE_IMMERSIVE_DARK_MODE
                Win.DwmSetWindowAttribute(h, 35, ref black, 4);    // DWMWA_CAPTION_COLOR
                Win.DwmSetWindowAttribute(h, 34, ref black, 4);    // DWMWA_BORDER_COLOR
                Win.DwmSetWindowAttribute(h, 36, ref light, 4);    // DWMWA_TEXT_COLOR
            }
            catch { }
        }
        static DateTime iniStamp;
        static int tickCount;

        [STAThread]
        static int Main(string[] args)
        {
            bool created;
            using (Mutex single = new Mutex(true, "AsciiScreenSingleInstance", out created))
            {
                if (!created) return 0;

                try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
                catch { try { Native.SetProcessDPIAware(); } catch { } }

                AppConfig.Load();
                AppConfig.PushToRuntime();
                iniStamp = IniStamp();
                settings = new Settings();
                settings.Source = SourceKind.Animation;
                settings.Animation = 3;
                settings.Charset = 0;
                settings.Style = (ColorStyle)AppConfig.Style;
                SettingsWindow.OnApplied = OnSettingsApplied;
                shotPath = Environment.GetEnvironmentVariable("ASCIISCREEN_SHOT");
                double delay;
                if (double.TryParse(Environment.GetEnvironmentVariable("ASCIISCREEN_SHOT_DELAY"), NumberStyles.Float, CultureInfo.InvariantCulture, out delay)) shotDelay = delay;

                IntPtr inst = Win.GetModuleHandle(null);
                Win.WNDCLASSEX wc = new Win.WNDCLASSEX();
                wc.cbSize = (uint)Marshal.SizeOf(typeof(Win.WNDCLASSEX));
                wc.style = 0x0003;   // CS_HREDRAW | CS_VREDRAW
                wc.lpfnWndProc = proc;
                wc.hInstance = inst;
                try { Win.SetPreferredAppMode(2); Win.FlushMenuThemes(); } catch { }   // dark popup menus
                wc.hIcon = AppIconHandle();
                wc.hIconSm = wc.hIcon;
                wc.hCursor = Win.LoadCursor(IntPtr.Zero, new IntPtr(32512));   // IDC_ARROW
                wc.hbrBackground = IntPtr.Zero;
                wc.lpszClassName = ClassName;
                if (Win.RegisterClassEx(ref wc) == 0) return 1;

                int sw = Win.GetSystemMetrics(0), sh = Win.GetSystemMetrics(1);
                int w = Math.Min(1100, sw - 80), h = Math.Min(680, sh - 120);
                hwnd = Win.CreateWindowEx(WS_EX_APPWINDOW, ClassName, "Ascii Screen", WS_OVERLAPPEDWINDOW | WS_VISIBLE,
                    (sw - w) / 2, (sh - h) / 2, w, h, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) return 1;
                DarkChrome(hwnd);

                Win.SetTimer(hwnd, new IntPtr(1), 33, IntPtr.Zero);
                Win.RegisterHotKey(hwnd, HotkeyId, 0x0001 | 0x0002 | 0x4000, 0x4F);   // Alt+Ctrl+O, no repeat
                Win.RegisterHotKey(hwnd, 2, 0x0001 | 0x0002 | 0x4000, 0x57);          // Alt+Ctrl+W: live wallpaper
                TrayIcon(0);   // NIM_ADD
                if (Array.IndexOf(args, "/wallpaper") >= 0 || AppConfig.StartWallpaper) SetWallpaper(true);
                else if (Array.IndexOf(args, "/overlay") >= 0 || AppConfig.StartOverlay) SetOverlay(true);
                if (Array.IndexOf(args, "/settings") >= 0) ShowSettings();

                Win.MSG msg;
                while (Win.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (SettingsWindow.IsDialogMessage(ref msg)) continue;   // Tab, Enter, Esc inside the settings window
                    Win.TranslateMessage(ref msg);
                    Win.DispatchMessage(ref msg);
                }
                if (engine != null) engine.Dispose();
                return 0;
            }
        }

        static IntPtr Proc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            switch (m)
            {
                case 0x0014:   // WM_ERASEBKGND: we paint every pixel ourselves
                    return new IntPtr(1);
                case 0x000F:   // WM_PAINT
                    {
                        Win.PAINTSTRUCT ps;
                        IntPtr hdc = Win.BeginPaint(h, out ps);
                        paintCount++;
                        if (paintCount <= 3 || paintCount % 200 == 0)
                            Log("paint #" + paintCount + " engine=" + (engine != null) + " clip=" + ps.rcPaint.Left + "," + ps.rcPaint.Top + "," + ps.rcPaint.Right + "," + ps.rcPaint.Bottom);
                        if (engine != null) Blit(hdc);
                        Win.EndPaint(h, ref ps);
                        return IntPtr.Zero;
                    }
                case 0x0113:   // WM_TIMER
                    Tick();
                    return IntPtr.Zero;
                case 0x0200:   // WM_MOUSEMOVE
                    if (!overlay)
                    {
                        long lp = l.ToInt64();
                        Native.RECT rc; Native.GetClientRect(h, out rc);
                        Pointer.Has = true;
                        Pointer.Nx = (short)(lp & 0xFFFF) / (double)Math.Max(1, rc.Right);
                        Pointer.Ny = (short)((lp >> 16) & 0xFFFF) / (double)Math.Max(1, rc.Bottom);
                        if (!trackingLeave)
                        {
                            Win.TRACKMOUSEEVENT t = new Win.TRACKMOUSEEVENT();
                            t.cbSize = (uint)Marshal.SizeOf(typeof(Win.TRACKMOUSEEVENT));
                            t.dwFlags = 0x2;   // TME_LEAVE
                            t.hwndTrack = h;
                            trackingLeave = Win.TrackMouseEvent(ref t);
                        }
                    }
                    return IntPtr.Zero;
                case 0x02A3:   // WM_MOUSELEAVE
                    trackingLeave = false;
                    Pointer.Has = false;
                    return IntPtr.Zero;
                case 0x0201:   // WM_LBUTTONDOWN
                    if (!overlay) Pointer.Clicks++;
                    return IntPtr.Zero;
                case 0x0205:   // WM_RBUTTONUP
                    ShowMenu(h);
                    return IntPtr.Zero;
                case 0x0100:   // WM_KEYDOWN
                    if (w.ToInt32() == 0x1B && !overlay) Win.DestroyWindow(h);   // Esc
                    return IntPtr.Zero;
                case 0x007E:   // WM_DISPLAYCHANGE: monitors added, removed or resized, so fit the wallpaper to the new desktop
                    if (wallpaper) { SetWallpaper(false); SetWallpaper(true); }
                    return IntPtr.Zero;
                case 0x0312:   // WM_HOTKEY
                    if (w.ToInt32() == 2) SetWallpaper(!wallpaper); else SetOverlay(!overlay);
                    return IntPtr.Zero;
                case 0x8001:   // tray icon callback
                    {
                        uint ev = (uint)(l.ToInt64() & 0xFFFF);
                        if (ev == 0x0203) { if (overlay) SetOverlay(false); else if (wallpaper) SetWallpaper(false); else { Win.ShowWindow(h, 9); Win.SetForegroundWindow(h); } }   // double-click
                        else if (ev == 0x0205) ShowMenu(h);                                                                                  // right-click
                        return IntPtr.Zero;
                    }
                case 0x0002:   // WM_DESTROY
                    Win.KillTimer(h, new IntPtr(1));
                    Win.UnregisterHotKey(h, HotkeyId);
                    Win.UnregisterHotKey(h, 2);
                    TrayIcon(2);   // NIM_DELETE
                    Win.PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return Win.DefWindowProc(h, m, w, l);
        }

        static DateTime IniStamp()
        {
            try { string p = AppConfig.FilePath(); return File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        // The modern settings app (WPF, Windows 11 Fluent) edits the same ini file; pick up its changes live.
        static void CheckSettingsFile()
        {
            DateTime t = IniStamp();
            if (t == iniStamp) return;
            iniStamp = t;
            int size = AppConfig.CharSize, fish = AppConfig.Fish, sharks = AppConfig.Sharks;
            AppConfig.Load();
            AppConfig.PushToRuntime();
            settings.Style = (ColorStyle)AppConfig.Style;   // colour and speed apply without a restart of the ocean
            if ((size != AppConfig.CharSize || fish != AppConfig.Fish || sharks != AppConfig.Sharks) && engine != null)
            {
                engine.Dispose();
                engine = null;
            }
        }

        // Opens the Fluent settings app next to this exe; falls back to the built-in Win32 window.
        static void ShowSettings()
        {
            try
            {
                string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-app", "AsciiScreenSettings.exe");
                if (File.Exists(exe))
                {
                    ProcessStartInfo psi = new ProcessStartInfo(exe);
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    return;
                }
            }
            catch { }
            SettingsWindow.Show();
        }

        static void Tick()
        {
            if (++tickCount % 15 == 0) CheckSettingsFile();
            Native.RECT rc; Native.GetClientRect(hwnd, out rc);
            int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
            if (w < 16 || h < 16) return;   // minimised
            if (engine == null || engine.W != w || engine.H != h)
            {
                if (engine != null) engine.Dispose();
                engine = new Engine(settings, w, h, new AnimSource(3), AppConfig.CharSize);
            }
            if (overlay || wallpaper) PollCursor(w, h);
            if (wallpaper && tickCount % 60 == 1) Log("tick " + tickCount + " client=" + w + "x" + h + " cells=" + engine.Cols + "x" + engine.Rows + " cell=" + engine.Cw + "x" + engine.Ch);
            engine.Render(clock.Elapsed.TotalSeconds);

            if (shotPath != null && !shotDone && clock.Elapsed.TotalSeconds > shotDelay)
            {
                shotDone = true;   // test hook: ASCIIOCEAN_SHOT=<png> saves one frame, then exits
                try { engine.Frame.Save(shotPath, ImageFormat.Png); } catch { }
                if (!overlay) Win.DestroyWindow(hwnd);
            }
            Win.InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        // The settings window changed something: restyle now, rebuild the ocean on the next tick.
        static void OnSettingsApplied()
        {
            settings.Style = (ColorStyle)AppConfig.Style;
            if (engine != null) { engine.Dispose(); engine = null; }
        }

        // In overlay mode the window is click-through, so it reads the cursor and button itself.
        static void PollCursor(int w, int h)
        {
            Win.POINT p;
            Win.GetCursorPos(out p);
            Pointer.Has = true;
            Pointer.Nx = (p.X - originX) / (double)w;
            Pointer.Ny = (p.Y - originY) / (double)h;
            bool down = (Win.GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (down && !wasDown) Pointer.Clicks++;
            wasDown = down;
        }

        static void Blit(IntPtr hdc)
        {
            BitmapData d = engine.Frame.LockBits(new Rectangle(0, 0, engine.W, engine.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                Win.BITMAPINFOHEADER bi = new Win.BITMAPINFOHEADER();
                bi.biSize = (uint)Marshal.SizeOf(typeof(Win.BITMAPINFOHEADER));
                bi.biWidth = engine.W; bi.biHeight = -engine.H;   // negative = top-down rows
                bi.biPlanes = 1; bi.biBitCount = 32;
                Win.StretchDIBits(hdc, 0, 0, engine.W, engine.H, 0, 0, engine.W, engine.H, d.Scan0, ref bi, 0, 0x00CC0020);
            }
            finally { engine.Frame.UnlockBits(d); }
        }

        // Debug log: set ASCIISCREEN_LOG=<file> to see what wallpaper mode finds and does.
        static void Log(string m)
        {
            string p = Environment.GetEnvironmentVariable("ASCIISCREEN_LOG");
            if (string.IsNullOrEmpty(p)) return;
            try { File.AppendAllText(p, m + "\r\n"); } catch { }
        }

        // The desktop window that sits behind the icons. Explorer builds it when sent message 0x052C.
        // Windows 11 24H2+ keeps the icon layer (SHELLDLL_DefView) inside Progman, with the wallpaper WorkerW behind it;
        // older builds put the icon layer in one WorkerW and the wallpaper in the next one.
        static IntPtr FindWallpaperHost(out IntPtr insertAfter)
        {
            insertAfter = IntPtr.Zero;
            IntPtr progman = Win.FindWindow("Progman", null);
            Log("Progman=" + progman);
            if (progman == IntPtr.Zero) return IntPtr.Zero;
            IntPtr result;
            Win.SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out result);

            IntPtr defView = Win.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr wpWorker = Win.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            Log("DefView under Progman=" + defView + "  WorkerW under Progman=" + wpWorker);
            if (defView != IntPtr.Zero)
            {
                insertAfter = defView;   // just under the icons, above the wallpaper layer
                return progman;
            }

            IntPtr host = IntPtr.Zero;
            Win.EnumWindows(delegate (IntPtr top, IntPtr l)
            {
                if (Win.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    host = Win.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return true;
            }, IntPtr.Zero);
            return host;
        }

        // Live wallpaper: the window becomes a child of the desktop, behind the icons, across every monitor.
        static void SetWallpaper(bool on)
        {
            if (on == wallpaper) return;
            if (on)
            {
                if (overlay) SetOverlay(false);
                IntPtr insertAfter;
                IntPtr host = FindWallpaperHost(out insertAfter);
                if (host == IntPtr.Zero) return;   // no desktop window found: stay a normal window

                Win.GetWindowRect(hwnd, out savedRect);
                int vx = Win.GetSystemMetrics(76), vy = Win.GetSystemMetrics(77), vw = Win.GetSystemMetrics(78), vh = Win.GetSystemMetrics(79);
                Native.SetWindowLong(hwnd, GWL_EXSTYLE, WS_EX_TOOLWINDOW | WS_EX_LAYERED);
                Native.SetWindowLong(hwnd, GWL_STYLE, unchecked((int)(0x40000000 | WS_VISIBLE)));   // WS_CHILD
                Native.SetParent(hwnd, host);
                Win.POINT origin; origin.X = vx; origin.Y = vy;
                Win.ScreenToClient(host, ref origin);
                Win.SetWindowPos(hwnd, insertAfter, origin.X, origin.Y, vw, vh, SWP_FRAMECHANGED | SWP_SHOWWINDOW | 0x10);
                // a layered child gets its own drawing surface; without this the desktop layer leaves it no visible area
                Win.SetLayeredWindowAttributes(hwnd, 0, 255, 0x2);   // LWA_ALPHA
                Native.RECT wr; Win.GetWindowRect(hwnd, out wr);
                Log("host=" + host + " insertAfter=" + insertAfter + " origin=" + origin.X + "," + origin.Y + " size=" + vw + "x" + vh +
                    " parent now=" + Win.GetParent(hwnd) + " visible=" + Win.IsWindowVisible(hwnd) +
                    " rect=" + wr.Left + "," + wr.Top + "," + wr.Right + "," + wr.Bottom);
                if (Environment.GetEnvironmentVariable("ASCIISCREEN_LOG") != null)
                {
                    // z-order of the desktop's children, topmost first
                    Win.EnumChildWindows(host, delegate (IntPtr c, IntPtr l)
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder(64);
                        Win.GetClassName(c, sb, 64);
                        Native.RECT r; Win.GetWindowRect(c, out r);
                        Log("  child " + c + " " + sb + " visible=" + Win.IsWindowVisible(c) + " rect=" + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom + (c == hwnd ? "   <== us" : ""));
                        return true;
                    }, IntPtr.Zero);
                }
                wallpaper = true;
                originX = vx; originY = vy;
            }
            else
            {
                wallpaper = false;
                Native.SetParent(hwnd, IntPtr.Zero);
                Native.SetWindowLong(hwnd, GWL_EXSTYLE, WS_EX_APPWINDOW | (topmost ? WS_EX_TOPMOST : 0));
                Native.SetWindowLong(hwnd, GWL_STYLE, unchecked((int)(WS_OVERLAPPEDWINDOW | WS_VISIBLE)));
                Win.SetWindowPos(hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, savedRect.Left, savedRect.Top,
                    savedRect.Right - savedRect.Left, savedRect.Bottom - savedRect.Top, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
                originX = 0; originY = 0;
            }
            Pointer.Has = false;
            Win.SetTimer(hwnd, new IntPtr(1), (uint)(wallpaper ? 42 : 33), IntPtr.Zero);   // a wallpaper does not need 30 fps
            if (engine != null) { engine.Dispose(); engine = null; }   // the surface size changed
        }

        static void SetOverlay(bool on)
        {
            if (on == overlay) return;
            if (on && wallpaper) SetWallpaper(false);
            overlay = on;
            if (on)
            {
                Win.GetWindowRect(hwnd, out savedRect);
                Native.SetWindowLong(hwnd, GWL_STYLE, unchecked((int)(WS_POPUP | WS_VISIBLE)));
                Native.SetWindowLong(hwnd, GWL_EXSTYLE, WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW);
                Win.SetLayeredWindowAttributes(hwnd, 0, 0, 0x1);   // LWA_COLORKEY: pure black is see-through
                Win.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, Win.GetSystemMetrics(0), Win.GetSystemMetrics(1), SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            }
            else
            {
                Native.SetWindowLong(hwnd, GWL_EXSTYLE, WS_EX_APPWINDOW | (topmost ? WS_EX_TOPMOST : 0));
                Native.SetWindowLong(hwnd, GWL_STYLE, unchecked((int)(WS_OVERLAPPEDWINDOW | WS_VISIBLE)));
                Win.SetWindowPos(hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, savedRect.Left, savedRect.Top,
                    savedRect.Right - savedRect.Left, savedRect.Bottom - savedRect.Top, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            }
            Pointer.Has = false;
        }

        static void ShowMenu(IntPtr h)
        {
            IntPtr menu = Win.CreatePopupMenu();
            Win.AppendMenu(menu, 0u | (overlay ? 8u : 0u), new UIntPtr(1), "Overlay on desktop\tCtrl+Alt+O");
            Win.AppendMenu(menu, 0u | (wallpaper ? 8u : 0u), new UIntPtr(4), "Live wallpaper\tCtrl+Alt+W");
            Win.AppendMenu(menu, 0u | (topmost ? 8u : 0u), new UIntPtr(2), "Always on top");
            Win.AppendMenu(menu, 0x800, UIntPtr.Zero, null);
            for (int i = 0; i < StyleNames.Length; i++)
                Win.AppendMenu(menu, 0u | ((int)settings.Style == i ? 8u : 0u), new UIntPtr((uint)(10 + i)), StyleNames[i]);
            Win.AppendMenu(menu, 0x800, UIntPtr.Zero, null);
            Win.AppendMenu(menu, 0, new UIntPtr(3), "Settings...");
            Win.AppendMenu(menu, 0, new UIntPtr(99), "Exit");

            Win.POINT p;
            Win.GetCursorPos(out p);
            Win.SetForegroundWindow(h);   // required or the menu will not dismiss on outside click
            uint cmd = Win.TrackPopupMenu(menu, 0x100 | 0x2, p.X, p.Y, 0, h, IntPtr.Zero);   // TPM_RETURNCMD | TPM_RIGHTBUTTON
            Win.PostMessage(h, 0, IntPtr.Zero, IntPtr.Zero);
            Win.DestroyMenu(menu);

            if (cmd == 1) SetOverlay(!overlay);
            else if (cmd == 2)
            {
                topmost = !topmost;
                if (!overlay) Win.SetWindowPos(hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, 0x2 | 0x1 | 0x10);   // NOMOVE|NOSIZE|NOACTIVATE
            }
            else if (cmd == 3) ShowSettings();
            else if (cmd == 4) SetWallpaper(!wallpaper);
            else if (cmd >= 10 && cmd < 10 + StyleNames.Length)
            {
                settings.Style = (ColorStyle)(cmd - 10);
                AppConfig.Style = (int)cmd - 10;
                AppConfig.Save();
            }
            else if (cmd == 99) Win.DestroyWindow(hwnd);
        }

        static void TrayIcon(uint message)
        {
            Win.NOTIFYICONDATA nid = new Win.NOTIFYICONDATA();
            nid.cbSize = (uint)Marshal.SizeOf(typeof(Win.NOTIFYICONDATA));
            nid.hWnd = hwnd;
            nid.uID = 1;
            nid.uFlags = 0x1 | 0x2 | 0x4;   // NIF_MESSAGE | NIF_ICON | NIF_TIP
            nid.uCallbackMessage = WM_APP_TRAY;
            nid.hIcon = AppIconHandle();
            nid.szTip = "Ascii Screen  (Ctrl+Alt+O: desktop overlay)";
            Win.Shell_NotifyIcon(message, ref nid);
        }
    }
}
