using System.Windows;
using ValheimControl.Services;

namespace ValheimControl;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Window startWindow = ConfigService.ConfigExists
            ? new MainWindow()
            : new SetupWindow();

        startWindow.Show();
    }
}
