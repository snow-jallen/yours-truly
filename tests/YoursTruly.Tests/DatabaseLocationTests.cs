using YoursTruly.App;
using YoursTruly.App.ViewModels;
using YoursTruly.Core.Import;
using YoursTruly.Data;
using YoursTruly.Messaging.Settings;
using Microsoft.EntityFrameworkCore;

namespace YoursTruly.Tests;

public sealed class DatabaseLocationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"yourstruly-db-{Guid.NewGuid():N}");
    private readonly string _settingsPath;
    private readonly SettingsStore _store;

    public DatabaseLocationTests()
    {
        Directory.CreateDirectory(_folder);
        _settingsPath = Path.Combine(_folder, "settings.json");
        _store = new SettingsStore(_settingsPath);
    }

    private string Path2(string name) => Path.Combine(_folder, name);

    private sealed class ChoosesFile(string? existing = null, string? created = null) : IDatabasePicker
    {
        public Task<string?> PickExistingAsync() => Task.FromResult(existing);
        public Task<string?> PickNewAsync() => Task.FromResult(created);
    }

    [Fact]
    public void The_list_opens_where_the_settings_say()
    {
        // Made first: the settings record where a list is, not an instruction to make
        // one there.
        var elsewhere = Path2("elsewhere.db");
        AppServices.Start(elsewhere, _settingsPath);
        _store.Save(_store.Load() with { DatabasePath = elsewhere });

        var services = AppServices.Start(settingsPath: _settingsPath);

        Assert.Equal(elsewhere, services.DatabasePath);
        Assert.True(services.HasList);
    }

    [Fact]
    public void A_list_that_is_not_there_any_more_opens_nothing()
    {
        // The file has been moved, renamed, or is on a drive that is not plugged in.
        // Making an empty one over the place where somebody's directory used to be is
        // the worst thing the app could do here, so it opens nothing and says so.
        var gone = Path2("gone.db");
        _store.Save(_store.Load() with { DatabasePath = gone });

        var services = AppServices.Start(settingsPath: _settingsPath);

        Assert.Null(services.DatabasePath);
        Assert.False(services.HasList);
        Assert.False(File.Exists(gone));
    }

    [Fact]
    public void A_fresh_install_has_no_list_and_has_written_nothing()
    {
        var services = AppServices.Start(settingsPath: _settingsPath);

        Assert.False(services.HasList);
        Assert.Throws<InvalidOperationException>(() => services.Db());

        // Settings and logs are the app's own; a list is not, and none exists yet.
        Assert.Empty(Directory.GetFiles(_folder, "*.db"));
    }

    [Fact]
    public void Settings_stay_put_when_the_database_moves()
    {
        var services = AppServices.Start(Path2("a.db"), _settingsPath);
        services.SwitchTo(Path2("b.db"));

        // The settings record where the database is, so they cannot live beside it.
        Assert.Equal(_settingsPath, services.SettingsPath);
    }

    [Fact]
    public async Task Opening_another_file_shows_that_file_rather_than_the_old_one()
    {
        var first = AppServices.Start(Path2("first.db"), _settingsPath);
        await using (var db = first.Db())
        {
            await new DirectoryService(db).AddPersonAsync(
                "Winslade", "Verity", null, "555-0150", null, AppServices.Today);
        }

        var second = Path2("second.db");
        var setup = new SetupViewModel(first, _store, isMac: true, databases: new ChoosesFile(created: second));
        await setup.NewDatabaseCommand.ExecuteAsync(null);

        Assert.Equal(second, first.DatabasePath);
        Assert.Equal(second, _store.Load().DatabasePath);
        Assert.Contains("empty", setup.BackupStatus, StringComparison.Ordinal);

        // The new one is empty, and the old one is untouched.
        await using (var db = first.Db())
            Assert.Empty(await new DirectoryService(db).RecipientsAsync());

        await using (var old = AppDatabase.Open(Path2("first.db")))
            Assert.Single(await new DirectoryService(old).RecipientsAsync());
    }

    [Fact]
    public async Task Somebody_elses_database_is_refused_before_it_becomes_the_open_one()
    {
        var services = AppServices.Start(Path2("good.db"), _settingsPath);

        // A SQLite file that is not a the app directory.
        var foreign = Path2("foreign.db");
        await using (var other = new SqliteJunk(foreign)) { }

        var setup = new SetupViewModel(services, _store, isMac: true, databases: new ChoosesFile(existing: foreign));
        await setup.OpenDatabaseCommand.ExecuteAsync(null);

        Assert.Equal(Path2("good.db"), services.DatabasePath);
        Assert.Contains("nothing changed", setup.BackupStatus, StringComparison.Ordinal);
        Assert.NotEqual(foreign, _store.Load().DatabasePath);
    }

    [Fact]
    public async Task Choosing_the_file_already_open_says_so_and_does_nothing()
    {
        var services = AppServices.Start(Path2("same.db"), _settingsPath);
        var setup = new SetupViewModel(services, _store, isMac: true,
            databases: new ChoosesFile(existing: Path2("same.db")));

        await setup.OpenDatabaseCommand.ExecuteAsync(null);

        Assert.Contains("already open", setup.BackupStatus, StringComparison.Ordinal);
    }

    /// <summary>A SQLite database with a table the app knows nothing about.</summary>
    private sealed class SqliteJunk : IAsyncDisposable
    {
        public SqliteJunk(string path)
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE People (Nonsense TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }
}
