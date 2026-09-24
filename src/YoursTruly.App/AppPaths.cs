namespace YoursTruly.App;

/// <summary>Where the app keeps its own things, as opposed to where the user keeps
/// theirs.
///
/// Settings and logs are the app's business: a credentials file and a diagnostic log
/// are not documents, and putting them in Documents means the user tidies them away or
/// syncs a Twilio token to a cloud drive. Lists of people are the user's business, and
/// the app has no opinion at all about where those go — it asks, on the import screen.
/// </summary>
public static class AppPaths
{
    private const string FolderName = "Yours Truly";

    /// <summary>The per-user application-data folder, in the place each platform means
    /// by that.
    ///
    /// Not <c>SpecialFolder.ApplicationData</c> on a Mac: .NET maps that to
    /// <c>~/.config</c>, which is a Unix convention rather than a Mac one, and a Mac
    /// user's settings belong in Application Support where their backup software will
    /// find them.</summary>
    public static string Settings =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", FolderName)
            : OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName)
                : Path.Combine(
                    Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                        ? xdg
                        : Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
                    "yours-truly");

    public static string SettingsFile => Path.Combine(Settings, "settings.json");

    public static string Logs => Path.Combine(Settings, "logs");

    /// <summary>Where the file picker starts when somebody makes a new list. A
    /// suggestion and nothing more — a list is the user's document, and it goes
    /// wherever they say.</summary>
    public static string SuggestedForNewLists =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
}
