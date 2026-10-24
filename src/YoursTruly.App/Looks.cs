using Avalonia;
using Avalonia.Styling;
using YoursTruly.Messaging.Settings;

namespace YoursTruly.App;

/// <summary>Puts the chosen appearance on the whole app. Both palettes are in
/// Theme/Palette.axaml; this only says which one is in use.</summary>
public static class Looks
{
    public static void Apply(Appearance appearance)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = appearance switch
        {
            Appearance.Light => ThemeVariant.Light,
            Appearance.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
