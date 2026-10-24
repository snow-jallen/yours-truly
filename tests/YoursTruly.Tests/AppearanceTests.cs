using YoursTruly.App;
using YoursTruly.App.ViewModels;
using YoursTruly.Messaging.Settings;

namespace YoursTruly.Tests;

/// <summary>Light, dark, or whatever the computer says. A choice somebody can see take
/// effect, so it is kept the moment it is made rather than waiting on Save.</summary>
public sealed class AppearanceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"yourstruly-looks-{Guid.NewGuid():N}");

    private string In(string name) => Path.Combine(_folder, name);

    [Fact]
    public void A_fresh_install_follows_the_computer() =>
        Assert.Equal(Appearance.System, new SettingsStore(In("settings.json")).Load().Appearance);

    [Fact]
    public void Choosing_dark_applies_it_and_keeps_it_without_saving_anything_else()
    {
        var store = new SettingsStore(In("settings.json"));
        var applied = new List<Appearance>();
        var setup = new SetupViewModel(
            AppServices.Start(In("contacts.db"), store.Path), store, isMac: true,
            applyAppearance: applied.Add);

        setup.Signature = "Jonathan Allen";
        setup.LooksDark = true;

        Assert.Equal([Appearance.Dark], applied);
        Assert.Equal(Appearance.Dark, store.Load().Appearance);
        // The signature was typed but not saved, and still is not.
        Assert.Equal("", store.Load().Signature);
        Assert.True(setup.IsDirty);
        Assert.False(setup.LooksLikeComputer);
    }

    [Fact]
    public void The_choice_is_stored_by_name()
    {
        var store = new SettingsStore(In("settings.json"));
        store.Save(new AppSettings { Appearance = Appearance.Light });

        Assert.Contains("\"Light\"", File.ReadAllText(store.Path), StringComparison.Ordinal);
        Assert.Equal(Appearance.Light, store.Load().Appearance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
