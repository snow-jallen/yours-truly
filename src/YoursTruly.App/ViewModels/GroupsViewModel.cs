using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoursTruly.Core.Diagnostics;
using YoursTruly.Core.Domain;
using YoursTruly.Data;

namespace YoursTruly.App.ViewModels;

/// <summary>A group in the list down the left.</summary>
public sealed partial class GroupRow(GroupSummary group, Action<GroupRow> open) : ObservableObject
{
    public GroupSummary Group { get; } = group;
    public Guid Id => Group.Id;
    public string Name => Group.Name;
    public string Count => Group.Members == 1 ? "1 person" : $"{Group.Members} people";

    [ObservableProperty] private bool _isSelected;

    [RelayCommand] private void Open() => open(this);
}

/// <summary>Someone in the chosen group, or someone who could be added to it.</summary>
public sealed partial class GroupPersonRow(Recipient person, Func<GroupPersonRow, Task> act) : ObservableObject
{
    public Recipient Person { get; } = person;
    public Guid Id => Person.Id;
    public string Name => Person.SortName;
    /// <summary>Enough to tell two people of the same name apart, and the only thing
    /// every list has: how to reach them.</summary>
    public string Detail =>
        Person.Email ?? (Person.Phone is null ? "no email or phone" : Person.PhoneForDisplay);

    /// <summary>Kept in the group, but a message would not reach them — said here so
    /// the count on the Send button is no surprise.</summary>
    public bool HasLeft => !Person.IsActive;

    [RelayCommand] private Task Act() => act(this);
}

/// <summary>Making, naming, filling and emptying groups, and sending to one.</summary>
public sealed partial class GroupsViewModel(AppServices services, Func<Guid, Task> sendToGroup) : ObservableObject
{
    private const int MostCandidates = 50;

    private IReadOnlyList<Recipient> _all = [];

    public ObservableCollection<GroupRow> Groups { get; } = [];
    public ObservableCollection<GroupPersonRow> Members { get; } = [];
    public ObservableCollection<GroupPersonRow> Candidates { get; } = [];

    [ObservableProperty] private GroupRow? _selected;
    [ObservableProperty] private string _newGroupName = "";
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _addSearch = "";
    [ObservableProperty] private string _candidateLine = "";
    [ObservableProperty] private string _memberLine = "";
    [ObservableProperty] private bool _confirmingDelete;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _loaded;

    public bool HasSelection => Selected is not null;
    public bool NoGroups => Loaded && Groups.Count == 0;
    public bool NothingChosen => Loaded && Groups.Count > 0 && Selected is null;
    public bool AnyCandidates => Candidates.Count > 0;
    public string DeleteLine => $"Delete “{Selected?.Name}”? The people in it stay in the directory; only the group goes.";

    partial void OnSelectedChanged(GroupRow? value)
    {
        foreach (var row in Groups) row.IsSelected = row == value;
        EditName = value?.Name ?? "";
        AddSearch = "";
        ConfirmingDelete = false;
        foreach (var name in (string[])[nameof(HasSelection), nameof(NothingChosen), nameof(DeleteLine)])
            OnPropertyChanged(name);
        ShowPeople();
    }

    partial void OnLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(NoGroups));
        OnPropertyChanged(nameof(NothingChosen));
    }

    partial void OnAddSearchChanged(string value) => ShowCandidates();

    /// <summary>Reloads everything, keeping the same group chosen when it still exists.</summary>
    public async Task LoadAsync(Guid? select = null)
    {
        await using var db = services.Db();
        _all = await new DirectoryService(db).RecipientsAsync();
        var groups = await new GroupService(db).ListAsync();

        var keep = select ?? Selected?.Id;
        Groups.Clear();
        foreach (var g in groups) Groups.Add(new GroupRow(g, row => Selected = row));

        Loaded = true;
        OnLoadedChanged(true);
        Selected = Groups.FirstOrDefault(g => g.Id == keep) ?? Groups.FirstOrDefault();
        if (Selected is null) ShowPeople();
    }

    private void ShowPeople()
    {
        Members.Clear();
        if (Selected is { } group)
        {
            var members = _all.Where(p => p.Groups.Contains(group.Id))
                .OrderBy(p => p.SortName, StringComparer.OrdinalIgnoreCase);
            foreach (var person in members) Members.Add(new GroupPersonRow(person, RemoveAsync));
        }

        var left = Members.Count(m => m.HasLeft);
        MemberLine = Members.Count == 0
            ? "Nobody is in this group yet. Find people below and add them."
            : left == 0
                ? Plural(Members.Count)
                : $"{Plural(Members.Count)}. {left} no longer in the directory, so a message would skip them.";
        ShowCandidates();
    }

    /// <summary>People not already in the group, narrowed by the search box. Capped, so
    /// an empty search does not lay out the whole directory; "Add all shown" adds
    /// exactly what is on screen and nothing hidden below it.</summary>
    private void ShowCandidates()
    {
        Candidates.Clear();
        if (Selected is { } group)
        {
            var filter = new AudienceFilter { Search = string.IsNullOrWhiteSpace(AddSearch) ? null : AddSearch };
            var matching = Audience.Select(_all, filter).Where(p => !p.Groups.Contains(group.Id)).ToList();
            foreach (var person in matching.Take(MostCandidates)) Candidates.Add(new GroupPersonRow(person, AddAsync));

            CandidateLine = matching.Count == 0
                ? (string.IsNullOrWhiteSpace(AddSearch) ? "Everyone in the directory is already in this group." : "Nobody else matches that.")
                : matching.Count > MostCandidates
                    ? $"Showing {MostCandidates} of {matching.Count}. Search to narrow it down."
                    : $"{Plural(matching.Count)} not in this group{(string.IsNullOrWhiteSpace(AddSearch) ? "" : " match")}.";
        }
        OnPropertyChanged(nameof(AnyCandidates));
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        var name = GroupName.Clean(NewGroupName);
        if (name is null) { Status = "Give the new group a name first, such as “Activities committee”."; return; }

        GroupSaved saved;
        await using (var db = services.Db())
            saved = await new GroupService(db).SaveMembersAsync(name, []);
        Log.Record("group.create", Log.Details(("group", saved.Group.Id), ("created", saved.Created)));

        NewGroupName = "";
        Status = saved.Created
            ? $"Made “{saved.Group.Name}”. Now add the people who belong in it."
            : $"There is already a group called “{saved.Group.Name}” — here it is.";
        await LoadAsync(saved.Group.Id);
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (Selected is not { } group) return;
        if (GroupName.Clean(EditName) == group.Name) return;

        GroupNameProblem problem;
        await using (var db = services.Db())
            problem = await new GroupService(db).RenameAsync(group.Id, EditName);
        Log.Record("group.rename", Log.Details(("group", group.Id), ("problem", problem.ToString())));

        Status = problem switch
        {
            GroupNameProblem.Blank => "A group needs a name.",
            GroupNameProblem.Taken => $"There is already a group called “{GroupName.Clean(EditName)}”. Choose another name.",
            _ => $"Renamed to “{GroupName.Clean(EditName)}”.",
        };
        if (problem is GroupNameProblem.None) await LoadAsync(group.Id);
    }

    private async Task AddAsync(GroupPersonRow row)
    {
        if (Selected is not { } group) return;
        await using (var db = services.Db())
            await new GroupService(db).AddMembersAsync(group.Id, [row.Id]);
        Log.Record("group.add", Log.Details(("group", group.Id), ("added", 1)));
        Status = $"Added {row.Person.FullName}.";
        await LoadAsync(group.Id);
    }

    [RelayCommand]
    private async Task AddAllShownAsync()
    {
        if (Selected is not { } group || Candidates.Count == 0) return;
        int added;
        await using (var db = services.Db())
            added = await new GroupService(db).AddMembersAsync(group.Id, Candidates.Select(c => c.Id));
        Log.Record("group.add", Log.Details(("group", group.Id), ("added", added)));
        Status = $"Added {Plural(added)}.";
        AddSearch = "";
        await LoadAsync(group.Id);
    }

    private async Task RemoveAsync(GroupPersonRow row)
    {
        if (Selected is not { } group) return;
        await using (var db = services.Db())
            await new GroupService(db).RemoveMembersAsync(group.Id, [row.Id]);
        Log.Record("group.remove", Log.Details(("group", group.Id), ("removed", 1)));
        Status = $"Took {row.Person.FullName} out of “{group.Name}”. They are still in the directory.";
        await LoadAsync(group.Id);
    }

    [RelayCommand]
    private void StartDeleting() => ConfirmingDelete = true;

    [RelayCommand]
    private void KeepGroup() => ConfirmingDelete = false;

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is not { } group) return;
        await using (var db = services.Db())
            await new GroupService(db).DeleteAsync(group.Id);
        Log.Record("group.delete", Log.Details(("group", group.Id)));

        Status = $"Deleted “{group.Name}”. Nobody was removed from the directory.";
        Selected = null;
        await LoadAsync();
    }

    /// <summary>Opens the Send screen showing this group, everyone in it ticked.</summary>
    [RelayCommand]
    private async Task SendAsync()
    {
        if (Selected is not { } group) return;
        Log.Record("group.send", Log.Details(("group", group.Id)));
        await sendToGroup(group.Id);
    }

    private static string Plural(int n) => n == 1 ? "1 person" : $"{n} people";
}
