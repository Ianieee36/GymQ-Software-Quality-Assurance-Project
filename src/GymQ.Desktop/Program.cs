using System;
using Avalonia;

namespace GymQ_ENSE707_SQA_Project;

public static class Program
{
    // Initialization code. Don't use any Visual Studio, MSBuild or Avalonia
    // code before BuildAvaloniaApp() has been called.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
