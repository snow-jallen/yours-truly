using YoursTruly.Core.Diagnostics;
using YoursTruly.Data;
using YoursTruly.Messaging.Settings;
using Microsoft.EntityFrameworkCore;

namespace YoursTruly.App;

/// <summary>Everything the app needs to exist, built once at start-up. Small enough
/// that a container would be more machinery than it saves.</summary>
public sealed class AppServices
{
    /// <summary>Which list is open, or null when none is. Null is the ordinary state on
    /// a fresh install: the app has no default list and no default place to keep one,
    /// so until somebody imports a file or starts a list by hand there is nothing open
    /// and nothing has been written anywhere.</summary>
    public string? DatabasePath { get; private set; }

    public bool HasList => DatabasePath is not null;

    /// <summary>Always in the same place, whatever the lists do — it is where their
    /// locations are recorded, so it cannot live beside any of them.</summary>
    public string SettingsPath { get; }

    /// <summary>Kept beside the settings rather than beside a list, so opening a
    /// different one does not scatter the log across folders.</summary>
    public JsonlActivityLog Activity { get; }

    private AppServices(string? databasePath, string settingsPath)
    {
        DatabasePath = databasePath;
        SettingsPath = settingsPath;
        Activity = new JsonlActivityLog(
            Path.Combine(Path.GetDirectoryName(settingsPath) ?? ".", "logs"));
    }

    /// <summary>Starts up, opening whichever list was open last if it is still there.
    ///
    /// The paths are only ever passed in by tests. A path given outright is opened and
    /// created if it does not exist — the caller is saying which list they mean. A path
    /// merely remembered in the settings is opened only if the file is still where it
    /// was: a list on a drive that is not plugged in, or one somebody moved, should
    /// leave the app open and asking rather than recreating an empty file over the
    /// place where their directory used to be.</summary>
    public static AppServices Start(string? databasePath = null, string? settingsPath = null)
    {
        var settings = settingsPath ?? AppPaths.SettingsFile;
        var remembered = Blank(new SettingsStore(settings).Load().DatabasePath);

        var chosen = databasePath
            ?? (remembered is not null && File.Exists(remembered) ? remembered : null);

        var services = new AppServices(chosen, settings);
        Log.UseForApp(services.Activity);

        if (chosen is null)
        {
            Log.Record("database.none", Log.Details(
                ("remembered", remembered is not null), ("missing", remembered is not null)));
            return services;
        }

        try
        {
            Prepare(chosen);
        }
        catch (Exception e)
        {
            Log.Failure("database.open", e, Log.Details(("path", Redact.Path(chosen))));
            throw;
        }

        Log.Record("database.open", Log.Details(
            ("path", Redact.Path(chosen)),
            ("chosen", databasePath is null ? "settings" : "given")));
        return services;
    }

    /// <summary>Opens a different list, making it if it is not there. Anything that
    /// cannot be opened as one is rejected before it becomes the open list, rather than
    /// after.</summary>
    public void SwitchTo(string path)
    {
        Prepare(path);
        DatabasePath = path;
        Log.Record("database.switch", Log.Details(("path", Redact.Path(path))));
    }

    private static void Prepare(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        using var db = AppDatabase.Open(path);
        db.Database.Migrate();
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>A context per unit of work. Desktop app, one user, no sharing.</summary>
    public AppDbContext Db() => AppDatabase.Open(
        DatabasePath ?? throw new InvalidOperationException(
            "No list is open. Every screen that reads one checks HasList first."));

    /// <summary>A context on a list other than the open one, made if it is not there
    /// yet. The import screen works against the list it is importing into, which is not
    /// necessarily — and on a first run is never — the one already open.</summary>
    public AppDbContext DbAt(string path)
    {
        Prepare(path);
        return AppDatabase.Open(path);
    }

    /// <summary>The day the user is living in. The server never decides this and
    /// neither does UTC.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}

/// <summary>Lets a view model ask for a file without knowing about windows.</summary>
public interface IFilePicker
{
    Task<string?> PickPdfAsync();
}

/// <summary>Choosing a directory file: an existing one, or somewhere to make a new one.</summary>
public interface IDatabasePicker
{
    Task<string?> PickExistingAsync();
    Task<string?> PickNewAsync();
}

/// <summary>Same again for the clipboard, which lives on the window.</summary>
public interface IClipboardWriter
{
    Task CopyAsync(string text);
}
