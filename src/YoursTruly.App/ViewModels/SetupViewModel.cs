using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoursTruly.Messaging;
using YoursTruly.Messaging.Email;
using YoursTruly.Messaging.Settings;
using YoursTruly.Messaging.Android;
using YoursTruly.Messaging.Mac;
using YoursTruly.Messaging.Twilio;
using YoursTruly.Core.Diagnostics;
using YoursTruly.Core.Domain;
using YoursTruly.Data;

namespace YoursTruly.App.ViewModels;

public sealed partial class SetupViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ISettingsStore _store;

    private readonly bool _isMac;

    /// <summary><paramref name="isMac"/> is only passed by tests; the app reads the
    /// platform it is actually running on.</summary>
    private readonly IDatabasePicker? _databases;
    private readonly IClipboardWriter? _clipboard;
    private readonly Action? _databaseChanged;

    public SetupViewModel(
        AppServices services,
        ISettingsStore store,
        bool? isMac = null,
        IDatabasePicker? databases = null,
        Action? databaseChanged = null,
        IClipboardWriter? clipboard = null,
        IUpdates? updates = null)
    {
        _services = services;
        _updates = updates ?? new UpdateService(UpdateService.DefaultRepository);
        _store = store;
        _databases = databases;
        _databaseChanged = databaseChanged;
        _clipboard = clipboard;
        _logFolder = services.Activity.Folder;
        _isMac = isMac ?? OperatingSystem.IsMacOS();

        var settings = store.Load();
        _signature = settings.Signature;
        _emailAddress = settings.Email.Address;
        _appPassword = settings.Email.AppPassword;
        _displayName = settings.Email.DisplayName;
        _accountSid = settings.Twilio.AccountSid;
        _authToken = settings.Twilio.AuthToken;
        _fromNumber = settings.Twilio.FromNumber;
        _testNumber = settings.Twilio.TestNumber;
        _messagingServiceSid = settings.Twilio.MessagingServiceSid;
        _textVia = settings.TextVia;
        _gatewayUrl = settings.AndroidGateway.BaseUrl;
        _gatewayUser = settings.AndroidGateway.Username;
        _gatewayPassword = settings.AndroidGateway.Password;
        _databasePath = services.DatabasePath;
        _settingsPath = store.Path;
        // The open list has to be in the switcher, so if it is not there yet it is
        // added — and written down, because a list the app forgot about between runs is
        // one the user has to go and find in a file picker again.
        _lists = Remembering(settings.Lists, services.DatabasePath);
        _listName = NameOfOpenList();
        // Including the open database path: the screen shows where the list actually
        // is, and that is not an edit somebody has failed to save. Without it Setup
        // says "not saved yet" the instant it opens, on a settings file that records
        // no path at all.
        _saved = settings with { Lists = _lists, DatabasePath = services.DatabasePath ?? "" };
        if (_lists.Count != settings.Lists.Count) _store.Save(_saved);
    }

    // --- the lists, and which one is open ----------------------------------------
    [ObservableProperty] private string? _databasePath;
    [ObservableProperty] private string _settingsPath;
    [ObservableProperty] private string _backupStatus = "";

    /// <summary>Every list the app knows about. One file each, because a soccer team
    /// and a class have nothing to do with each other and an import that clears one
    /// has no business touching the other.</summary>
    [ObservableProperty] private IReadOnlyList<SavedList> _lists;

    /// <summary>What the open list is called. Editable here, which is the whole point:
    /// "contacts.db" tells nobody anything and "Primary class" tells them everything.</summary>
    [ObservableProperty] private string _listName;

    /// <summary>True when there is a list to name, back up or talk about at all.</summary>
    public bool HasList => DatabasePath is not null;

    public IReadOnlyList<SavedList> OtherLists =>
        [.. Lists.Where(l => !string.Equals(l.Path, DatabasePath, StringComparison.Ordinal))];

    public bool HasOtherLists => OtherLists.Count > 0;

    partial void OnListsChanged(IReadOnlyList<SavedList> value)
    {
        OnPropertyChanged(nameof(OtherLists));
        OnPropertyChanged(nameof(HasOtherLists));
    }

    partial void OnDatabasePathChanged(string? value) => OnPropertyChanged(nameof(HasList));

    partial void OnListNameChanged(string value)
    {
        var clean = value.Trim();
        if (clean.Length == 0 || DatabasePath is null) return;
        Lists = [.. Lists.Select(l =>
            string.Equals(l.Path, DatabasePath, StringComparison.Ordinal) ? l with { Name = clean } : l)];
    }

    /// <summary>The saved lists with the open one certainly among them, so switching
    /// back to it later is one click rather than a trip through the file picker.</summary>
    private static IReadOnlyList<SavedList> Remembering(IReadOnlyList<SavedList> lists, string? openPath)
    {
        if (openPath is null) return lists;
        if (lists.Any(l => string.Equals(l.Path, openPath, StringComparison.Ordinal))) return lists;
        return [.. lists, new SavedList(SavedList.NameFor(openPath), openPath)];
    }

    private string NameOfOpenList() =>
        DatabasePath is null
            ? ""
            : Lists.FirstOrDefault(l => string.Equals(l.Path, DatabasePath, StringComparison.Ordinal))?.Name
              ?? SavedList.NameFor(DatabasePath);

    // --- updates ---------------------------------------------------------------------
    private readonly IUpdates _updates;

    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _updateBusy;
    [ObservableProperty] private bool _updateReady;

    public string CurrentVersion => $"Version {UpdateService.CurrentVersion}";

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateBusy = true;
        UpdateStatus = "Looking for a newer version\u2026";
        try
        {
            var state = await _updates.CheckAsync();
            UpdateStatus = state.Message;

            // Found one: fetch it straight away rather than making them press twice.
            if (state.Version is not null)
            {
                UpdateStatus = $"Downloading version {state.Version}\u2026";
                var downloaded = await _updates.DownloadAsync();
                UpdateStatus = downloaded.Message;
                UpdateReady = downloaded.UpdateReady;
            }
        }
        finally { UpdateBusy = false; }
    }

    [RelayCommand]
    private void RestartToUpdate() => _updates.ApplyAndRestart();

    // --- getting help --------------------------------------------------------------
    [ObservableProperty] private string _logFolder;
    [ObservableProperty] private string _logStatus = "";

    public bool CanCopyLog => _clipboard is not null;

    /// <summary>Puts the recent log on the clipboard to paste to whoever is helping.
    /// It records what was done and what failed, never a name, a number or a word of
    /// any message.</summary>
    [RelayCommand]
    private async Task CopyLogAsync()
    {
        if (_clipboard is null) return;
        try
        {
            await _clipboard.CopyAsync(_services.Activity.Recent());
            LogStatus = "Copied. Paste it into an email or a message to whoever is helping you.";
            Log.Record("log.copied");
        }
        catch (Exception e)
        {
            LogStatus = $"The log could not be copied. {e.Message}";
        }
    }

    // --- who the messages are from ------------------------------------------------
    /// <summary>Whatever should go at the bottom of every message. One box, written by
    /// the person whose messages these are, because the app cannot know what they want
    /// to be called or what they want to say about themselves.</summary>
    [ObservableProperty] private string _signature;

    public string SignaturePreview =>
        Core.Domain.Signature.Clean(Signature) is { } line
            ? $"\u2014 {line}"
            : "Nobody has said who these messages are from yet.";

    public int SignatureMaxLength => Core.Domain.Signature.MaxLength;

    partial void OnSignatureChanged(string value) => OnPropertyChanged(nameof(SignaturePreview));

    // --- email -------------------------------------------------------------------
    [ObservableProperty] private string _emailAddress;
    [ObservableProperty] private string _appPassword;
    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _emailStatus = "";
    [ObservableProperty] private bool _emailOk;
    [ObservableProperty] private bool _emailBusy;

    // --- twilio ------------------------------------------------------------------
    [ObservableProperty] private string _accountSid;
    [ObservableProperty] private string _authToken;
    [ObservableProperty] private string _fromNumber;
    [ObservableProperty] private string _testNumber;
    [ObservableProperty] private string _messagingServiceSid;

    public bool SendsRichText => MessagingServiceSid.Trim().Length > 0;

    /// <summary>Where texts leave from: a service, or the user's own phone — through
    /// the Messages app on a Mac for an iPhone, or through a gateway app for an
    /// Android. Both phone routes send from the user's real number.</summary>
    [ObservableProperty] private TextTransport _textVia;

    [ObservableProperty] private string _gatewayUrl;
    [ObservableProperty] private string _gatewayUser;
    [ObservableProperty] private string _gatewayPassword;

    public bool UseTwilioForText
    {
        get => TextVia == TextTransport.Twilio;
        set { if (value) TextVia = TextTransport.Twilio; }
    }

    /// <summary>Only selectable on a Mac. The iPhone route works by driving the
    /// Messages app, which exists nowhere else — so this is refused rather than
    /// accepted and then failing at the moment somebody presses Send.</summary>
    public bool UseIphone
    {
        get => TextVia == TextTransport.MacMessages;
        set { if (value && IphoneRouteAvailable) TextVia = TextTransport.MacMessages; }
    }

    public bool UseAndroid
    {
        get => TextVia == TextTransport.AndroidGateway;
        set { if (value) TextVia = TextTransport.AndroidGateway; }
    }

    /// <summary>The iPhone route drives the Messages app, which only exists on a Mac.
    /// The Android route is an HTTP call, so it works from anywhere.</summary>
    public bool IphoneRouteAvailable => _isMac;

    /// <summary>True when the saved choice cannot work here — an iPhone route carried
    /// over from a Mac, opened on Windows or Linux.</summary>
    public bool TextRouteBlocked => TextVia == TextTransport.MacMessages && !IphoneRouteAvailable;

    /// <summary>Calls always go through Twilio, so the account is needed even when
    /// texts come from the user's own phone. Saying which of the two it is for stops
    /// the credentials looking like leftovers from a route no longer in use.</summary>
    public string TwilioPurpose => UseTwilioForText
        ? "Used for your texts and your phone calls"
        : "Used for phone calls — your texts go out from your own phone";

    public string TestTextLabel => TextVia switch
    {
        TextTransport.MacMessages => "Text myself through Messages",
        TextTransport.AndroidGateway => "Text myself through my phone",
        _ => "Send myself a test text",
    };

    public string TextRouteBlockedMessage =>
        "Texting from your iPhone needs Yours Truly running on a Mac, because it works by asking the Messages app to send. "
        + "On this computer, choose Twilio, or your Android phone if you have one. Your iPhone setting is kept for when you are back on the Mac.";

    partial void OnTextViaChanged(TextTransport value)
    {
        foreach (var name in (string[])
                 ["UseTwilioForText", "UseIphone", "UseAndroid", "TextRouteBlocked",
                  "TwilioPurpose", "TestTextLabel"])
            OnPropertyChanged(name);
    }

    partial void OnMessagingServiceSidChanged(string value) => OnPropertyChanged(nameof(SendsRichText));
    [ObservableProperty] private string _twilioStatus = "";
    [ObservableProperty] private bool _twilioOk;
    [ObservableProperty] private bool _twilioBusy;

    /// <summary>What was last written to disk. Everything on this screen is compared
    /// against it, so the Save button can say whether it needs pressing instead of
    /// leaving somebody to wonder.</summary>
    private AppSettings _saved;

    public bool IsDirty => Current != _saved;

    public string SaveHint => IsDirty
        ? "You have changes that are not saved yet."
        : "Everything here is saved.";

    private static readonly string[] NotWorthRechecking =
        [nameof(IsDirty), nameof(SaveHint), nameof(OtherLists), nameof(HasOtherLists),
         nameof(EmailStatus), nameof(TwilioStatus),
         nameof(BackupStatus), nameof(EmailBusy), nameof(TwilioBusy), nameof(EmailOk), nameof(TwilioOk),
         nameof(TwilioPurpose), nameof(TestTextLabel), nameof(TextRouteBlocked)];

    /// <summary>Any field changing can make the screen dirty, and there are a lot of
    /// fields. Watching them all in one place beats remembering to add each new one.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is null || NotWorthRechecking.Contains(e.PropertyName)) return;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(SaveHint));
    }

    private void MarkSaved()
    {
        _saved = Current;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(SaveHint));
    }

    private AppSettings Current => new()
    {
        DatabasePath = DatabasePath ?? "",
        TextVia = TextVia,
        AndroidGateway = new AndroidGatewaySettings
        {
            BaseUrl = GatewayUrl.Trim(),
            Username = GatewayUser.Trim(),
            Password = GatewayPassword.Trim(),
        },
        Lists = Lists,
        Signature = Core.Domain.Signature.Clean(Signature) ?? "",
        Email = new EmailSettings
        {
            Address = EmailAddress.Trim(),
            AppPassword = AppPassword.Trim(),

            // As typed, and only as typed. What a recipient actually sees falls back to
            // the name in the signature — see AppSettings.EmailAs, which is where
            // that belongs: derived here, it reads as an unsaved edit nobody made.
            DisplayName = DisplayName.Trim(),
        },
        Twilio = new TwilioSettings
        {
            AccountSid = AccountSid.Trim(),
            AuthToken = AuthToken.Trim(),
            FromNumber = FromNumber.Trim(),
            TestNumber = TestNumber.Trim(),
            MessagingServiceSid = MessagingServiceSid.Trim(),
        },
    };

    [RelayCommand]
    private void Save()
    {
        _store.Save(Current);
        MarkSaved();

        // What was filled in, never what was typed into it.
        Log.Record("settings.save", Log.Details(
            ("signed", Current.Signature.Length > 0), ("email", Current.Email.IsComplete),
            ("twilio", Current.Twilio.IsComplete), ("textVia", TextVia.ToString()),
            ("rcs", SendsRichText), ("androidGateway", Current.AndroidGateway.IsComplete)));
    }

    [RelayCommand]
    private async Task TestEmailAsync()
    {
        EmailBusy = true;
        EmailStatus = "Sending you a test message…";
        try
        {
            _store.Save(Current);
            MarkSaved();
            var sender = new EmailSender(new SmtpTransport(), Current.EmailAs);
            var check = await sender.TestAsync("");
            EmailOk = check.Ok;
            EmailStatus = check.Message;
            Log.Record("test.email", Log.Details(
                ("ok", check.Ok), ("host", Current.EmailAs.Host),
                ("result", Redact.Failure(check.Message))));
        }
        finally { EmailBusy = false; }
    }

    [RelayCommand]
    private async Task TestTextAsync()
    {
        TwilioBusy = true;
        TwilioStatus = "Sending you a test text…";
        try
        {
            _store.Save(Current);
            MarkSaved();
            IMessageSender sender = TextVia switch
            {
                TextTransport.MacMessages => new MessagesTextSender(new AppleScriptRunner()),
                TextTransport.AndroidGateway => new AndroidGatewayTextSender(new HttpClient(), Current.AndroidGateway),
                _ => new TextSender(new TwilioGateway(Current.Twilio), Current.Twilio),
            };
            var check = await sender.TestAsync(
                TextVia == TextTransport.Twilio ? "" : TestNumber.Trim());
            TwilioOk = check.Ok;
            TwilioStatus = check.Message;
            Log.Record("test.text", Log.Details(
                ("ok", check.Ok), ("via", TextVia.ToString()),
                ("rcs", SendsRichText), ("result", Redact.Failure(check.Message))));
        }
        finally { TwilioBusy = false; }
    }

    [RelayCommand]
    private async Task TestCallAsync()
    {
        TwilioBusy = true;
        TwilioStatus = "Calling you now…";
        try
        {
            _store.Save(Current);
            MarkSaved();
            var sender = new VoiceSender(new TwilioGateway(Current.Twilio), Current.Twilio);
            var check = await sender.TestAsync("");
            TwilioOk = check.Ok;
            TwilioStatus = check.Message;
        }
        finally { TwilioBusy = false; }
    }

    public bool CanBrowseForDatabase => _databases is not null;

    [RelayCommand]
    private Task OpenDatabaseAsync() => SwitchAsync(existing: true);

    [RelayCommand]
    private Task NewDatabaseAsync() => SwitchAsync(existing: false);

    /// <summary>Opens one of the lists already known, from the list of them here.</summary>
    [RelayCommand]
    private void OpenSaved(SavedList list) => Switch(list.Path, list.Name, existing: true);

    /// <summary>Takes a list off the switcher without touching the file. Forgetting
    /// where something is kept is not the same as deleting it, and this app should
    /// never be the reason a directory disappears.</summary>
    [RelayCommand]
    private void Forget(SavedList list)
    {
        Lists = [.. Lists.Where(l => !string.Equals(l.Path, list.Path, StringComparison.Ordinal))];
        _store.Save(Current);
        MarkSaved();
        BackupStatus = $"Yours Truly has forgotten “{list.Name}”. The file itself is untouched, at {list.Path}.";
    }

    /// <summary>Opens a different list, or starts one somewhere else.</summary>
    private async Task SwitchAsync(bool existing)
    {
        if (_databases is null) return;

        var path = existing ? await _databases.PickExistingAsync() : await _databases.PickNewAsync();
        if (path is null) return;

        Switch(path, SavedList.NameFor(path), existing);
    }

    /// <summary>The chosen file has to open as a the app list before it becomes the
    /// open one — finding out afterwards would mean the app is already pointed at it.</summary>
    private void Switch(string path, string name, bool existing)
    {
        if (string.Equals(path, _services.DatabasePath, StringComparison.Ordinal))
        {
            BackupStatus = "That is the list already open.";
            return;
        }

        var previous = ListName;
        try
        {
            _services.SwitchTo(path);
        }
        catch (Exception e)
        {
            Log.Failure("database.switch", e, Log.Details(("path", Redact.Path(path))));
            BackupStatus = $"That file could not be opened as a Yours Truly list, so nothing changed. {e.Message}";
            return;
        }

        DatabasePath = path;
        Lists = Remembering(Lists, path);
        ListName = NameOfOpenList();
        if (ListName != name && !existing) ListName = name;

        _store.Save(Current with { DatabasePath = path });
        MarkSaved();

        BackupStatus = existing
            ? $"Opened “{ListName}”. “{previous}” is untouched."
            : $"Started “{ListName}”, which is empty. Import a file on the People screen to fill it.";

        _databaseChanged?.Invoke();
    }

    [RelayCommand]
    private void BackUpNow()
    {
        if (_services.DatabasePath is not { } open)
        {
            BackupStatus = "There is no list open to back up.";
            return;
        }

        try
        {
            var to = AppDatabase.BackUp(open, DateTimeOffset.Now);
            BackupStatus = $"Copied to {Path.GetFileName(to)}.";
        }
        catch (Exception e)
        {
            BackupStatus = $"Could not make a backup. {e.Message}";
        }
    }
}
