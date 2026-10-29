using System.Threading;
using System.Windows;

namespace AsciiScreenSettings
{
    public partial class App : Application
    {
        Mutex single;

        protected override void OnStartup(StartupEventArgs e)
        {
            // one settings window at a time
            bool created;
            single = new Mutex(true, "AsciiScreenSettingsSingleInstance", out created);
            if (!created) { Shutdown(); return; }
            base.OnStartup(e);
        }
    }
}
