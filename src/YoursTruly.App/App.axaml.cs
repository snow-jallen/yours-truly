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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = AppServices.Start();
            Looks.Apply(new Messaging.Settings.SettingsStore(services.SettingsPath).Load().Appearance);
            var window = new MainWindow(services);
            desktop.MainWindow = window;

            // Deliberately not awaited: the window opens now, and the rail grows a
            // restart button later if there turns out to be anything to restart into.
            window.StartUpdateCheck();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
