using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoursTruly.Core.Diagnostics;
using YoursTruly.Data;

namespace YoursTruly.App.ViewModels;

public sealed partial class CorrectionRow : ObservableObject
{
    private readonly Func<Guid, Task> _markCopiedBack;
    private readonly Func<string, Task> _copy;

    public CorrectionRow(Correction correction, Func<Guid, Task> markCopiedBack, Func<string, Task> copy)
    {
        Correction = correction;
        _markCopiedBack = markCopiedBack;
        _copy = copy;
    }

    public Correction Correction { get; }
    public string PersonName => Correction.PersonName;
    public string KindLabel => Correction.KindLabel;
    public string Value => Correction.Value;
    public string AddedOn => Correction.AddedOn.ToString("d MMM");

    [RelayCommand] private Task Copy() => _copy(Correction.Value);
    [RelayCommand] private Task Done() => _markCopiedBack(Correction.ContactPointId);
}

/// <summary>What has changed here since the last import: the details typed into the app
/// that the file it was imported from does not carry.
///
/// This is the answer to "what does the app know that my list doesn't?", and it exists
/// because the app deliberately never writes a correction over what a file said — it
/// records it alongside, which is what makes it survive every future import and what
/// leaves it visible here until the two agree.</summary>
public sealed partial class ChangesViewModel(AppServices services, IClipboardWriter clipboard)
    : ObservableObject
{
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isEmpty;

    public ObservableCollection<CorrectionRow> Rows { get; } = [];

    public async Task LoadAsync()
    {
        await using var db = services.Db();
        var corrections = await new DirectoryService(db).CorrectionsAsync();

        Rows.Clear();
        foreach (var correction in corrections)
            Rows.Add(new CorrectionRow(correction, MarkCopiedBackAsync, CopyAsync));

        IsEmpty = Rows.Count == 0;
        Summary = Rows.Count switch
        {
            0 => "Nothing has changed since your last import. Everything Yours Truly holds came out of a file.",
            1 => "1 detail here that your file does not have.",
            _ => $"{Rows.Count} details here that your file does not have.",
        };
    }

    [RelayCommand]
    private async Task CopyAll()
    {
        var lines = Rows.Select(r => $"{r.PersonName}\t{r.KindLabel}\t{r.Value}");
        await clipboard.CopyAsync(string.Join(Environment.NewLine, lines));
        Log.Record("changes.copied", Log.Details(("rows", Rows.Count)));
    }

    private Task CopyAsync(string value) => clipboard.CopyAsync(value);

    private async Task MarkCopiedBackAsync(Guid contactPointId)
    {
        await using var db = services.Db();
        await new DirectoryService(db).MarkCopiedBackAsync(contactPointId, AppServices.Today);
        Log.Record("changes.done");
        await LoadAsync();
    }
}
