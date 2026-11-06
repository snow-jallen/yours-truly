using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using YoursTruly.App.Views;

namespace YoursTruly.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime relay && Relay.RelayMode.Current is { } mode)
        {
            mode.Attach(relay);
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = AppServices.Start();
            Looks.Apply(new Messaging.Settings.SettingsStore(services.SettingsPath).Load().Appearance);
            var window = new MainWindow(services);
            desktop.MainWindow = window;

            // Deliberately not awaited: the window opens now, and the rail grows a
            // restart button later if there turns out to be anything to restart into.
            window.StartUpdateCheck();

            // If this Mac texts for the user's other computers, make sure that is still
            // going — a reinstall quits it, and opening the app is the natural moment to
            // put that right. Off the UI thread: it starts a process.
            if (OperatingSystem.IsMacOS())
                _ = System.Threading.Tasks.Task.Run(() => new Relay.LaunchAgentRelay().EnsureRunning());
        }

        base.OnFrameworkInitializationCompleted();
    }
}
