using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using YoursTruly.App;
using YoursTruly.App.ViewModels;
using YoursTruly.Data;
using YoursTruly.Messaging.Settings;
using Xunit.Abstractions;

namespace YoursTruly.Tests;

/// <summary>Drives the real window with rendering turned on and writes a picture of
/// each screen into .screenshots at the repository root.
///
/// This exists because a window that lays out wrongly still passes every assertion you
/// can write about it. Within minutes of the first capture it turned up a search box
/// overflowing off the left of the screen, an address column clipped at the card edge,
/// a header whose columns did not match the rows beneath it, and every subtitle centred
/// instead of sitting under its heading — none of which 258 tests had noticed.</summary>
public sealed class ScreenshotHarness(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder =
        Path.Combine(TestPaths.RepoRoot ?? Path.GetTempPath(), ".screenshots");

    private sealed class NoFiles : IFilePicker { public Task<string?> PickPdfAsync() => Task.FromResult<string?>(null); }
    private sealed class NoClipboard : IClipboardWriter { public Task CopyAsync(string t) => Task.CompletedTask; }

    [Fact]
    public Task Photograph_every_screen()
    {
        // The lambda has to return a value. Dispatch has three overloads, and an async
        // lambda with none has natural type Func<Task>, which binds to Func<TResult>
        // with TResult inferred as Task itself — that overload wraps the call in
        // Task.FromResult, so the inner task is handed back as an inert result nobody
        // awaits, and everything after the first await that genuinely yields is
        // silently abandoned. It cost a screenshot that stopped being taken and a test
        // that went on passing. Func<Task<int>> binds to the overload that forwards the
        // real task. The same trap is written up in UserInterfaceTests.InWindow.
        Func<Task<int>> shoot = async () =>
        {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        Directory.CreateDirectory(_folder);
        var services = AppServices.Start(
            Path.Combine(_folder, "contacts.db"), Path.Combine(_folder, "settings.json"));

        await SeedAsync(services);

        var store = new SettingsStore(services.SettingsPath);
        // An e-mail account too, so the Gmail quota bar is in the picture. It only
        // appears once there is an address to have a limit, and a panel nobody
        // photographs is a panel nobody notices is laid out wrongly — this one first
        // went out with its label and its number drawn on top of each other.
        store.Save(store.Load() with
        {
            Signature = "Jonathan Allen, Thunder FC",
            Email = new YoursTruly.Messaging.Settings.EmailSettings
            {
                Address = "jonathan@example.com",
                AppPassword = "xxxx xxxx xxxx xxxx",
                DisplayName = "Jonathan Allen",
            },
        });

        var window = new YoursTruly.App.Views.MainWindow(services);
        window.Width = 1400;
        window.Height = 900;
        window.Show();

        var model = (MainWindowViewModel)window.DataContext!;

        // Seeded people make the window open on Send; wait for that, then walk round.
        await window.FirstScreen;
        await model.ShowPeopleAsync(); Shoot(window, "2-people");
        await model.ShowSendAsync();
        ((SendViewModel)model.Current).Body = "Practice is this Friday, 6:30 at the field. Bring a ball if you can.";
        Shoot(window, "3-send");
        await model.ShowChangesAsync(); Shoot(window, "4-changes");
        await model.ShowHistoryAsync(); Shoot(window, "5-history");
        model.ShowSetup(); Shoot(window, "6-setup");

        await model.ShowPeopleAsync();
        var people = (PeopleViewModel)model.Current;
        people.Group = people.GroupOptions.Single(o => o.Name == "Activities committee");
        people.Rows[0].EditCommand.Execute(null);
        Shoot(window, "7-people-group");

        await model.ShowGroupsAsync(); Shoot(window, "9-groups");

        await model.ShowSendAsync();
        var send = (SendViewModel)model.Current;
        send.StartSavingGroupCommand.Execute(null);
        send.NewGroupName = "Coaches";
        Shoot(window, "8-send-save-group");

        // The import screen, which has no place on the rail: it is reached from the
        // People screen and hands the user straight back.
        await model.ShowPeopleAsync();
        var importing = new ImportViewModel(
            services, new NoFiles(), store, (_, _) => { }, () => Task.CompletedTask);
        await importing.LoadAsync(Roster(services));
        model.Current = importing;
        Shoot(window, "1-import");

        // And the same screen refusing a file it could not read. This one is worth a
        // picture of its own: it is a wall of warning text where there is normally a
        // table, and it is the screen nobody sees until something has gone wrong.
        var unreadable = Path.Combine(_folder, "unreadable.pdf");
        File.WriteAllBytes(unreadable, SyntheticRoster.Nameless());
        var refusing = new ImportViewModel(
            services, new NoFiles(), store, (_, _) => { }, () => Task.CompletedTask);
        await refusing.LoadAsync(unreadable);
        model.Current = refusing;
        Shoot(window, "1b-import-unreadable");

        // What somebody sees on a fresh install, before there is any list at all.
        var nothing = new NoListViewModel(
            import: () => Task.CompletedTask, openExisting: () => Task.CompletedTask,
            startEmpty: () => Task.CompletedTask, openSaved: _ => Task.CompletedTask)
        {
            Known = [new YoursTruly.Messaging.Settings.SavedList(
                "Soccer team", "/Users/you/Documents/soccer-team.db")],
        };
        model.Current = nothing;
        Shoot(window, "0-no-list");

        var shots = Directory.GetFiles(_folder, "*.png");
        output.WriteLine($"FOLDER {_folder} — {shots.Length} images");
        Assert.Equal(11, shots.Length);
        Assert.All(shots, f => Assert.True(new FileInfo(f).Length > 5000, $"{f} is suspiciously small"));
            return 0;
        };
        return HeadlessApp.Session.Value.Dispatch(shoot, CancellationToken.None);
    }

    /// <summary>No Task.Delay here: the headless dispatcher does not pump timers, so a
    /// delay never resumes and the whole run stops silently. A render tick is the
    /// supported way to make a frame happen.</summary>
    private void Shoot(Window window, string name)
    {
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException($"nothing rendered for {name}");
        var path = Path.Combine(_folder, $"{name}.png");
        frame.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        output.WriteLine($"SHOT {path} {new FileInfo(path).Length} bytes");
    }

    /// <summary>A file to photograph the import screen against, written next to the
    /// screenshots rather than committed.</summary>
    private string Roster(AppServices services)
    {
        var path = Path.Combine(_folder, "spring-roster.pdf");
        File.WriteAllBytes(path, SyntheticRoster.Table());
        return path;
    }

    private static async Task SeedAsync(AppServices services)
    {
        await using var db = services.Db();
        var directory = new DirectoryService(db);
        var people = new[]
        {
            ("Ashgrove", "Adelaide", "a.ashgrove@example.com", "(435) 555-0111"),
            ("Bellweather", "Horatio", "", "(435) 555-0127"),
            ("Carrowmore", "Ophelia", "o.carrowmore@example.com", "(435) 555-0133"),
            ("Denholm", "Verity", "v.denholm@example.com", "(435) 555-0140"),
            ("Eastleigh", "Cordelia", "", "(435) 555-0152"),
            ("Fairbourne", "Jemima", "j.fairbourne@example.com", ""),
            ("Glenhaven", "Percival", "p.glenhaven@example.com", "(435) 555-0160"),
            ("Harrowfield", "Nathaniel", "", "(435) 555-0166"),
        };
        var ids = new List<Guid>();
        foreach (var (last, first, email, phone) in people)
            ids.Add(await directory.AddPersonAsync(last, first, email, phone, null, AppServices.Today));

        // A couple of people with more than one channel ticked, so the chips in the
        // photograph show what several at once actually looks like.
        await directory.SetPreferredChannelsAsync(ids[0],
            YoursTruly.Core.Domain.ChannelSet.Of(
                YoursTruly.Core.Domain.Channel.Email, YoursTruly.Core.Domain.Channel.Text));
        await directory.SetPreferredChannelsAsync(ids[3],
            YoursTruly.Core.Domain.ChannelSet.Of(YoursTruly.Core.Domain.Channel.Text));

        await new GroupService(db).SaveMembersAsync("Activities committee", [ids[0], ids[3], ids[6]]);

        // A couple of corrections, so the changes-since-import screen has rows in it.
        // Everybody here was added by hand, so every detail is one a file does not have.
        await directory.UpdateDetailsAsync(
            ids[1], "h.bellweather@example.com", null, null, AppServices.Today);
    }

    public void Dispose() { }
}
