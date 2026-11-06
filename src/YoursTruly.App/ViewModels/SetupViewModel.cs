using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoursTruly.Messaging;
using YoursTruly.Messaging.Email;
using YoursTruly.Messaging.Settings;
using YoursTruly.Messaging.Android;
using YoursTruly.Messaging.BlueBubbles;
using YoursTruly.Messaging.Mac;
using YoursTruly.Messaging.Relay;
using YoursTruly.App.Relay;
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
    private readonly Action<Appearance> _applyAppearance;

    public SetupViewModel(
        AppServices services,
        ISettingsStore store,
        bool? isMac = null,
        IDatabasePicker? databases = null,
        Action? databaseChanged = null,
        IClipboardWriter? clipboard = null,
        IUpdates? updates = null,
        Action<Appearance>? applyAppearance = null,
        IRelayAgent? relayAgent = null)
    {
        _relay = relayAgent ?? (OperatingSystem.IsMacOS() ? new LaunchAgentRelay() : new NoRelayAgent());
        _applyAppearance = applyAppearance ?? Looks.Apply;
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
        _appearance = settings.Appearance;
        _gatewayUrl = settings.AndroidGateway.BaseUrl;
        _gatewayUser = settings.AndroidGateway.Username;
        _gatewayPassword = settings.AndroidGateway.Password;
        _blueBubblesUrl = settings.BlueBubbles.ServerUrl;
        _blueBubblesPassword = settings.BlueBubbles.Password;
        _macRelayAddress = settings.MacRelay.Address;
        _macRelayKey = settings.MacRelay.Key;
        _relayKey = settings.RelayHost.Key;
        _relayOn = _relay.IsSupported && _relay.IsOn;
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

    // --- how it looks ----------------------------------------------------------------
    /// <summary>Takes effect the moment it is clicked and is saved at once, without the
    /// rest of the screen: something you can see changing does not need a Save button,
    /// and one that reverted at the next start because Save was never pressed would
    /// look like a bug.</summary>
    [ObservableProperty] private Appearance _appearance;

    public bool LooksLikeComputer
    {
        get => Appearance == Appearance.System;
        set { if (value) Appearance = Appearance.System; }
    }

    public bool LooksLight
    {
        get => Appearance == Appearance.Light;
        set { if (value) Appearance = Appearance.Light; }
    }

    public bool LooksDark
    {
        get => Appearance == Appearance.Dark;
        set { if (value) Appearance = Appearance.Dark; }
    }

    partial void OnAppearanceChanged(Appearance value)
    {
        _applyAppearance(value);
        _saved = _saved with { Appearance = value };
        _store.Save(_saved);
        Log.Record("settings.appearance", Log.Details(("appearance", value.ToString())));
        foreach (var name in (string[])["LooksLikeComputer", "LooksLight", "LooksDark"])
            OnPropertyChanged(name);
    }

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

    /// <summary>A passed test is a statement about the details that were in the boxes
    /// when it ran. Change one and the statement stops being true, so it goes — along
    /// with the message underneath, which names an address that may no longer be the
    /// one in the box. Leaving either up is how a screen ends up saying "it works"
    /// about something nobody has tried.</summary>
    partial void OnEmailAddressChanged(string value) => ForgetEmailTest();
    partial void OnAppPasswordChanged(string value) => ForgetEmailTest();

    private void ForgetEmailTest()
    {
        EmailOk = false;
        EmailStatus = "";
    }

    // --- twilio ------------------------------------------------------------------
    [ObservableProperty] private string _accountSid;
    [ObservableProperty] private string _authToken;
    [ObservableProperty] private string _fromNumber;
    [ObservableProperty] private string _testNumber;
    [ObservableProperty] private string _messagingServiceSid;

    public bool SendsRichText => MessagingServiceSid.Trim().Length > 0;

    /// <summary>Where texts leave from: a service, or the user's own phone — through
    /// the Messages app on this Mac or BlueBubbles on any Mac for an iPhone, or through
    /// a gateway app for an Android. Every phone route sends from the user's real
    /// number.</summary>
    [ObservableProperty] private TextTransport _textVia;

    [ObservableProperty] private string _gatewayUrl;
    [ObservableProperty] private string _gatewayUser;
    [ObservableProperty] private string _gatewayPassword;

    [ObservableProperty] private string _blueBubblesUrl;
    [ObservableProperty] private string _blueBubblesPassword;

    [ObservableProperty] private string _macRelayAddress;
    [ObservableProperty] private string _macRelayKey;

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

    /// <summary>An iPhone, through Yours Truly on a Mac that has "Let my other computers
    /// text through this Mac" switched on. Offered everywhere, including on a Mac — a
    /// laptop at home can send through the Mac at the office.</summary>
    public bool UseMacRelay
    {
        get => TextVia == TextTransport.MacRelay;
        set { if (value) TextVia = TextTransport.MacRelay; }
    }

    /// <summary>An iPhone, through BlueBubbles Server on a Mac. Offered everywhere: it is
    /// a web call to the Mac, so it works from Windows and Linux, and from a Mac that is
    /// not the one signed in to Messages.</summary>
    public bool UseBlueBubbles
    {
        get => TextVia == TextTransport.BlueBubbles;
        set { if (value) TextVia = TextTransport.BlueBubbles; }
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
        TextTransport.BlueBubbles => "Text myself through BlueBubbles",
        TextTransport.MacRelay => "Text myself through my Mac",
        _ => "Send myself a test text",
    };

    public string TextRouteBlockedMessage =>
        "Texting from your iPhone needs Yours Truly running on a Mac, because it works by asking the Messages app to send. "
        + "On this computer, choose \u201cthrough Yours Truly on a Mac\u201d, which asks a Mac's Messages app over the network, or Twilio, or your Android phone if you have one. Your iPhone setting is kept for when you are back on the Mac.";

    partial void OnTextViaChanged(TextTransport value)
    {
        foreach (var name in (string[])
                 ["UseTwilioForText", "UseIphone", "UseAndroid", "UseBlueBubbles", "UseMacRelay", "TextRouteBlocked",
                  "TwilioPurpose", "TestTextLabel"])
            OnPropertyChanged(name);
    }

    partial void OnMessagingServiceSidChanged(string value)
    {
        OnPropertyChanged(nameof(SendsRichText));
        ForgetTwilioTest();
    }

    partial void OnAccountSidChanged(string value) => ForgetTwilioTest();
    partial void OnAuthTokenChanged(string value) => ForgetTwilioTest();
    partial void OnFromNumberChanged(string value) => ForgetTwilioTest();
    partial void OnTestNumberChanged(string value) => ForgetTwilioTest();

    /// <summary>Same again for the account the texts and calls go through.</summary>
    private void ForgetTwilioTest()
    {
        TwilioOk = false;
        TwilioStatus = "";
    }
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
         nameof(TwilioPurpose), nameof(TestTextLabel), nameof(TextRouteBlocked),
         nameof(LooksLikeComputer), nameof(LooksLight), nameof(LooksDark),
         nameof(RelayOn), nameof(RelayStatus), nameof(RelayBusy), nameof(RelayStateLine), nameof(RelayHasKey)];

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
        Appearance = Appearance,
        AndroidGateway = new AndroidGatewaySettings
        {
            BaseUrl = GatewayUrl.Trim(),
            Username = GatewayUser.Trim(),
            Password = GatewayPassword.Trim(),
        },
        BlueBubbles = new BlueBubblesSettings
        {
            ServerUrl = BlueBubblesUrl.Trim(),
            Password = BlueBubblesPassword.Trim(),
        },
        MacRelay = new MacRelaySettings
        {
            Address = MacRelayAddress.Trim(),
            Key = MacRelayKey.Trim(),
        },
        RelayHost = new RelayHostSettings { Key = RelayKey },
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
            ("rcs", SendsRichText), ("androidGateway", Current.AndroidGateway.IsComplete),
            ("blueBubbles", Current.BlueBubbles.IsComplete), ("macRelay", Current.MacRelay.IsComplete)));
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
                TextTransport.MacRelay => new RelayTextSender(
                    new HttpClient { Timeout = RelayTextSender.Patience }, Current.MacRelay),
                TextTransport.BlueBubbles => new BlueBubblesTextSender(
                    new HttpClient { Timeout = BlueBubblesTextSender.Patience }, Current.BlueBubbles),
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

    // --- this Mac, texting for the user's other computers ------------------------------
    private readonly IRelayAgent _relay;

    /// <summary>Only on a Mac: it is the Mac's Messages app that does the sending.</summary>
    public bool RelayHostAvailable => _isMac && _relay.IsSupported;

    /// <summary>Whether the login item is there — which is what actually decides whether
    /// the relay runs, so it is read from there rather than kept in the settings.</summary>
    [ObservableProperty] private bool _relayOn;
    [ObservableProperty] private string _relayKey;
    [ObservableProperty] private string _relayStatus = "";
    [ObservableProperty] private bool _relayBusy;

    public bool RelayHasKey => RelayKey.Length > 0;

    public string RelayStateLine => RelayOn
        ? "On. Your other computers can send texts through this Mac while it is awake and you are logged in."
        : "Off. Nothing on this Mac is listening.";

    /// <summary>Where the other computers should point. Read when shown rather than
    /// once, since a laptop's address changes as it moves between networks.</summary>
    public IReadOnlyList<RelayAddress> RelayAddresses =>
        RelayHostAvailable ? RelayAddressBook.ForThisMac() : [];

    partial void OnRelayOnChanged(bool value) => OnPropertyChanged(nameof(RelayStateLine));
    partial void OnRelayKeyChanged(string value) => OnPropertyChanged(nameof(RelayHasKey));

    [RelayCommand]
    private void TurnOnRelay()
    {
        if (!RelayHostAvailable) return;
        try
        {
            if (!RelayHasKey) RelayKey = Messaging.Relay.RelayKey.New();
            _store.Save(Current);
            MarkSaved();
            _relay.TurnOn();
            RelayOn = true;
            OnPropertyChanged(nameof(RelayAddresses));
            RelayStatus = "Switched on. Give your other computers one of the addresses below and the key. "
                + "Then press \u201cText myself through this Mac\u201d once: macOS will ask whether Yours Truly may control Messages, and somebody has to be at this Mac to say OK.";
        }
        catch (Exception e)
        {
            Log.Failure("relay.on", e);
            RelayStatus = e.Message;
        }
    }

    [RelayCommand]
    private void TurnOffRelay()
    {
        try
        {
            _relay.TurnOff();
            RelayOn = false;
            RelayStatus = "Switched off. Your other computers can no longer send through this Mac. The key is kept, so switching on again needs nothing changed on them.";
        }
        catch (Exception e)
        {
            Log.Failure("relay.off", e);
            RelayStatus = e.Message;
        }
    }

    /// <summary>For a key that has gone somewhere it should not have. Every other
    /// computer stops working until it is given the new one, and the screen says so.</summary>
    [RelayCommand]
    private void NewRelayKey()
    {
        RelayKey = Messaging.Relay.RelayKey.New();
        _store.Save(Current);
        MarkSaved();
        try
        {
            _relay.Restart();
            RelayStatus = "New key made, and the old one no longer works. Each of your other computers needs the new one on its Setup screen before it can send again.";
        }
        catch (Exception e)
        {
            Log.Failure("relay.restart", e);
            RelayStatus = e.Message;
        }
    }

    [RelayCommand]
    private async Task CopyRelayDetailsAsync()
    {
        if (_clipboard is null || !RelayHasKey) return;
        var addresses = string.Join(Environment.NewLine, RelayAddresses.Select(a => $"  {a.Url}   ({a.Where})"));
        await _clipboard.CopyAsync(
            $"Address (use the first that works from that computer):{Environment.NewLine}{addresses}{Environment.NewLine}Key: {RelayKey}");
        RelayStatus = "Copied the addresses and the key. Paste them somewhere only you can see — anybody with both can send texts as you.";
    }

    /// <summary>Sends a test the whole way round — out to this Mac's own listener and in
    /// through Messages — which is also what makes macOS ask for permission to control
    /// Messages, while somebody is sitting here to answer.</summary>
    [RelayCommand]
    private async Task TestRelayAsync()
    {
        RelayBusy = true;
        RelayStatus = "Sending you a test through this Mac\u2026";
        try
        {
            var sender = new RelayTextSender(
                new HttpClient { Timeout = RelayTextSender.Patience },
                new MacRelaySettings { Address = $"http://localhost:{RelayProtocol.DefaultPort}", Key = RelayKey });
            var check = await sender.TestAsync(TestNumber.Trim());
            RelayStatus = check.Message;
            Log.Record("test.relay", Log.Details(("ok", check.Ok), ("result", Redact.Failure(check.Message))));
        }
        finally { RelayBusy = false; }
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
