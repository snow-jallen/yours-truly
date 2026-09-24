using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoursTruly.Core.Diagnostics;
using YoursTruly.Core.Domain;
using YoursTruly.Data;
using YoursTruly.Messaging.Settings;

namespace YoursTruly.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IFilePicker _picker;
    private readonly IClipboardWriter _clipboard;
    private readonly ISettingsStore _store;

    /// <summary>Shared with the Setup screen rather than one each: the updater holds
    /// the downloaded update in a field, so two of them would mean the rail fetches
    /// something the Setup screen's restart button knows nothing about.</summary>
    private readonly IUpdates _updates;

    private ImportViewModel _import = null!;
    private PeopleViewModel _people = null!;
    private GroupsViewModel _groups = null!;
    private SendViewModel _send = null!;
    private ChangesViewModel _changes = null!;
    private NoListViewModel _noList = null!;
    private HistoryViewModel _history = null!;
    private SetupViewModel _setup = null!;

    [ObservableProperty] private object _current;
    [ObservableProperty] private string _senderLabel;

    /// <summary>Whether anything has been typed in that the imported file does not
    /// carry. The rail only offers that screen when it has something on it: a permanent
    /// entry leading to "nothing has changed" is a place you learn to stop looking.</summary>
    [ObservableProperty] private bool _hasChanges;

    // --- which list is open ----------------------------------------------------------
    [ObservableProperty] private IReadOnlyList<ListChoice> _lists = [];
    [ObservableProperty] private string _listStatus = "";

    /// <summary>Set while a switch is being applied, so choosing a list does not set
    /// the property, which switches, which sets the property again.</summary>
    private bool _switching;

    [ObservableProperty] private ListChoice? _openList;

    /// <summary>The running version where a user can always see it, so "which version
    /// are you on?" is answered by looking rather than by hunting through Setup.</summary>
    public static string WindowTitle =>
        UpdateService.CurrentVersion is "unknown"
            ? "Yours Truly"
            : $"Yours Truly (v{UpdateService.CurrentVersion})";

    /// <summary>True once a newer version is downloaded and waiting. The rail shows a
    /// restart button only then, so the rest of the time it looks exactly as it did.</summary>
    [ObservableProperty] private bool _updateReady;

    /// <summary>What the update button last had to say. Composed here rather than in
    /// the rail, because views do not compute.</summary>
    [ObservableProperty] private string _updateCaption = "";

    [ObservableProperty] private bool _updateBusy;

    [ObservableProperty] private string _updateVersion = "";

    /// <summary>One button, two jobs: look for an update, and once one is waiting,
    /// install it. Naming the version on it means nobody has to go to Setup to find out
    /// what they are about to restart into.</summary>
    public string UpdateButton =>
        UpdateReady ? $"Restart & install v{UpdateVersion}" : "Check for updates";

    partial void OnUpdateReadyChanged(bool value) => OnPropertyChanged(nameof(UpdateButton));
    partial void OnUpdateVersionChanged(string value) => OnPropertyChanged(nameof(UpdateButton));

    /// <summary>The button on the rail. Unlike the quiet check at start-up, somebody
    /// asked, so every outcome is reported — including being run from a build folder,
    /// where there is nothing to replace.</summary>
    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (UpdateReady) { _updates.ApplyAndRestart(); return; }

        UpdateBusy = true;
        UpdateCaption = "Looking…";
        try
        {
            if (!_updates.Installed)
            {
                UpdateCaption = "Running from a build folder — nothing to update.";
                return;
            }

            var found = await _updates.CheckAsync();
            if (found.Version is null) { UpdateCaption = "You have the newest version."; return; }

            UpdateCaption = $"Downloading {found.Version}…";
            var ready = await _updates.DownloadAsync();
            UpdateVersion = ready.Version ?? found.Version;
            UpdateReady = ready.UpdateReady;
            UpdateCaption = ready.UpdateReady
                ? $"Version {UpdateVersion} is downloaded and waiting"
                : ready.Message;
        }
        catch (Exception failure)
        {
            Log.Failure("update.check", failure);
            UpdateCaption = "Could not check just now.";
        }
        finally { UpdateBusy = false; }
    }

    /// <summary>Looks for a newer version and fetches it in the background, leaving the
    /// user nothing to do but restart when it suits them.
    ///
    /// Silent from end to end. Nobody asked for this, so a copy run from a build
    /// folder, a machine that is offline, and a version that is already current all
    /// look the same from the rail: nothing appears. Setup's own button still reports
    /// every one of those out loud, because there the user did ask.</summary>
    public async Task CheckForUpdateAsync()
    {
        try
        {
            if (!_updates.Installed) return;

            var found = await _updates.CheckAsync();
            if (found.Version is null) return;

            var ready = await _updates.DownloadAsync();
            if (!ready.UpdateReady) return;

            UpdateVersion = ready.Version ?? found.Version;
            UpdateCaption = $"Version {UpdateVersion} is downloaded and waiting";
            UpdateReady = true;
        }
        catch (Exception failure)
        {
            Log.Failure("update.startup", failure);
        }
    }

    [RelayCommand]
    private void RestartToUpdate() => _updates.ApplyAndRestart();

    public MainWindowViewModel(
        AppServices services, IFilePicker picker, IClipboardWriter clipboard, IUpdates? updates = null)
    {
        _services = services;
        _picker = picker;
        _clipboard = clipboard;
        _updates = updates ?? new UpdateService(UpdateService.DefaultRepository);
        _store = new SettingsStore(services.SettingsPath);

        Build();

        _senderLabel = Label(_store.Load().Signature);
        _current = _services.HasList ? _people : _noList;
        RefreshLists();
    }

    /// <summary>Every screen holds a view of one list, so opening another means
    /// building them again rather than asking each to notice.</summary>
    private void Build()
    {
        _import = new ImportViewModel(_services, _picker, _store, FinishedImporting, ShowPeopleAsync);
        _people = new PeopleViewModel(_services, ShowImportAsync);
        _groups = new GroupsViewModel(_services, SendToGroupAsync);
        _send = new SendViewModel(_services, _store);
        _changes = new ChangesViewModel(_services, _clipboard);
        _history = new HistoryViewModel(_services, _clipboard);
        _noList = new NoListViewModel(
            import: ShowImportAsync,
            openExisting: OpenFromFileAsync,
            startEmpty: StartEmptyListAsync,
            openSaved: list => { OpenList = ListChoice.Of(list); return Task.CompletedTask; });
        _setup = new SetupViewModel(
            _services, _store,
            databases: _picker as IDatabasePicker,
            databaseChanged: OnDatabaseChanged,
            clipboard: _clipboard,
            updates: _updates);
    }

    private void OnDatabaseChanged()
    {
        var setup = _setup;
        Build();
        RefreshLists();

        // Keep the user on Setup, where they just pressed the button, and keep the
        // instance they are looking at so its message does not vanish.
        _setup = setup;
        Current = setup;
    }

    // --- switching between lists -----------------------------------------------------

    /// <summary>The lists on the rail, and which of them is open. Re-read from the
    /// settings rather than tracked, because Setup can add one too.</summary>
    private void RefreshLists()
    {
        _switching = true;
        var settings = _store.Load();
        Lists = ListChoice.Options(settings, _services.DatabasePath);
        OpenList = ListChoice.Find(Lists, _services.DatabasePath);
        _switching = false;
    }

    partial void OnOpenListChanged(ListChoice? value)
    {
        if (_switching || value is null) return;
        if (_services.DatabasePath is { } open && ListChoice.Same(value.Path, open)) return;
        Open(value);
    }

    /// <summary>Opens another list. A file that cannot be opened leaves the one in front
    /// of the user exactly as it was, and says so — finding out afterwards would mean
    /// the app is already pointed at it.</summary>
    private void Open(ListChoice chosen) => OpenPath(chosen.Path, chosen.Name);

    /// <summary>Opens a list and rebuilds every screen around it. Remembers it too, so
    /// it is one click away next time rather than another trip through a file picker.</summary>
    public void OpenPath(string path, string name)
    {
        try
        {
            _services.SwitchTo(path);
        }
        catch (Exception failure)
        {
            Log.Failure("list.open", failure, Log.Details(("path", Redact.Path(path))));
            ListStatus = $"“{name}” could not be opened, so nothing changed. {failure.Message}";
            _noList.Status = ListStatus;
            RefreshLists();
            return;
        }

        var settings = _store.Load();
        var known = settings.Lists.Any(l => ListChoice.Same(l.Path, path))
            ? settings.Lists
            : [.. settings.Lists, new SavedList(name, path)];
        _store.Save(settings with { DatabasePath = path, Lists = known });
        Log.Record("list.open", Log.Details(("path", Redact.Path(path))));

        Build();
        RefreshLists();
        ListStatus = "";
        _ = ShowPeopleAsync();
    }

    /// <summary>Where the window opens. People is the right first screen: it is where
    /// importing starts, and where somebody with a list already looks first.</summary>
    public async Task OpenFirstScreenAsync()
    {
        if (!_services.HasList) { ShowNoList(); return; }

        bool anyone;
        await using (var db = _services.Db())
            anyone = await new DirectoryService(db).AnyPeopleAsync();

        if (anyone) await ShowSendAsync();
        else await ShowPeopleAsync();
    }

    /// <summary>The list screens reload each time they are opened, so a channel chosen
    /// on one of them, or an import just applied, is reflected on the others without
    /// anything having to coordinate.</summary>
    public async Task ShowPeopleAsync()
    {
        Log.Record("screen.open", Log.Details(("screen", "people")));
        RefreshSender();
        if (ShowNoList()) return;
        Current = _people;
        await _people.LoadAsync();
        await RefreshChangesAsync();
    }

    /// <summary>Puts the "no list open" screen up instead, and says so. Every screen
    /// that reads a list goes through here, so none of them needs an empty state of its
    /// own and none of them can reach a database that is not there.</summary>
    /// <summary>Counts what is waiting on the changes screen, so the rail knows whether
    /// to offer it. Cheap, indexed, and run whenever a screen opens — an edit made on
    /// People has to show up on the rail without anything having to notify anything.</summary>
    private async Task RefreshChangesAsync()
    {
        if (!_services.HasList) { HasChanges = false; return; }
        try
        {
            await using var db = _services.Db();
            HasChanges = (await new DirectoryService(db).CorrectionsAsync()).Count > 0;
        }
        catch (Exception failure)
        {
            Log.Failure("changes.count", failure);
            HasChanges = false;
        }
    }

    private bool ShowNoList()
    {
        if (_services.HasList) return false;
        _noList.Known = _store.Load().Lists;
        Current = _noList;
        return true;
    }

    /// <summary>Opens a list from a file — one made on another computer, or the one
    /// that was open before somebody moved it.</summary>
    private async Task OpenFromFileAsync()
    {
        if (_picker is not IDatabasePicker databases) return;
        if (await databases.PickExistingAsync() is not { } path) return;
        OpenPath(path, SavedList.NameFor(path));
    }

    /// <summary>Starts a list with nobody in it, for typing people in by hand.</summary>
    private async Task StartEmptyListAsync()
    {
        if (_picker is not IDatabasePicker databases) return;
        if (await databases.PickNewAsync() is not { } path) return;
        OpenPath(path, SavedList.NameFor(path));
    }

    /// <summary>Importing has a screen but no place on the rail: it is reached from the
    /// button on People, does its one job, and hands the user straight back.</summary>
    public async Task ShowImportAsync()
    {
        Log.Record("screen.open", Log.Details(("screen", "import")));
        Current = _import;
        await _import.ChooseFileAsync();

        // Nothing chosen — they changed their mind at the file picker, so there is
        // nothing to show and nowhere to be but back on People.
        if (!_import.HasFile && !_import.Failed) await ShowPeopleAsync();
    }

    /// <summary>An import has landed. Whatever list it went into is now the open one —
    /// including when that list did not exist a minute ago — and the user is put back
    /// on People looking at the result.</summary>
    private void FinishedImporting(string path, string name)
    {
        var said = _import.Done;
        OpenPath(path, name);
        if (said.Length > 0) _people.EditStatus = said;
    }

    public async Task ShowGroupsAsync()
    {
        Log.Record("screen.open", Log.Details(("screen", "groups")));
        RefreshSender();
        if (ShowNoList()) return;
        Current = _groups;
        await _groups.LoadAsync();
        await RefreshChangesAsync();
    }

    /// <summary>From the Groups screen: Send, showing that group with everyone ticked.</summary>
    public async Task SendToGroupAsync(Guid groupId)
    {
        // Reached from a button inside a screen rather than from the rail, so the rail's
        // guard against a screen failing to open does not cover it; this does the same.
        try
        {
            await ShowSendAsync();
            _send.FocusGroup(groupId);
        }
        catch (Exception failure)
        {
            Log.Failure("screen.open", failure);
            Current = new ScreenFailedViewModel(failure.Message);
        }
    }

    public async Task ShowSendAsync()
    {
        Log.Record("screen.open", Log.Details(("screen", "send")));
        RefreshSender();
        if (ShowNoList()) return;
        Current = _send;
        await _send.LoadAsync();
        await RefreshChangesAsync();
    }

    public async Task ShowChangesAsync()
    {
        Log.Record("screen.open", Log.Details(("screen", "changes")));
        RefreshSender();
        if (ShowNoList()) return;
        Current = _changes;
        await _changes.LoadAsync();
        await RefreshChangesAsync();
    }

    public async Task ShowHistoryAsync()
    {
        Log.Record("screen.open", Log.Details(("screen", "history")));
        RefreshSender();
        if (ShowNoList()) return;
        Current = _history;
        await _history.LoadAsync();
        await RefreshChangesAsync();
    }

    public void ShowSetup()
    {
        Log.Record("screen.open", Log.Details(("screen", "setup")));
        Current = _setup;
    }

    /// <summary>The rail names whoever the messages come from, which is a person rather
    /// than an organisation. Re-read on every move between screens, so a signature
    /// typed into Setup shows up without anything having to notify anything.</summary>
    private void RefreshSender()
    {
        SenderLabel = Label(_store.Load().Signature);
        RefreshLists();
    }

    private static string Label(string? signature) =>
        Signature.SenderName(signature) is { Length: > 0 } name
            ? name.ToUpperInvariant()
            : "SET UP WHO THIS IS FROM";
}

/// <summary>Shown when a screen cannot be opened, in place of the screen.</summary>
public sealed class ScreenFailedViewModel(string message)
{
    public string Message { get; } = message;
}
