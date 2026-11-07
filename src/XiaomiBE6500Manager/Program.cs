using Avalonia;

namespace XiaomiBE6500Manager;

static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Matches StartupWMClass in the .desktop launcher so the dock groups the window under it.
            .With(new X11PlatformOptions { WmClass = "com.florin.xiaomi_be6500_manager" })
            .LogToTrace();
}
