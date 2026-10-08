using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace GymQ_ENSE707_SQA_Project;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Test mode: two windows sharing one gym, to simulate two members on separate devices.
            // Enabled only when GYMQ_TWO_DEVICES=1, so normal runs are unaffected.
            if (Environment.GetEnvironmentVariable("GYMQ_TWO_DEVICES") == "1")
            {
                var gym = new GymQ.Services.GymSession();
                desktop.MainWindow = new MainWindow(new GymQ.Desktop.ViewModels.ShellViewModel(gym)) { Title = "Device A" };
                new MainWindow(new GymQ.Desktop.ViewModels.ShellViewModel(gym)) { Title = "Device B" }.Show();
            }
            else
            {
                desktop.MainWindow = new MainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
