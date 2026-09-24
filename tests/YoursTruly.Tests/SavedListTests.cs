using YoursTruly.App;
using YoursTruly.App.ViewModels;
using YoursTruly.Messaging.Settings;

namespace YoursTruly.Tests;

/// <summary>Several lists of people, one file each — a soccer team, a class, a street.
/// The settings remember where they all are, because a list cannot be the thing that
/// remembers where the other lists are.</summary>
public sealed class SavedListTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"yourstruly-lists-{Guid.NewGuid():N}");

    private string Path2(string name) => Path.Combine(_folder, name);

    private SettingsStore Store() => new(Path2("settings.json"));

    [Theory]
    [InlineData("soccer-team.db", "Soccer team")]
    [InlineData("primary_class.db", "Primary class")]
    [InlineData("contacts.db", "Contacts")]
    public void A_file_nobody_has_named_is_called_after_its_file_name(string file, string expected) =>
        Assert.Equal(expected, SavedList.NameFor(Path2(file)));

    [Fact]
    public void Opening_a_list_puts_it_in_the_switcher_and_names_it()
    {
        var services = AppServices.Start(Path2("soccer-team.db"), Path2("settings.json"));
        var store = Store();
        _ = new SetupViewModel(services, store, isMac: true);

        var saved = Assert.Single(store.Load().Lists);
        Assert.Equal("Soccer team", saved.Name);
        Assert.Equal(Path2("soccer-team.db"), saved.Path);
    }

    [Fact]
    public async Task Starting_a_second_list_leaves_the_first_where_it_was()
    {
        var services = AppServices.Start(Path2("soccer-team.db"), Path2("settings.json"));
        var store = Store();
        var setup = new SetupViewModel(
            services, store, isMac: true, databases: new ChoosesFile(Path2("neighbours.db")));

        await setup.NewDatabaseCommand.ExecuteAsync(null);

        // The open one has moved, and both are remembered.
        Assert.Equal(Path2("neighbours.db"), services.DatabasePath);
        Assert.Equal(Path2("neighbours.db"), store.Load().DatabasePath);
        Assert.Equal(["Soccer team", "Neighbours"], store.Load().Lists.Select(l => l.Name));
        Assert.Contains("empty", setup.BackupStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_list_can_be_called_whatever_the_user_calls_it()
    {
        var services = AppServices.Start(Path2("contacts.db"), Path2("settings.json"));
        var store = Store();
        var setup = new SetupViewModel(services, store, isMac: true);

        setup.ListName = "Primary class";
        setup.SaveCommand.Execute(null);

        Assert.Equal("Primary class", Assert.Single(store.Load().Lists).Name);

        // And that is the name the rail's switcher shows.
        var options = ListChoice.Options(store.Load(), services.DatabasePath);
        Assert.Equal("Primary class", Assert.Single(options).Name);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Forgetting_a_list_takes_it_off_the_switcher_and_leaves_the_file_alone()
    {
        var services = AppServices.Start(Path2("soccer-team.db"), Path2("settings.json"));
        var store = Store();
        var setup = new SetupViewModel(
            services, store, isMac: true, databases: new ChoosesFile(Path2("neighbours.db")));
        await setup.NewDatabaseCommand.ExecuteAsync(null);

        var soccer = setup.OtherLists.Single();
        setup.ForgetCommand.Execute(soccer);

        Assert.Empty(setup.OtherLists);
        Assert.Equal(["Neighbours"], store.Load().Lists.Select(l => l.Name));

        // Forgetting where something is kept is not the same as deleting it.
        Assert.True(File.Exists(Path2("soccer-team.db")));
        Assert.Contains("untouched", setup.BackupStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void The_open_list_is_always_on_the_switcher_even_before_it_is_saved()
    {
        var options = ListChoice.Options(new AppSettings(), Path2("brand-new.db"));
        Assert.Equal("Brand new", Assert.Single(options).Name);
    }

    [Fact]
    public void Settings_read_back_off_disk_compare_equal_to_what_was_written()
    {
        // A record compares a list by reference, so without AppSettings writing its
        // own Equals, Setup would report unsaved changes the moment it opened.
        var store = Store();
        var written = new AppSettings
        {
            Lists = [new SavedList("Soccer team", Path2("soccer-team.db"))],
            Signature = "Jonathan Allen",
        };
        store.Save(written);

        Assert.Equal(written, store.Load());
    }

    private sealed class ChoosesFile(string path) : IDatabasePicker
    {
        public Task<string?> PickExistingAsync() => Task.FromResult<string?>(path);
        public Task<string?> PickNewAsync() => Task.FromResult<string?>(path);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
        catch (IOException) { /* a temp folder left behind harms nothing */ }
    }
}
