using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using YoursTruly.App.ViewModels;

namespace YoursTruly.App.Views;

public partial class MainWindow : Window, IFilePicker, IClipboardWriter, IDatabasePicker
{
    private MainWindowViewModel? _model;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindow(AppServices services, IUpdates? updates = null) : this()
    {
        _model = new MainWindowViewModel(services, this, this, updates);
        DataContext = _model;
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Current)) MarkRail();
        };
        FirstScreen = Show(m => m.OpenFirstScreenAsync());
    }

    /// <summary>Finishes when the first screen has been chosen and loaded. Tests wait on
    /// it; the app has no need to.</summary>
    public Task FirstScreen { get; } = Task.CompletedTask;

    /// <summary>Keeps the rail on the screen actually showing. Clicking a button marks
    /// it on its own, but the window can also open on Send, and the rail must say so.</summary>
    private void MarkRail()
    {
        var button = _model?.Current switch
        {
            PeopleViewModel => "NavPeople",
            GroupsViewModel => "NavGroups",
            SendViewModel => "NavSend",
            ChangesViewModel => "NavChanges",
            HistoryViewModel => "NavHistory",
            SetupViewModel => "NavSetup",
            _ => null,
        };
        if (button is not null && this.FindControl<RadioButton>(button) is { } nav) nav.IsChecked = true;
    }

    /// <summary>Starts the background look for a newer version. Called by App rather
    /// than from the constructor, so a window built directly — as the tests build it —
    /// never reaches for the network unless the test asks it to.</summary>
    public void StartUpdateCheck() => _ = _model?.CheckForUpdateAsync();

    public async Task<string?> PickPdfAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a file of people",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private static FilePickerFileType AppDatabase =>
        new("Yours Truly list") { Patterns = ["*.db"] };

    public async Task<string?> PickExistingAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a Yours Truly list",
            AllowMultiple = false,
            FileTypeFilter = [AppDatabase],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickNewAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Where should the new list be kept?",
            SuggestedFileName = "new-list.db",
            DefaultExtension = "db",
            FileTypeChoices = [AppDatabase],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task CopyAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(text);
    }

    private async void OnPeople(object? sender, RoutedEventArgs e) => await Show(m => m.ShowPeopleAsync());
    private async void OnGroups(object? sender, RoutedEventArgs e) => await Show(m => m.ShowGroupsAsync());
    private async void OnSend(object? sender, RoutedEventArgs e) => await Show(m => m.ShowSendAsync());
    private async void OnChanges(object? sender, RoutedEventArgs e) => await Show(m => m.ShowChangesAsync());
    private async void OnHistory(object? sender, RoutedEventArgs e) => await Show(m => m.ShowHistoryAsync());
    private void OnSetup(object? sender, RoutedEventArgs e) => _model?.ShowSetup();

    /// <summary>A screen that cannot load is not a reason to take the whole window
    /// down, which is what an unhandled exception in an async void handler would do.</summary>
    private async Task Show(Func<MainWindowViewModel, Task> open)
    {
        if (_model is null) return;
        try
        {
            await open(_model);
        }
        catch (Exception failure)
        {
            _model.Current = new ScreenFailedViewModel(failure.Message);
        }
    }
}
