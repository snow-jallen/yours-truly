using System.Diagnostics;
using System.Net;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using YoursTruly.Core.Diagnostics;
using YoursTruly.Messaging.Mac;
using YoursTruly.Messaging.Relay;
using YoursTruly.Messaging.Settings;

namespace YoursTruly.App.Relay;

/// <summary>Yours Truly started by its login item with <c>--relay</c>: no window and no
/// Dock icon, just an icon in the menu bar and a listener that sends texts for the
/// user's other computers through this Mac's Messages app.</summary>
public sealed class RelayMode
{
    /// <summary>Set before the window system starts when this copy is the relay, so
    /// <see cref="App"/> shows a menu-bar icon instead of a window.</summary>
    public static RelayMode? Current { get; private set; }

    private readonly RelayServer _server;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private NativeMenuItem? _count;
    private NativeMenuItem? _pause;
    private DispatcherTimer? _updateWatch;

    private RelayMode(RelayServer server) => _server = server;

    private static Version? Running => typeof(RelayMode).Assembly.GetName().Version;

    /// <summary>Starts listening, then runs the app with only a menu-bar icon. Returns
    /// the exit code launchd reads: 0 means stay stopped until next login, anything
    /// else means start again.</summary>
    public static int Run(string[] args, Func<AppBuilder> build)
    {
        // Only the settings and the log. Not AppServices.Start, which opens the last
        // list's database: the relay has no business with anybody's directory.
        Log.UseForApp(new JsonlActivityLog(AppPaths.Logs));
        var settings = new SettingsStore(AppPaths.SettingsFile).Load();

        // Switched on with no key means the settings were reset or copied from another
        // computer. Nothing can be sent without one.
        if (RelayKey.Normalise(settings.RelayHost.Key).Length == 0)
        {
            Log.Record("relay.start", Log.Details(("ok", false), ("why", "no-key")));
            return 0;
        }

        var server = new RelayServer(
            settings.RelayHost.Key,
            new MessagesTextSender(new AppleScriptRunner()),
            version: Running?.ToString() ?? "");
        try
        {
            server.Start();
        }
        catch (HttpListenerException e)
        {
            // Almost always another copy already listening. Leave it to that one rather
            // than fighting over the port.
            Log.Failure("relay.start", e);
            return 0;
        }

        Log.Record("relay.start", Log.Details(("ok", true), ("port", server.Port)));
        Current = new RelayMode(server);
        server.TextHandled += _ => Dispatcher.UIThread.Post(Current.ShowCount);

        var code = build().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        server.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        return code;
    }

    /// <summary>Called by <see cref="App"/> once the window system is up.</summary>
    public void Attach(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        ShowIcon();
        WatchForUpdates();
    }

    private void ShowIcon()
    {
        _count = new NativeMenuItem { IsEnabled = false };
        _pause = new NativeMenuItem();
        _pause.Click += (_, _) => TogglePause();

        var open = new NativeMenuItem { Header = "Open Yours Truly" };
        open.Click += (_, _) => OpenApp();

        var quit = new NativeMenuItem { Header = "Stop until I next log in" };
        quit.Click += (_, _) => Stop();

        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem { Header = "Texting for your other computers", IsEnabled = false });
        menu.Items.Add(_count);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_pause);
        menu.Items.Add(open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://YoursTruly.App/app-icon.png"))),
            ToolTipText = "Yours Truly is texting for your other computers",
            Menu = menu,
            IsVisible = true,
        };
        TrayIcon.SetIcons(Application.Current!, [tray]);

        ShowCount();
        ShowPaused();
    }

    private void ShowCount()
    {
        if (_count is null) return;
        var today = _server.Allowance.SentToday;
        _count.Header = today == 1 ? "1 text sent today" : $"{today} texts sent today";
    }

    private void ShowPaused()
    {
        if (_pause is null) return;
        _pause.Header = _server.Paused ? "Resume" : "Pause";
    }

    private void TogglePause()
    {
        _server.Paused = !_server.Paused;
        Log.Record("relay.pause", Log.Details(("paused", _server.Paused)));
        ShowPaused();
    }

    /// <summary>The ordinary window, as a separate copy of the app: this one has no
    /// window to show and must keep listening.</summary>
    private static void OpenApp()
    {
        if (Environment.ProcessPath is { } self)
            Process.Start(new ProcessStartInfo(self) { UseShellExecute = false });
    }

    /// <summary>Exits cleanly, which tells launchd not to restart it until next login.
    /// Turning it off for good is a switch in Setup.</summary>
    private void Stop()
    {
        Log.Record("relay.stop");
        _desktop?.Shutdown(0);
    }

    /// <summary>When Yours Truly updates itself, the new version replaces the files this
    /// copy was started from. This notices and exits with an error, so launchd restarts
    /// it on the new version — otherwise the relay would go on running the old code
    /// until the Mac next restarted.</summary>
    private void WatchForUpdates()
    {
        var started = Running;
        var assembly = typeof(RelayMode).Assembly.Location;

        _updateWatch = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _updateWatch.Tick += (_, _) =>
        {
            Version? onDisk;
            try
            {
                onDisk = AssemblyName.GetAssemblyName(assembly).Version;
            }
            catch (Exception)
            {
                return; // mid-update, most likely; look again next time
            }

            if (onDisk is null || onDisk == started) return;

            Log.Record("relay.updated", Log.Details(("from", started?.ToString()), ("to", onDisk.ToString())));
            _desktop?.Shutdown(75);
        };
        _updateWatch.Start();
    }
}
