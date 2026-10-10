using Avalonia;

namespace Pangaea.Dock.Demo;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<DemoApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}
