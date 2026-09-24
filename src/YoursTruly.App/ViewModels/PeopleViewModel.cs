using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoursTruly.Core.Diagnostics;
using YoursTruly.Core.Domain;
using YoursTruly.Data;

namespace YoursTruly.App.ViewModels;

/// <summary>One person in the list. Holds its own commands so a row can change a
/// channel without every template reaching back up through the tree.</summary>
public sealed partial class PersonRow : ObservableObject
{
    private readonly Func<Guid, ChannelSet, Task> _choose;
    private readonly Action<PersonRow> _edit;

    public PersonRow(Recipient person, Func<Guid, ChannelSet, Task> choose, Action<PersonRow> edit)
    {
        Person = person;
        _choose = choose;
        _edit = edit;
        _channels = person.PreferredChannels;
    }

    [RelayCommand] private void Edit() => _edit(this);

    public Recipient Person { get; }
    public Guid Id => Person.Id;
    public string Name => Person.SortName;
    public string Email => Person.Email ?? "—";
    public string Phone => Person.Phone is null ? "—" : Person.PhoneForDisplay;
    public string Age => Person.Age?.ToString() ?? "—";
    public string Note => Person.Notes ?? "";
    public bool HasNote => Person.HasNote;
    public string Birthday => Person.BirthMonth is int m && Person.BirthDay is int d
        ? $"{d} {Months[m - 1]}" : "—";

    /// <summary>Every channel this person has asked for. Several may be on at once, and
    /// each one ticked is a message that will actually go out.</summary>
    [ObservableProperty] private ChannelSet _channels;

    public bool IsEmail => Channels.Has(Channel.Email);
    public bool IsText => Channels.Has(Channel.Text);
    public bool IsVoice => Channels.Has(Channel.Voice);

    partial void OnChannelsChanged(ChannelSet value)
    {
        OnPropertyChanged(nameof(IsEmail));
        OnPropertyChanged(nameof(IsText));
        OnPropertyChanged(nameof(IsVoice));
    }

    [RelayCommand] private Task ChooseEmail() => Toggle(Channel.Email);
    [RelayCommand] private Task ChooseText() => Toggle(Channel.Text);
    [RelayCommand] private Task ChooseVoice() => Toggle(Channel.Voice);

    /// <summary>Each chip is on or off on its own, so somebody can have a text and an
    /// e-mail. Clicking one already on turns it off, which is how you get back to
    /// having chosen nothing without a button for it.</summary>
    private async Task Toggle(Channel channel)
    {
        Channels = Channels.Toggle(channel);
        await _choose(Id, Channels);
    }

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
}

/// <summary>One group in the person editor, ticked when this person is in it.</summary>
public sealed partial class GroupTick(Guid id, string name, bool isMember) : ObservableObject
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    [ObservableProperty] private bool _isMember = isMember;
}

/// <summary>Correcting what the app holds for one person. Saving does not overwrite
/// what the imported file said — it records the correction, which is what keeps it
/// through the next import and what puts it on the Changes since import list.</summary>
public sealed partial class PersonEditor : ObservableObject
{
    private readonly Func<PersonEditor, Task> _save;
    private readonly Action _close;

    /// <summary>Correcting somebody already in the directory.</summary>
    public PersonEditor(
        Recipient person, IEnumerable<GroupSummary> groups, Func<PersonEditor, Task> save, Action close)
    {
        _save = save;
        _close = close;
        Groups = [.. groups.Select(g => new GroupTick(g.Id, g.Name, person.Groups.Contains(g.Id)))];
        Id = person.Id;
        Name = person.SortName;
        _lastName = person.LastName;
        _firstName = person.FirstName;
        _email = person.Email ?? "";
        _phone = person.PhoneForDisplay;
        _notes = person.Notes ?? "";
    }

    /// <summary>Adding somebody no file carries.</summary>
    public PersonEditor(IEnumerable<GroupSummary> groups, Func<PersonEditor, Task> save, Action close)
    {
        _save = save;
        _close = close;
        Groups = [.. groups.Select(g => new GroupTick(g.Id, g.Name, false))];
        IsNew = true;
        Name = "Someone new";
    }

    public Guid Id { get; }
    public string Name { get; }
    public bool IsNew { get; }

    public IReadOnlyList<GroupTick> Groups { get; }
    public bool HasGroups => Groups.Count > 0;

    /// <summary>Optional: a group to start with this person in it.</summary>
    [ObservableProperty] private string _newGroup = "";

    public IReadOnlySet<Guid> TickedGroups => Groups.Where(g => g.IsMember).Select(g => g.Id).ToHashSet();

    [ObservableProperty] private string _lastName = "";
    [ObservableProperty] private string _firstName = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;

    [RelayCommand]
    private async Task Save()
    {
        if (IsNew && LastName.Trim().Length == 0)
        {
            Status = "A surname is needed, so there is something to sort them by.";
            return;
        }

        Busy = true;
        try { await _save(this); }
        catch (Exception e) { Status = $"Not saved. {e.Message}"; }
        finally { Busy = false; }
    }

    [RelayCommand] private void Cancel() => _close();
}

public sealed partial class PeopleViewModel(AppServices services, Func<Task>? importFile = null)
    : ObservableObject
{
    private IReadOnlyList<Recipient> _all = [];
    private IReadOnlyList<GroupSummary> _groups = [];
    private AudienceSort _sort = AudienceSort.ByName;

    [ObservableProperty] private IReadOnlyList<GroupChoice> _groupOptions = [GroupChoice.Any];
    [ObservableProperty] private GroupChoice? _group = GroupChoice.Any;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private PersonEditor? _editing;
    [ObservableProperty] private string _editStatus = "";

    public bool IsEditing => Editing is not null;

    partial void OnEditingChanged(PersonEditor? value) => OnPropertyChanged(nameof(IsEditing));

    public ObservableCollection<PersonRow> Rows { get; } = [];

    /// <summary>Importing lives here rather than on the rail: it is something done to
    /// the list of people, not a place to go.</summary>
    public bool CanImport => importFile is not null;

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (importFile is not null) await importFile();
    }

    public async Task LoadAsync()
    {
        await using var db = services.Db();
        _all = await new DirectoryService(db).RecipientsAsync();
        _groups = await new GroupService(db).ListAsync();
        var options = GroupChoice.Options(_groups);
        var keep = GroupChoice.Find(options, Group);
        GroupOptions = options;
        Group = keep;
        Loaded = true;
        Refresh();
    }

    partial void OnSearchChanged(string value) => Refresh();

    partial void OnGroupChanged(GroupChoice? value) => Refresh();

    [RelayCommand]
    private void SortBy(string key)
    {
        var chosen = key switch
        {
            "age" => AudienceSortKey.Age,
            "birthday" => AudienceSortKey.Birthday,
            "channel" => AudienceSortKey.Channel,
            _ => AudienceSortKey.Name,
        };
        _sort = _sort.Key == chosen ? _sort.Reversed() : new AudienceSort(chosen);
        Refresh();
    }

    private void Refresh()
    {
        if (!Loaded) return;

        var filter = new AudienceFilter
        {
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search,
            Group = Group?.Id,
            ActiveOnly = true,
        };

        var shown = Audience.Select(_all, filter, _sort);
        Rows.Clear();
        foreach (var person in shown) Rows.Add(new PersonRow(person, SaveChannelsAsync, StartEditing));

        var active = _all.Count(p => p.IsActive);
        Summary = shown.Count == active
            ? $"{active} people"
            : $"{shown.Count} of {active} people";
    }

    /// <summary>Somebody no file carries — a spouse, a visitor, anybody the list has
    /// not caught up with.</summary>
    [RelayCommand]
    private void AddPerson()
    {
        EditStatus = "";
        Editing = new PersonEditor(_groups, SaveDetailsAsync, () => Editing = null);
    }

    private void StartEditing(PersonRow row)
    {
        EditStatus = "";
        Editing = new PersonEditor(row.Person, _groups, SaveDetailsAsync, () => Editing = null);
    }

    private async Task SaveDetailsAsync(PersonEditor editor)
    {
        if (editor.IsNew)
        {
            await using (var adding = services.Db())
            {
                var id = await new DirectoryService(adding).AddPersonAsync(
                    editor.LastName, editor.FirstName,
                    editor.Email, editor.Phone, editor.Notes, AppServices.Today);
                await SaveGroupsAsync(adding, id, editor);
            }

            Log.Record("person.add", Log.Details(
                ("gaveEmail", editor.Email.Trim().Length > 0),
                ("gavePhone", editor.Phone.Trim().Length > 0)));
            EditStatus = $"Added {editor.LastName.Trim()}. They are not in any imported file, so no import will remove them.";
            Editing = null;
            await LoadAsync();
            return;
        }

        int added;
        await using (var db = services.Db())
        {
            added = await new DirectoryService(db).UpdateDetailsAsync(
                editor.Id, editor.Email, editor.Phone, editor.Notes, AppServices.Today);
            await SaveGroupsAsync(db, editor.Id, editor);
        }

        Log.Record("person.edit", Log.Details(("person", editor.Id), ("newDetails", added)));
        EditStatus = added switch
        {
            0 => $"Saved {editor.Name}.",
            1 => $"Saved. One detail for {editor.Name} that your file does not have — it is on Changes since import.",
            _ => $"Saved. {added} details for {editor.Name} that your file does not have — they are on Changes since import.",
        };

        Editing = null;
        await LoadAsync();
    }

    private static async Task SaveGroupsAsync(AppDbContext db, Guid personId, PersonEditor editor)
    {
        var groups = new GroupService(db);
        await groups.SetGroupsForPersonAsync(personId, editor.TickedGroups);
        if (GroupName.Clean(editor.NewGroup) is { } name)
            await groups.SaveMembersAsync(name, [personId]);

        Log.Record("person.groups", Log.Details(
            ("person", personId), ("groups", editor.TickedGroups.Count), ("newGroup", GroupName.Clean(editor.NewGroup) is not null)));
    }

    private async Task SaveChannelsAsync(Guid personId, ChannelSet channels)
    {
        Log.Record("person.channels", Log.Details(("person", personId), ("channels", channels.ToWire())));
        await using var db = services.Db();
        await new DirectoryService(db).SetPreferredChannelsAsync(personId, channels);

        // Keep the copy in memory in step, so re-filtering does not undo the click.
        _all = [.. _all.Select(p => p.Id == personId ? p with { PreferredChannels = channels } : p)];
    }
}
