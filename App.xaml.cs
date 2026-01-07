using System.Windows;

namespace ClipboardManagerCS
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set shutdown mode to explicit so app doesn't close when main window is hidden
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }
}
