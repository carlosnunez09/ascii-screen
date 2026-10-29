using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AsciiScreenSettings
{
    // Edits %APPDATA%\AsciiScreen\settings.ini, the same file AsciiScreen.exe reads and live-reloads.
    // The preview at the top runs the same renderer and ocean simulation as AsciiScreen.exe.
    public partial class MainWindow : Window
    {
        static readonly string IniPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AsciiScreen", "settings.ini");

        const int PreviewW = 900, PreviewH = 300;

        readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        readonly DispatcherTimer renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        readonly Stopwatch clock = Stopwatch.StartNew();
        bool loading = true;
        bool rebuildPending;

        AsciiSaver.Settings previewSettings;
        AsciiSaver.Engine engine;
        WriteableBitmap bitmap;

        public MainWindow()
        {
            InitializeComponent();
            saveTimer.Tick += delegate { saveTimer.Stop(); Save(); if (rebuildPending) BuildPreview(); };
            renderTimer.Tick += delegate { RenderPreview(); };
            Load();
            loading = false;
            UpdateTexts();
            BuildPreview();
            renderTimer.Start();
        }

        // ---------------------------------------------------------------- preview

        void BuildPreview()
        {
            rebuildPending = false;
            if (engine != null) { engine.Dispose(); engine = null; }

            AsciiSaver.OceanOptions.Speed = SpeedSlider.Value / 100.0;
            AsciiSaver.OceanOptions.Fish = FishSlider.Value / 100.0;
            AsciiSaver.OceanOptions.Sharks = (int)SharkSlider.Value;
            AsciiSaver.OceanOptions.Extras = ExtrasBox.IsChecked == true;
            AsciiSaver.OceanOptions.ClickRipples = ClickBox.IsChecked == true;

            previewSettings = new AsciiSaver.Settings();
            previewSettings.Source = AsciiSaver.SourceKind.Animation;
            previewSettings.Animation = 3;
            previewSettings.Charset = 0;
            previewSettings.Style = (AsciiSaver.ColorStyle)Math.Max(0, StyleBox.SelectedIndex);

            int font = Math.Max(5, (int)Math.Round(SizeSlider.Value * 0.8));
            engine = new AsciiSaver.Engine(previewSettings, PreviewW, PreviewH, new AsciiSaver.AnimSource(3), font);

            if (bitmap == null)
            {
                bitmap = new WriteableBitmap(PreviewW, PreviewH, 96, 96, PixelFormats.Bgr32, null);
                PreviewRect.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            }
        }

        void RenderPreview()
        {
            if (engine == null || bitmap == null) return;
            engine.Render(clock.Elapsed.TotalSeconds);
            System.Drawing.Imaging.BitmapData d = engine.Frame.LockBits(
                new System.Drawing.Rectangle(0, 0, PreviewW, PreviewH),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try { bitmap.WritePixels(new Int32Rect(0, 0, PreviewW, PreviewH), d.Scan0, d.Stride * PreviewH, d.Stride); }
            finally { engine.Frame.UnlockBits(d); }
        }

        void Preview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            Point p = e.GetPosition(PreviewRect);
            AsciiSaver.Pointer.Has = true;
            AsciiSaver.Pointer.Nx = Math.Max(0, Math.Min(1, p.X / Math.Max(1, PreviewRect.ActualWidth)));
            AsciiSaver.Pointer.Ny = Math.Max(0, Math.Min(1, p.Y / Math.Max(1, PreviewRect.ActualHeight)));
        }

        void Preview_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { AsciiSaver.Pointer.Has = false; }
        void Preview_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { AsciiSaver.Pointer.Clicks++; }

        // ---------------------------------------------------------------- settings file

        void Load()
        {
            int style = 0, size = 8, speed = 100, fish = 100, sharks = 4;
            bool overlay = false, wallpaper = false, extras = true, clicks = true;
            try
            {
                if (File.Exists(IniPath))
                    foreach (string line in File.ReadAllLines(IniPath, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                        int n;
                        if (!int.TryParse(v, out n)) n = 0;
                        switch (k)
                        {
                            case "style": style = n; break;
                            case "charsize": size = n; break;
                            case "speed": speed = n; break;
                            case "fish": fish = n; break;
                            case "sharks": sharks = n; break;
                            case "startoverlay": overlay = v == "1" || v.ToLowerInvariant() == "true"; break;
                            case "startwallpaper": wallpaper = v == "1" || v.ToLowerInvariant() == "true"; break;
                            case "extras": extras = !(v == "0" || v.ToLowerInvariant() == "false"); break;
                            case "clickripples": clicks = !(v == "0" || v.ToLowerInvariant() == "false"); break;
                        }
                    }
            }
            catch { }
            StyleBox.SelectedIndex = Math.Max(0, Math.Min(5, style));
            SizeSlider.Value = Math.Max(8, Math.Min(24, size));
            SpeedSlider.Value = Math.Max(25, Math.Min(200, speed));
            FishSlider.Value = Math.Max(25, Math.Min(200, fish));
            SharkSlider.Value = Math.Max(0, Math.Min(4, sharks));
            OverlayBox.IsChecked = overlay;
            WallpaperBox.IsChecked = wallpaper;
            ExtrasBox.IsChecked = extras;
            ClickBox.IsChecked = clicks;
            AutostartBox.IsChecked = IsAutostartOn();
        }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IniPath));
                File.WriteAllLines(IniPath, new string[]
                {
                    "Style=" + StyleBox.SelectedIndex,
                    "CharSize=" + (int)SizeSlider.Value,
                    "Speed=" + (int)SpeedSlider.Value,
                    "Fish=" + (int)FishSlider.Value,
                    "Sharks=" + (int)SharkSlider.Value,
                    "StartOverlay=" + (OverlayBox.IsChecked == true ? 1 : 0),
                    "Extras=" + (ExtrasBox.IsChecked == true ? 1 : 0),
                    "ClickRipples=" + (ClickBox.IsChecked == true ? 1 : 0),
                    "StartWallpaper=" + (WallpaperBox.IsChecked == true ? 1 : 0),
                }, Encoding.UTF8);
            }
            catch { }
        }


        // ---------------------------------------------------------------- start with Windows (opt-in)

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static bool IsAutostartOn()
        {
            try { using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("AsciiScreen") != null; }
            catch { return false; }
        }

        void Autostart_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (AutostartBox.IsChecked == true)
                    {
                        string exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "AsciiScreen.exe"));
                        if (File.Exists(exe)) k.SetValue("AsciiScreen", "\"" + exe + "\"");   // starts in the mode chosen above
                        else AutostartBox.IsChecked = false;
                    }
                    else k.DeleteValue("AsciiScreen", false);
                }
            }
            catch { }
        }
        // ---------------------------------------------------------------- controls

        void UpdateTexts()
        {
            if (SizeText == null) return;
            SizeText.Text = (int)SizeSlider.Value + " px";
            SpeedText.Text = (int)SpeedSlider.Value + "%";
            FishText.Text = (int)FishSlider.Value + "%";
            SharkText.Text = ((int)SharkSlider.Value).ToString();
        }

        // any control changed: refresh labels, restyle the preview now, save and rebuild shortly after the last change
        void Changed(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            // overlay and wallpaper are alternatives
            if (sender == OverlayBox && OverlayBox.IsChecked == true) { loading = true; WallpaperBox.IsChecked = false; loading = false; }
            if (sender == WallpaperBox && WallpaperBox.IsChecked == true) { loading = true; OverlayBox.IsChecked = false; loading = false; }
            UpdateTexts();
            if (previewSettings != null) previewSettings.Style = (AsciiSaver.ColorStyle)Math.Max(0, StyleBox.SelectedIndex);
            AsciiSaver.OceanOptions.Speed = SpeedSlider.Value / 100.0;
            rebuildPending = true;
            saveTimer.Stop();
            saveTimer.Start();
        }

        void Reset_Click(object sender, RoutedEventArgs e)
        {
            loading = true;
            StyleBox.SelectedIndex = 0;
            SizeSlider.Value = 8;
            SpeedSlider.Value = 100;
            FishSlider.Value = 100;
            SharkSlider.Value = 4;
            OverlayBox.IsChecked = false;
            WallpaperBox.IsChecked = false;
            ExtrasBox.IsChecked = true;
            ClickBox.IsChecked = true;
            loading = false;
            UpdateTexts();
            Save();
            BuildPreview();
        }

        void Done_Click(object sender, RoutedEventArgs e)
        {
            Save();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            renderTimer.Stop();
            if (saveTimer.IsEnabled) { saveTimer.Stop(); Save(); }
            if (engine != null) { engine.Dispose(); engine = null; }
            base.OnClosed(e);
        }
    }
}

