// AsciiScreen settings: saved to %APPDATA%\AsciiScreen\settings.ini, edited in a native Win32
// window built from standard Windows controls (combo box, trackbars, checkbox, buttons).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AsciiSaver
{
    static class AppConfig
    {
        public static int Style = 0;          // ColorStyle index
        public static int CharSize = 8;       // pixels
        public static int Speed = 100;        // percent
        public static int Fish = 100;         // percent
        public static int Sharks = 4;         // 0..4
        public static bool StartOverlay = false;
        public static bool StartWallpaper = false;
        public static bool Extras = true;     // manta ray and jellyfish
        public static bool ClickRipples = true;   // a mouse click sends a ripple

        public static string FilePath()
        {
            return Path.Combine(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AsciiScreen"), "settings.ini");
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        public static void Load()
        {
            try
            {
                string p = FilePath();
                if (!File.Exists(p)) return;
                foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                    int n;
                    switch (k)
                    {
                        case "style": if (int.TryParse(v, out n)) Style = Clamp(n, 0, 5); break;
                        case "charsize": if (int.TryParse(v, out n)) CharSize = Clamp(n, 8, 24); break;
                        case "speed": if (int.TryParse(v, out n)) Speed = Clamp(n, 25, 200); break;
                        case "fish": if (int.TryParse(v, out n)) Fish = Clamp(n, 25, 200); break;
                        case "sharks": if (int.TryParse(v, out n)) Sharks = Clamp(n, 0, 4); break;
                        case "startoverlay": StartOverlay = v == "1" || v.ToLowerInvariant() == "true"; break;
                        case "startwallpaper": StartWallpaper = v == "1" || v.ToLowerInvariant() == "true"; break;
                        case "extras": Extras = !(v == "0" || v.ToLowerInvariant() == "false"); break;
                        case "clickripples": ClickRipples = !(v == "0" || v.ToLowerInvariant() == "false"); break;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                string p = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllLines(p, new string[]
                {
                    "Style=" + Style, "CharSize=" + CharSize, "Speed=" + Speed, "Fish=" + Fish,
                    "Sharks=" + Sharks, "StartOverlay=" + (StartOverlay ? 1 : 0), "Extras=" + (Extras ? 1 : 0), "StartWallpaper=" + (StartWallpaper ? 1 : 0), "ClickRipples=" + (ClickRipples ? 1 : 0)
                }, Encoding.UTF8);
            }
            catch { }
        }

        // Hands the values the ocean simulation reads.
        public static void PushToRuntime()
        {
            OceanOptions.Speed = Speed / 100.0;
            OceanOptions.Fish = Fish / 100.0;
            OceanOptions.Sharks = Sharks;
            OceanOptions.Extras = Extras;
            OceanOptions.ClickRipples = ClickRipples;
        }
    }

    static class SettingsWindow
    {
        const string ClassName = "AsciiScreenSettings";
        const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_TABSTOP = 0x00010000, WS_GROUP = 0x00020000;
        const uint WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_VSCROLL = 0x00200000;
        const uint CBS_DROPDOWNLIST = 3, BS_PUSHBUTTON = 0, BS_DEFPUSHBUTTON = 1, BS_AUTOCHECKBOX = 3, TBS_AUTOTICKS = 1;
        const uint WM_SETFONT = 0x30, CB_ADDSTRING = 0x143, CB_GETCURSEL = 0x147, CB_SETCURSEL = 0x14E;
        const uint BM_GETCHECK = 0xF0, BM_SETCHECK = 0xF1;
        const uint TBM_GETPOS = 0x400, TBM_SETPOS = 0x405, TBM_SETRANGE = 0x406, TBM_SETTICFREQ = 0x414;
        const int IdOk = 1, IdCancel = 2, IdApply = 3, ClientW = 432, ClientH = 336;

        public static readonly string[] StyleNames = { "Original colours", "Green terminal", "Amber terminal", "White", "Cyan", "Rainbow" };

        static readonly Win.WndProc proc = Proc;
        static bool registered;
        static IntPtr hwnd, font, inst;
        static IntPtr cmbStyle, tbSize, tbSpeed, tbFish, tbSharks, chkOverlay;
        static IntPtr lblSize, lblSpeed, lblFish, lblSharks;

        public static Action OnApplied;

        public static bool IsDialogMessage(ref Win.MSG msg)
        {
            return hwnd != IntPtr.Zero && Win.IsDialogMessage(hwnd, ref msg);
        }

        static IntPtr Ctl(string cls, string text, uint style, int x, int y, int w, int h, int id)
        {
            IntPtr c = Win.CreateWindowEx(0, cls, text, WS_CHILD | WS_VISIBLE | style, x, y, w, h, hwnd, new IntPtr(id), inst, IntPtr.Zero);
            Win.SendMessage(c, WM_SETFONT, font, new IntPtr(1));
            return c;
        }

        static IntPtr Slider(int y, int min, int max, int value, int tickEvery)
        {
            IntPtr t = Ctl("msctls_trackbar32", "", WS_TABSTOP | TBS_AUTOTICKS, 146, y - 4, 214, 32, 0);
            Win.SendMessage(t, TBM_SETRANGE, new IntPtr(1), new IntPtr((max << 16) | (min & 0xFFFF)));
            Win.SendMessage(t, TBM_SETTICFREQ, new IntPtr(tickEvery), IntPtr.Zero);
            Win.SendMessage(t, TBM_SETPOS, new IntPtr(1), new IntPtr(value));
            return t;
        }

        public static void Show()
        {
            if (hwnd != IntPtr.Zero)
            {
                Win.ShowWindow(hwnd, 9);
                Win.SetForegroundWindow(hwnd);
                return;
            }
            inst = Win.GetModuleHandle(null);
            if (!registered)
            {
                Win.INITCOMMONCONTROLSEX icc = new Win.INITCOMMONCONTROLSEX();
                icc.dwSize = (uint)Marshal.SizeOf(typeof(Win.INITCOMMONCONTROLSEX));
                icc.dwICC = 0x4;   // ICC_BAR_CLASSES (trackbars)
                Win.InitCommonControlsEx(ref icc);

                Win.WNDCLASSEX wc = new Win.WNDCLASSEX();
                wc.cbSize = (uint)Marshal.SizeOf(typeof(Win.WNDCLASSEX));
                wc.lpfnWndProc = proc;
                wc.hInstance = inst;
                wc.hIcon = OceanApp.AppIconHandle();
                wc.hIconSm = wc.hIcon;
                wc.hCursor = Win.LoadCursor(IntPtr.Zero, new IntPtr(32512));
                wc.hbrBackground = new IntPtr(16);   // COLOR_BTNFACE + 1
                wc.lpszClassName = ClassName;
                Win.RegisterClassEx(ref wc);
                registered = true;
            }
            font = SystemFonts.MessageBoxFont.ToHfont();

            uint style = WS_CAPTION | WS_SYSMENU;
            Native.RECT r = new Native.RECT();
            r.Right = ClientW; r.Bottom = ClientH;
            Win.AdjustWindowRectEx(ref r, style, false, 0);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            int x = (Win.GetSystemMetrics(0) - w) / 2, y = (Win.GetSystemMetrics(1) - h) / 2;
            hwnd = Win.CreateWindowEx(0, ClassName, "Ascii Screen Settings", style, x, y, w, h, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return;

            Ctl("STATIC", "Colour", 0, 16, 19, 124, 20, 0);
            cmbStyle = Ctl("COMBOBOX", "", WS_TABSTOP | WS_GROUP | WS_VSCROLL | CBS_DROPDOWNLIST, 146, 16, 270, 200, 0);
            foreach (string n in StyleNames) Win.SendMessage(cmbStyle, CB_ADDSTRING, IntPtr.Zero, n);
            Win.SendMessage(cmbStyle, CB_SETCURSEL, new IntPtr(AppConfig.Style), IntPtr.Zero);

            Ctl("STATIC", "Character size", 0, 16, 59, 124, 20, 0);
            tbSize = Slider(56, 8, 24, AppConfig.CharSize, 2);
            lblSize = Ctl("STATIC", "", 0, 366, 59, 56, 20, 0);

            Ctl("STATIC", "Swim speed", 0, 16, 99, 124, 20, 0);
            tbSpeed = Slider(96, 25, 200, AppConfig.Speed, 25);
            lblSpeed = Ctl("STATIC", "", 0, 366, 99, 56, 20, 0);

            Ctl("STATIC", "Fish", 0, 16, 139, 124, 20, 0);
            tbFish = Slider(136, 25, 200, AppConfig.Fish, 25);
            lblFish = Ctl("STATIC", "", 0, 366, 139, 56, 20, 0);

            Ctl("STATIC", "Sharks", 0, 16, 179, 124, 20, 0);
            tbSharks = Slider(176, 0, 4, AppConfig.Sharks, 1);
            lblSharks = Ctl("STATIC", "", 0, 366, 179, 56, 20, 0);

            chkOverlay = Ctl("BUTTON", "Start in desktop overlay mode", WS_TABSTOP | BS_AUTOCHECKBOX, 146, 220, 270, 22, 0);
            Win.SendMessage(chkOverlay, BM_SETCHECK, new IntPtr(AppConfig.StartOverlay ? 1 : 0), IntPtr.Zero);

            Ctl("STATIC", "Ctrl+Alt+O toggles the overlay from anywhere. Changes apply when you press OK or Apply; fish and shark counts restart the ocean.",
                0, 16, 256, 400, 36, 0);

            Ctl("BUTTON", "OK", WS_TABSTOP | BS_DEFPUSHBUTTON, 160, 298, 80, 26, IdOk);
            Ctl("BUTTON", "Cancel", WS_TABSTOP | BS_PUSHBUTTON, 248, 298, 80, 26, IdCancel);
            Ctl("BUTTON", "Apply", WS_TABSTOP | BS_PUSHBUTTON, 336, 298, 80, 26, IdApply);

            UpdateLabels();
            Win.ShowWindow(hwnd, 5);
            Win.SetForegroundWindow(hwnd);
        }

        static int Pos(IntPtr trackbar) { return (int)Win.SendMessage(trackbar, TBM_GETPOS, IntPtr.Zero, IntPtr.Zero).ToInt64(); }

        static void UpdateLabels()
        {
            Win.SetWindowText(lblSize, Pos(tbSize) + " px");
            Win.SetWindowText(lblSpeed, Pos(tbSpeed) + "%");
            Win.SetWindowText(lblFish, Pos(tbFish) + "%");
            Win.SetWindowText(lblSharks, Pos(tbSharks).ToString(CultureInfo.InvariantCulture));
        }

        static void Apply()
        {
            int sel = (int)Win.SendMessage(cmbStyle, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
            if (sel >= 0) AppConfig.Style = sel;
            AppConfig.CharSize = Pos(tbSize);
            AppConfig.Speed = Pos(tbSpeed);
            AppConfig.Fish = Pos(tbFish);
            AppConfig.Sharks = Pos(tbSharks);
            AppConfig.StartOverlay = Win.SendMessage(chkOverlay, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() == 1;
            AppConfig.Save();
            AppConfig.PushToRuntime();
            if (OnApplied != null) OnApplied();
        }

        static IntPtr Proc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            switch (m)
            {
                case 0x0111:   // WM_COMMAND
                    {
                        int id = (int)(w.ToInt64() & 0xFFFF);
                        if (id == IdOk) { Apply(); Win.DestroyWindow(h); }
                        else if (id == IdCancel) Win.DestroyWindow(h);
                        else if (id == IdApply) Apply();
                        return IntPtr.Zero;
                    }
                case 0x0114:   // WM_HSCROLL (a trackbar moved)
                    UpdateLabels();
                    return IntPtr.Zero;
                case 0x0002:   // WM_DESTROY
                    hwnd = IntPtr.Zero;
                    if (font != IntPtr.Zero) { Win.DeleteObject(font); font = IntPtr.Zero; }
                    return IntPtr.Zero;
            }
            return Win.DefWindowProc(h, m, w, l);
        }
    }
}


