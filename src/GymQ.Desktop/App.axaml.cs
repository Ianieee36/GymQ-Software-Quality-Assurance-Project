using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GymQ.Persistence;
using GymQ.Services;
using GymQ.Desktop.ViewModels;
using System.Text.Json;

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
            JsonGymStateStore? store = null;
            GymSession gym;
            string? persistenceWarning = null;
            try
            {
                if (Environment.GetEnvironmentVariable("GYMQ_DISABLE_PERSISTENCE") == "1")
                    gym = new GymSession();
                else
                {
                    var path = Environment.GetEnvironmentVariable("GYMQ_DATA_PATH")
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GymQ", "gym-state.json");
                    store = new JsonGymStateStore(path);
                    gym = GymSession.OpenPersistent(store);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
            {
                store?.Dispose();
                store = null;
                gym = new GymSession(seed: false);
                persistenceWarning = "Saved data could not be opened. Your existing files have been preserved. "
                    + "This run uses memory only and will not save changes. " + ex.Message;
            }
            desktop.Exit += (_, _) =>
            {
                gym.SaveState();
                store?.Dispose();
            };
            // Test mode: two windows sharing one gym, to simulate two members on separate devices.
            // Enabled only when GYMQ_TWO_DEVICES=1, so normal runs are unaffected.
            if (Environment.GetEnvironmentVariable("GYMQ_TWO_DEVICES") == "1")
            {
                desktop.MainWindow = new MainWindow(new ShellViewModel(gym, persistenceWarning)) { Title = "Device A" };
                new MainWindow(new ShellViewModel(gym, persistenceWarning)) { Title = "Device B" }.Show();
            }
            else
            {
                desktop.MainWindow = new MainWindow(new ShellViewModel(gym, persistenceWarning));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
