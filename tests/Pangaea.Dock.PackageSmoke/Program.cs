using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;
using Pangaea.Dock.Avalonia;

AppBuilder.Configure<SmokeApp>().UseHeadless(new() { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
var registry = new PanelRegistry(); registry.Register(new("hello", "Hello"), _ => new HelloSession());
var views = new PanelViewRegistry(); views.Register("hello", _ => new TextBlock { Text = "No Laurentia. No Dock.Avalonia. Just a package.", Margin = new(24) });
using var workspace = new DockWorkspace(registry);
await workspace.AddAsync("hello", workspace.Layout.Groups.First().Id);
using var control = new DockWorkspaceControl(workspace, views);
var window = new Window { Width = 640, Height = 360, Content = control }; window.Show(); Dispatcher.UIThread.RunJobs();
using var frame = window.CaptureRenderedFrame() ?? throw new Exception("Package controls did not render.");
if (control.Surfaces.Count != 1 || control.Surfaces.Values.First().Geometry?.Groups.Count != 1) throw new Exception("Package docking failed.");
window.Close(); Console.WriteLine("PASS: independent NuGet-only consumer rendered Pangaea Dock.");
sealed class HelloSession : PanelSession;
sealed class SmokeApp : Application { public override void Initialize() => Styles.Add(new FluentTheme()); }
