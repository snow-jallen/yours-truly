using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoursTruly.Core.Diagnostics;
using YoursTruly.Core.Import;
using YoursTruly.Data;
using YoursTruly.Messaging.Settings;

namespace YoursTruly.App.ViewModels;

/// <summary>One answer in a column's drop-down. A record rather than the bare enum so
/// the list can show "Don't import" where the code says <c>Ignore</c>.</summary>
public sealed record FieldChoice(ImportField Field)
{
    public string Label => Field.Label();

    public override string ToString() => Label;

    public static readonly IReadOnlyList<FieldChoice> All =
        [.. ImportFields.All.Select(f => new FieldChoice(f))];

    public static FieldChoice Of(ImportField field) => All.First(c => c.Field == field);
}

/// <summary>One column of the file, and what it is being imported as.</summary>
public sealed partial class ColumnRow : ObservableObject
{
    private readonly Action _changed;

    public ColumnRow(int index, string heading, ImportField field, string sample, Action changed)
    {
        Index = index;
        Heading = heading.Length > 0 ? heading : $"Column {index + 1}";
        Sample = sample;
        _changed = changed;
        _choice = FieldChoice.Of(field);
    }

    public int Index { get; }
    public string Heading { get; }

    /// <summary>The first few values in this column, so the choice is made against what
    /// is actually in it rather than against what the heading claims.</summary>
    public string Sample { get; }

    public IReadOnlyList<FieldChoice> Choices => FieldChoice.All;

    [ObservableProperty] private FieldChoice _choice;

    partial void OnChoiceChanged(FieldChoice value) => _changed();
}

/// <summary>Opening a file of people: what the app made of its columns, what that
/// would change, and the button that does it.
///
/// There is no entry for this on the rail. It is reached from the People screen,
/// because importing is something you do to the list of people rather than a place you
/// go, and it hands the user back to People as soon as it has finished.</summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IFilePicker _picker;
    private readonly ISettingsStore _store;
    private readonly Action<string, string> _opened;
    private readonly Func<Task> _cancelled;

    private ContactSheet? _sheet;
    private ImportPlan? _plan;

    public ImportViewModel(
        AppServices services, IFilePicker picker, ISettingsStore store,
        Action<string, string> opened, Func<Task> cancelled)
    {
        _services = services;
        _picker = picker;
        _store = store;
        _opened = opened;
        _cancelled = cancelled;

        var settings = store.Load();
        _targets = ListChoice.Options(settings, services.DatabasePath);
        _target = ListChoice.Find(_targets, services.DatabasePath) ?? _targets.FirstOrDefault();
    }

    // --- where these people are going ------------------------------------------------

    /// <summary>Which list this import writes into: the one already open, one opened
    /// before, or a new file somewhere the user picks.
    ///
    /// This lives here rather than in a setting because it is genuinely a decision per
    /// import. An updated roster goes back into the list it came from; a second,
    /// different set of people — the neighbours on top of the soccer team — belongs in
    /// a file of its own, where an import of one can never mark the other as gone.</summary>
    [ObservableProperty] private IReadOnlyList<ListChoice> _targets;

    [ObservableProperty] private ListChoice? _target;

    public string TargetPath => Target?.Path ?? "";

    public bool HasTarget => Target is not null;

    public bool HasNoTargets => Targets.Count == 0;

    partial void OnTargetChanged(ListChoice? value)
    {
        OnPropertyChanged(nameof(TargetPath));
        OnPropertyChanged(nameof(HasTarget));
        Replan();
    }

    partial void OnTargetsChanged(IReadOnlyList<ListChoice> value) =>
        OnPropertyChanged(nameof(HasNoTargets));

    /// <summary>Starts a list somewhere the user chooses. Nothing is written until they
    /// press Import, so picking a place here is not yet a commitment.</summary>
    [RelayCommand]
    private async Task NewListAsync()
    {
        if (_picker is not IDatabasePicker databases) return;
        if (await databases.PickNewAsync() is not { } path) return;
        Aim(path);
    }

    /// <summary>Points at a list that already exists — one made on another computer,
    /// say, or one Yours Truly has not been shown before.</summary>
    [RelayCommand]
    private async Task ExistingListAsync()
    {
        if (_picker is not IDatabasePicker databases) return;
        if (await databases.PickExistingAsync() is not { } path) return;
        Aim(path);
    }

    private void Aim(string path)
    {
        var chosen = ListChoice.Find(Targets, path)
                     ?? new ListChoice(SavedList.NameFor(path), path);
        if (!Targets.Any(t => ListChoice.Same(t.Path, path))) Targets = [.. Targets, chosen];
        Target = chosen;
    }

    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _fileDetail = "";
    [ObservableProperty] private bool _hasFile;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _failed;

    /// <summary>Said out loud when the file had no heading row and the app read it by
    /// hunting for e-mail addresses and phone numbers: the columns below are its own
    /// invention, not the file's, and that changes how much they should be trusted.</summary>
    [ObservableProperty] private bool _guessedColumns;

    /// <summary>Whether people already here but missing from this file are marked as no
    /// longer listed. On, because that is what importing an up-to-date list means — and
    /// off for anyone adding a second list to the same file.</summary>
    [ObservableProperty] private bool _markMissingInactive = true;

    [ObservableProperty] private int _inFile;
    [ObservableProperty] private int _added;
    [ObservableProperty] private int _updated;
    [ObservableProperty] private int _deactivated;
    [ObservableProperty] private int _unchanged;
    [ObservableProperty] private string _mappingProblem = "";
    [ObservableProperty] private string _groupLine = "";

    public bool IsReady => MappingProblem.Length == 0 && HasFile && HasTarget;

    partial void OnMappingProblemChanged(string value) => OnPropertyChanged(nameof(IsReady));
    partial void OnHasFileChanged(bool value) => OnPropertyChanged(nameof(IsReady));
    partial void OnMarkMissingInactiveChanged(bool value) => Replan();

    public ObservableCollection<ColumnRow> Columns { get; } = [];
    public ObservableCollection<ChangeRow> Changes { get; } = [];

    /// <summary>Opens the file picker. Called by the People screen's button as well as
    /// by this screen's own, so choosing another file does not mean going back.</summary>
    [RelayCommand]
    public async Task ChooseFileAsync()
    {
        var path = await _picker.PickPdfAsync();
        if (path is null) return;
        await LoadAsync(path);
    }

    public async Task LoadAsync(string path)
    {
        Busy = true;
        Failed = false;
        Status = "Reading the file…";
        Columns.Clear();
        Changes.Clear();

        try
        {
            var sheet = await Task.Run(() => ImportService.Read(path));
            _sheet = sheet;

            FileName = sheet.FileName;
            FileDetail = $"{sheet.PageCount} pages · {sheet.Rows.Count} rows · {sheet.Columns.Count} columns";
            GuessedColumns = !sheet.HeadingsFound;
            HasFile = true;

            var guess = ImportMapping.Guess(sheet);
            foreach (var column in guess.Columns)
                Columns.Add(new ColumnRow(
                    column.Column, column.Heading, column.Field,
                    string.Join(" · ", sheet.Sample(column.Column)),
                    Replan));

            Log.Record("import.read", Log.Details(
                ("file", sheet.FileName), ("pages", sheet.PageCount), ("rows", sheet.Rows.Count),
                ("columns", sheet.Columns.Count), ("headings", sheet.HeadingsFound)));

            await ReplanAsync();
        }
        catch (ImportException e)
        {
            Log.Failure("import.read", e, Log.Details(("path", Redact.Path(path))));
            Failed = true;
            HasFile = false;
            Status = e.Message;
        }
        catch (Exception e)
        {
            Log.Failure("import.read", e, Log.Details(("path", Redact.Path(path))));
            Failed = true;
            HasFile = false;
            Status = $"That file could not be read. {e.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Re-plans after a drop-down changes. Nothing is waiting on the answer —
    /// the screen updates when it arrives — but an exception in a task nobody awaits
    /// takes the process down, so it is caught and shown here rather than thrown into
    /// the void.</summary>
    private void Replan() => _ = ReplanSafelyAsync();

    private async Task ReplanSafelyAsync()
    {
        try
        {
            await ReplanAsync();
        }
        catch (Exception e)
        {
            Log.Failure("import.plan", e);
            Failed = true;
            Status = $"That change could not be worked out. {e.Message}";
        }
    }

    /// <summary>What the file would do, given how its columns are currently set. Run
    /// again on every change, so the numbers below the columns always belong to the
    /// choices above them.</summary>
    private async Task ReplanAsync()
    {
        if (_sheet is null) return;

        var mapping = Mapping();
        MappingProblem = mapping.Problem ?? "";
        Changes.Clear();

        if (!mapping.IsReady || Target is null)
        {
            _plan = null;
            InFile = Added = Updated = Deactivated = Unchanged = 0;
            GroupLine = "";
            Status = "";
            return;
        }

        // Against the list this import is aimed at, which is not necessarily the one
        // open — and on a first run is never one that exists yet.
        await using var db = _services.DbAt(Target!.Path);
        var plan = await new ImportService(db).PlanAsync(_sheet, mapping, MarkMissingInactive);
        _plan = plan;

        InFile = plan.TotalInFile;
        Added = plan.Added.Count;
        Updated = plan.Updated.Count;
        Deactivated = plan.Deactivated.Count;
        Unchanged = plan.Unchanged;

        foreach (var p in plan.Added)
            Changes.Add(new ChangeRow("New", p.DisplayName, Describe(p)));
        foreach (var u in plan.Updated)
            Changes.Add(new ChangeRow("Updated", u.Incoming.DisplayName,
                string.Join("   ", u.Changes.Select(c => $"{c.Field}: {Blank(c.From)} → {Blank(c.To)}"))));
        foreach (var r in plan.Reactivated)
            Changes.Add(new ChangeRow("Back", r.Incoming.DisplayName,
                "Listed again — restored with everything they had"));
        foreach (var d in plan.Deactivated)
            Changes.Add(new ChangeRow("Not listed", $"{d.LastName}, {d.FirstName}",
                "Kept and marked no longer listed — nothing is deleted"));

        GroupLine = plan.Groups.Count == 0
            ? ""
            : $"{plan.Groups.Count} {(plan.Groups.Count == 1 ? "group" : "groups")} from this file: "
              + string.Join(", ", plan.Groups.Take(8))
              + (plan.Groups.Count > 8 ? $" and {plan.Groups.Count - 8} more" : "");

        Status = Added + Updated + Deactivated + plan.Reactivated.Count == 0
            ? "Nothing in this file has changed since the last import."
            : "Nothing has been saved yet.";
    }

    private ImportMapping Mapping() =>
        new([.. Columns.Select(c => new ColumnMapping(c.Index, c.Heading, c.Choice.Field))]);

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_sheet is null || _plan is null || Target is null) return;
        Busy = true;
        try
        {
            var into = Target;
            if (File.Exists(into.Path)) AppDatabase.BackUp(into.Path, DateTimeOffset.Now);

            await using (var db = _services.DbAt(into.Path))
                await new ImportService(db).ApplyAsync(_sheet, _plan, Mapping(), AppServices.Today);

            Log.Record("import.apply", Log.Details(
                ("added", Added), ("updated", Updated), ("deactivated", Deactivated)));

            Done = $"Imported {FileName} into {into.Name}. {Added} added, {Updated} updated, "
                 + $"{Deactivated} marked no longer listed.";
            Clear();

            // Whatever it was written into is now the list in front of the user.
            _opened(into.Path, into.Name);
        }
        catch (Exception e)
        {
            Log.Failure("import.apply", e);
            Failed = true;
            Status = $"Nothing was saved. {e.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>What to tell the People screen once this has finished, since that is
    /// where the user is put back.</summary>
    public string Done { get; private set; } = "";

    [RelayCommand]
    private async Task CancelAsync()
    {
        Clear();
        Done = "";
        await _cancelled();
    }

    private void Clear()
    {
        HasFile = false;
        Columns.Clear();
        Changes.Clear();
        Status = "";
        MappingProblem = "";
        GroupLine = "";
        MarkMissingInactive = true;
        _sheet = null;
        _plan = null;
    }

    private static string Blank(string? s) => string.IsNullOrWhiteSpace(s) ? "(none)" : s;

    private static string Describe(NormalizedPerson p)
    {
        var has = new List<string>();
        if (p.Email is not null) has.Add("email");
        if (p.PhoneRaw is not null) has.Add("phone");
        if (p.Groups.Count > 0) has.Add(string.Join(", ", p.Groups));
        return has.Count == 0 ? "Added with no contact details" : "Added with " + string.Join(", ", has);
    }
}

/// <summary>One line of what an import would do.</summary>
public sealed record ChangeRow(string Kind, string Name, string Detail)
{
    public bool IsAdded => Kind == "New";
    public bool IsUpdated => Kind == "Updated";
    public bool IsGone => Kind == "Not listed";
    public bool IsBack => Kind == "Back";
}
