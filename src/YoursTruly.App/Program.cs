using Avalonia;
using YoursTruly.Core.Diagnostics;
using System;
using System.Linq;

namespace YoursTruly.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Must be the first thing that happens: on the run straight after an update this
        // is what finishes installing it, and it exits rather than returning.
        Velopack.VelopackApp.Build().Run();

        // A crash is the one thing a user cannot describe usefully, so it is the one
        // thing most worth writing down.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error) Log.Failure("app.crash", error);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Failure("app.unobserved", e.Exception);
            e.SetObserved();
        };

        // Started by the login item to text for the user's other computers: a menu-bar
        // icon and a listener, no window. See Relay/RelayMode.cs.
        if (args.Contains(Relay.LaunchAgentRelay.Argument))
            return Relay.RelayMode.Run(args, () => BuildAvaloniaApp(showInDock: false));

        int code;
        try
        {
            code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            Log.Failure("app.start", error);
            throw;
        }

        Log.Record("session.end");
        return code;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(showInDock: true);

    private static AppBuilder BuildAvaloniaApp(bool showInDock)
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = showInDock })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
