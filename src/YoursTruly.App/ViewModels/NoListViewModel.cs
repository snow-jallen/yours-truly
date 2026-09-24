using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoursTruly.Messaging.Settings;

namespace YoursTruly.App.ViewModels;

/// <summary>What every screen shows when no list is open, which on a fresh install is
/// all of them.
///
/// The app has no default list and no default place to keep one. That is deliberate: a
/// list of people is the user's document, and an app that quietly makes one somewhere
/// of its own choosing has decided something it was not asked to decide. So until
/// somebody imports a file or starts a list by hand, nothing has been written
/// anywhere — and this says so, with the three ways out of it.</summary>
public sealed partial class NoListViewModel(
    Func<Task> import, Func<Task> openExisting, Func<Task> startEmpty, Func<SavedList, Task> openSaved)
    : ObservableObject
{
    /// <summary>Lists opened before, in case the one that was open has been moved,
    /// renamed, or is on a drive that is not plugged in.</summary>
    [ObservableProperty] private IReadOnlyList<SavedList> _known = [];

    public bool HasKnown => Known.Count > 0;

    [ObservableProperty] private string _status = "";

    partial void OnKnownChanged(IReadOnlyList<SavedList> value) => OnPropertyChanged(nameof(HasKnown));

    [RelayCommand] private Task Import() => import();
    [RelayCommand] private Task Open() => openExisting();
    [RelayCommand] private Task StartEmpty() => startEmpty();
    [RelayCommand] private Task OpenSaved(SavedList list) => openSaved(list);
}
