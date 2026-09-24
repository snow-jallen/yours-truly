using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoursTruly.Core.Diagnostics;
using YoursTruly.Core.Domain;
using YoursTruly.Data;
using YoursTruly.Messaging;
using YoursTruly.Messaging.Email;
using YoursTruly.Messaging.Settings;
using YoursTruly.Messaging.Android;
using YoursTruly.Messaging.Mac;
using YoursTruly.Messaging.Twilio;
using Entities = YoursTruly.Data.Entities;

namespace YoursTruly.App.ViewModels;

public sealed partial class SendRow : ObservableObject
{
    private readonly Action<Guid, bool> _pick;
    private readonly Func<Guid, ChannelSet, Task> _choose;

    public SendRow(Recipient person, Action<Guid, bool> pick, Func<Guid, ChannelSet, Task> choose)
    {
        Person = person;
        _pick = pick;
        _choose = choose;
        _channels = person.PreferredChannels;
    }

    public Recipient Person { get; }
    public Guid Id => Person.Id;
    public string Name => Person.SortName;
    public string Age => Person.Age?.ToString() ?? "—";
    public string Birthday => Person.BirthMonth is int m && Person.BirthDay is int d
        ? $"{d} {Months[m - 1]}" : "—";

    /// <summary>Editable here as well as on the People screen: deciding how to reach
    /// somebody usually occurs to you while looking at who is about to be sent to.</summary>
    [ObservableProperty] private ChannelSet _channels;

    public bool IsEmail => Channels.Has(Channel.Email);
    public bool IsText => Channels.Has(Channel.Text);
    public bool IsVoice => Channels.Has(Channel.Voice);
    public bool NoChannel => Channels.IsEmpty;

    [RelayCommand] private Task ChooseEmail() => Toggle(Channel.Email);
    [RelayCommand] private Task ChooseText() => Toggle(Channel.Text);
    [RelayCommand] private Task ChooseVoice() => Toggle(Channel.Voice);

    private async Task Toggle(Channel channel)
    {
        Channels = Channels.Toggle(channel);
        await _choose(Person.Id, Channels);
    }

    partial void OnChannelsChanged(ChannelSet value)
    {
        foreach (var name in (string[])["IsEmail", "IsText", "IsVoice", "NoChannel", "CanReceive", "GoesTo"])
            OnPropertyChanged(name);
    }

    private Recipient AsChosen => Person with { PreferredChannels = Channels };

    public bool CanReceive => AsChosen.CanReceive;

    /// <summary>Where this message would actually land, in the form people read rather
    /// than the form a service dials — every address, because somebody who ticked two
    /// channels gets two messages and both are worth seeing before pressing Send.</summary>
    public string GoesTo
    {
        get
        {
            var going = AsChosen.Deliveries;
            return going.Count == 0
                ? Problem
                : string.Join(", ", going.Select(r =>
                    r.Channel is Channel.Email ? r.Address! : PhoneFormat.ForDisplay(r.Address)));
        }
    }

    public string Note => Person.Notes ?? "";
    public bool HasNote => Person.HasNote;

    private string Problem => AsChosen.Reachability.Reason switch
    {
        UnreachableReason.NoChannelChosen => "no channel chosen",
        UnreachableReason.MissingAddress => Channels.Has(Channel.Email) && !Channels.Has(Channel.Text)
            ? "no email address" : "no phone number",
        UnreachableReason.ChannelNotSupported => "channel not supported yet",
        UnreachableReason.NotInDirectory => "no longer in the directory",
        _ => "cannot be reached",
    };

    [ObservableProperty] private bool _isSelected = true;

    partial void OnIsSelectedChanged(bool value) => _pick(Id, value);

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
}

public sealed partial class SendViewModel(AppServices services, ISettingsStore store, bool? isMac = null)
    : ObservableObject
{
    private readonly bool _isMac = isMac ?? OperatingSystem.IsMacOS();

    private const decimal TextCost = 0.0079m;
    private const decimal CallCost = 0.014m;

    private IReadOnlyList<Recipient> _all = [];
    private readonly HashSet<Guid> _deselected = [];
    private AudienceSort _sort = AudienceSort.ByName;

    public const string AnyChannel = "Any channel";
    public const string NoChannelChosen = "No channel chosen";
    public const string AnyMonth = "Any birthday";
    public const string EachPersonsChoice = "However each person prefers";

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _channel = AnyChannel;
    [ObservableProperty] private string _birthdayMonth = AnyMonth;
    [ObservableProperty] private string _minAge = "";
    [ObservableProperty] private string _maxAge = "";
    [ObservableProperty] private IReadOnlyList<GroupChoice> _groupOptions = [GroupChoice.Any];
    [ObservableProperty] private GroupChoice? _group = GroupChoice.Any;

    // --- saving the ticked people as a group ---------------------------------------
    [ObservableProperty] private bool _savingGroup;
    [ObservableProperty] private string _newGroupName = "";
    [ObservableProperty] private string _groupStatus = "";

    /// <summary>Overrides everyone's preference for this one send — for something
    /// urgent enough to text the people who would normally be e-mailed.</summary>
    [ObservableProperty] private string _sendVia = EachPersonsChoice;

    [ObservableProperty] private string _subject = "";
    [ObservableProperty] private string _body = "";
    [ObservableProperty] private string _messagePreview = "";
    [ObservableProperty] private string _lengthLine = "";
    [ObservableProperty] private string _senderLine = "";

    // --- how the people who prefer a call will hear this -------------------------
    [ObservableProperty] private bool _speakAloud = true;
    [ObservableProperty] private string _recordingUrl = "";
    [ObservableProperty] private string _voiceStatus = "";
    [ObservableProperty] private bool _voiceBusy;

    public bool UseMyVoice
    {
        get => !SpeakAloud;
        set => SpeakAloud = !value;
    }

    public bool HasRecording => RecordingUrl.Length > 0;

    partial void OnSpeakAloudChanged(bool value)
    {
        OnPropertyChanged(nameof(UseMyVoice));
        VoiceStatus = value
            ? "Twilio will read the message out."
            : HasRecording ? "Your recording will play." : "Record yourself reading it, then it will play.";
    }

    partial void OnRecordingUrlChanged(string value) => OnPropertyChanged(nameof(HasRecording));

    [ObservableProperty] private string _matchLine = "";
    [ObservableProperty] private string _selectedLine = "";
    [ObservableProperty] private int _emailCount;
    [ObservableProperty] private int _textCount;
    [ObservableProperty] private int _voiceCount;
    [ObservableProperty] private string _costLine = "";
    [ObservableProperty] private string _costTotal = "$0.00";
    [ObservableProperty] private string _paceLine = "";
    [ObservableProperty] private string _unreachableLine = "";
    [ObservableProperty] private bool _anyUnreachable;
    [ObservableProperty] private string _sendLabel = "Send";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _sending;
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private string _blockedReason = "";

    public bool IsBlocked => BlockedReason.Length > 0;

    partial void OnBlockedReasonChanged(string value) => OnPropertyChanged(nameof(IsBlocked));

    /// <summary>Refuses the send outright rather than letting every message fail one at
    /// a time — the saved route is an iPhone, and this is not a Mac.</summary>
    private void CheckRoute()
    {
        var settings = store.Load();
        BlockedReason = settings.TextVia == TextTransport.MacMessages && !_isMac
            ? "Texts cannot go out from this computer: Yours Truly is set to send from your iPhone, which needs to run on a Mac. Open Setup and choose Twilio, or your Android phone."
            : "";
    }

    public ObservableCollection<SendRow> Rows { get; } = [];
    public IReadOnlyList<string> ChannelOptions { get; } =
        [AnyChannel, "Email", "Text", "Phone call", NoChannelChosen];
    public IReadOnlyList<string> SendViaOptions { get; } =
        [EachPersonsChoice, "Everyone by email", "Everyone by text", "Everyone by phone call"];

    public IReadOnlyList<string> MonthOptions { get; } =
        [AnyMonth, "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public async Task LoadAsync()
    {
        await using var db = services.Db();
        _all = await new DirectoryService(db).RecipientsAsync();
        await LoadGroupsAsync(db);
        Loaded = true;
        OnSpeakAloudChanged(SpeakAloud);
        CheckRoute();
        Refresh();
        RefreshMessage();
    }

    partial void OnBodyChanged(string value)
    {
        // The message is the script. Once it changes, a recording of the old wording is
        // worse than none — it would go out sounding confident and be wrong.
        if (HasRecording)
        {
            RecordingUrl = "";
            VoiceStatus = "The message changed, so the recording was cleared. Record it again before sending.";
        }

        RefreshMessage();
    }

    /// <summary>Rings the user, reads them the message so they are not improvising, and
    /// records them reading it. Waits for the recording rather than making them press
    /// another button at exactly the right moment.</summary>
    [RelayCommand]
    private async Task RecordVoiceAsync()
    {
        VoiceBusy = true;
        try
        {
            var settings = store.Load();
            var sender = new VoiceSender(new TwilioGateway(settings.Twilio), settings.Twilio);
            var session = await sender.StartRecordingAsync();

            Log.Record("voice.record", Log.Details(("started", session is { CallSid.Length: > 0 })));

            if (session is null || session.CallSid.Length == 0)
            {
                VoiceStatus = session?.Message ?? "Fill in your Twilio details and your own number on the Setup screen first.";
                return;
            }

            VoiceStatus = session.Message + " Yours Truly will pick it up once you hang up.";

            for (var attempt = 0; attempt < 60; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                var url = await sender.CollectRecordingAsync(session.CallSid);
                if (url is null) continue;

                RecordingUrl = url;
                SpeakAloud = false;
                VoiceStatus = "Got it. That is what the people who prefer a call will hear.";
                return;
            }

            VoiceStatus = "Yours Truly did not get a recording. Try again, and read the message after the beep before hanging up.";
        }
        finally { VoiceBusy = false; }
    }

    /// <summary>Calls the user and plays exactly what everyone else would hear.</summary>
    [RelayCommand]
    private async Task PreviewVoiceAsync()
    {
        VoiceBusy = true;
        try
        {
            var settings = store.Load();
            if (settings.Twilio.TestNumber.Trim().Length == 0)
            {
                VoiceStatus = "Add your own mobile number on the Setup screen so Yours Truly knows where to call.";
                return;
            }

            var sender = new VoiceSender(new TwilioGateway(settings.Twilio), settings.Twilio);
            var outcome = await sender.PreviewAsync(
                settings.Twilio.TestNumber.Trim(),
                new OutgoingMessage(Subject, Signed, HasRecording ? RecordingUrl : null, SpeakAloud));

            Log.Record("voice.preview", Log.Details(
                ("ok", outcome.Status == SendStatus.Sent),
                ("mode", HasRecording ? "recording" : "spoken"),
                ("error", Redact.Failure(outcome.Error))));

            VoiceStatus = outcome.Status == SendStatus.Sent
                ? $"Calling {PhoneFormat.ForDisplay(settings.Twilio.TestNumber)} now — answer it to hear what they will hear."
                : outcome.Error ?? "Yours Truly could not place the call.";
        }
        finally { VoiceBusy = false; }
    }

    partial void OnSendViaChanged(string value) => Summarise();

    private Core.Domain.Channel? Override => SendVia switch
    {
        "Everyone by email" => Core.Domain.Channel.Email,
        "Everyone by text" => Core.Domain.Channel.Text,
        "Everyone by voice" => Core.Domain.Channel.Voice,
        _ => null,
    };

    /// <summary>The body as a recipient will read it: signed, once, here — so the
    /// preview, the length shown and what actually leaves are the same string.</summary>
    private string Signed => Signature.Compose(Body, store.Load().Signature);

    private void RefreshMessage()
    {
        var signature = Signature.Clean(store.Load().Signature);
        MessagePreview = Signed;
        SenderLine = signature is not null
            ? $"Signed \u2014 {signature.Split('\n')[0]}"
            : "Nobody has said who these messages are from. Write a signature on the Setup screen.";

        var segments = Signature.TextSegments(Signed);
        LengthLine = segments <= 1
            ? $"{Signed.Length} characters \u2014 fits in one text message."
            : $"{Signed.Length} characters \u2014 sends as {segments} text segments, billed separately.";
    }

    partial void OnSearchChanged(string value) => Refresh();
    partial void OnChannelChanged(string value) => Refresh();
    partial void OnBirthdayMonthChanged(string value) => Refresh();
    partial void OnMinAgeChanged(string value) => Refresh();
    partial void OnMaxAgeChanged(string value) => Refresh();
    partial void OnGroupChanged(GroupChoice? value) => Refresh();

    private async Task LoadGroupsAsync(AppDbContext db)
    {
        var options = GroupChoice.Options(await new GroupService(db).ListAsync());
        var keep = GroupChoice.Find(options, Group);
        GroupOptions = options;
        Group = keep;
    }

    /// <summary>Shows exactly this group with everyone in it ticked — what "Send a
    /// message" on the Groups screen promises. Other filters are cleared, since a
    /// search left over from last time would quietly drop some of the group.</summary>
    public void FocusGroup(Guid groupId)
    {
        ClearFilters();
        _deselected.Clear();
        SavingGroup = false;
        GroupStatus = "";
        Group = GroupChoice.Find(GroupOptions, new GroupChoice(groupId, ""));
        Refresh();
    }

    /// <summary>Opens the box for naming a group. Typing a name that exists adds the
    /// ticked people to it, so there is one action for "new group" and "add to group".</summary>
    [RelayCommand]
    private void StartSavingGroup()
    {
        NewGroupName = Group?.Id is null ? "" : Group.Name;
        GroupStatus = "";
        SavingGroup = true;
    }

    [RelayCommand]
    private void CancelSavingGroup()
    {
        SavingGroup = false;
        GroupStatus = "";
    }

    [RelayCommand]
    private async Task SaveGroupAsync()
    {
        var name = GroupName.Clean(NewGroupName);
        if (name is null) { GroupStatus = "Give the group a name first, such as “Activities committee”."; return; }

        var ticked = Rows.Where(r => r.IsSelected).Select(r => r.Id).ToList();
        if (ticked.Count == 0) { GroupStatus = "Tick the people who belong in it first."; return; }

        await using var db = services.Db();
        var saved = await new GroupService(db).SaveMembersAsync(name, ticked);
        Log.Record("group.save", Log.Details(
            ("group", saved.Group.Id), ("created", saved.Created), ("added", saved.Added)));

        _all = await new DirectoryService(db).RecipientsAsync();
        await LoadGroupsAsync(db);
        Refresh();

        SavingGroup = false;
        GroupStatus = saved switch
        {
            { Created: true } => $"Saved {Count(saved.Added)} as “{saved.Group.Name}”. Choose it from the group list next time.",
            { Added: 0 } => $"Everyone ticked was already in “{saved.Group.Name}”.",
            _ => $"Added {Count(saved.Added)} to “{saved.Group.Name}”, which now has {Count(saved.Group.Members)}.",
        };
    }

    private static string Count(int n) => n == 1 ? "1 person" : $"{n} people";

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

    [RelayCommand]
    private void ClearFilters()
    {
        Search = "";
        Channel = AnyChannel;
        BirthdayMonth = AnyMonth;
        MinAge = "";
        MaxAge = "";
        Group = GroupChoice.Any;
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Rows) { _deselected.Remove(row.Id); row.IsSelected = true; }
        Summarise();
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var row in Rows) { _deselected.Add(row.Id); row.IsSelected = false; }
        Summarise();
    }

    private AudienceFilter Filter()
    {
        var channel = Channel switch
        {
            "Email" => ChannelFilter.Is(Core.Domain.Channel.Email),
            "Text" => ChannelFilter.Is(Core.Domain.Channel.Text),
            "Phone call" => ChannelFilter.Is(Core.Domain.Channel.Voice),
            NoChannelChosen => ChannelFilter.NoneChosen,
            _ => ChannelFilter.Any,
        };

        var month = MonthOptions.ToList().IndexOf(BirthdayMonth);

        return new AudienceFilter
        {
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search,
            Group = Group?.Id,
            Channel = channel,
            BirthdayMonth = month > 0 ? month : null,
            MinAge = int.TryParse(MinAge, out var min) ? min : null,
            MaxAge = int.TryParse(MaxAge, out var max) ? max : null,
        };
    }

    private void Refresh()
    {
        if (!Loaded) return;

        var matching = Audience.Select(_all, Filter(), _sort);
        Rows.Clear();
        foreach (var person in matching)
            Rows.Add(new SendRow(person, Pick, SaveChannelsAsync) { IsSelected = !_deselected.Contains(person.Id) });

        MatchLine = $"{matching.Count} of {_all.Count(p => p.IsActive)} people match these filters.";
        Summarise();
    }

    private void Pick(Guid id, bool selected)
    {
        if (selected) _deselected.Remove(id); else _deselected.Add(id);
        Summarise();
    }

    /// <summary>The people ticked, carrying any channel changed on this screen so the
    /// summary and the send agree.</summary>
    private IReadOnlyList<Recipient> Chosen =>
        [.. Rows.Where(r => r.IsSelected).Select(r => r.Person with { PreferredChannels = r.Channels })];

    private async Task SaveChannelsAsync(Guid personId, ChannelSet channels)
    {
        await using var db = services.Db();
        await new DirectoryService(db).SetPreferredChannelsAsync(personId, channels);
        _all = [.. _all.Select(p => p.Id == personId ? p with { PreferredChannels = channels } : p)];
        Summarise();
    }

    private void Summarise()
    {
        var summary = Audience.Summarise(Chosen, Override);

        EmailCount = summary.Count(Core.Domain.Channel.Email);
        TextCount = summary.Count(Core.Domain.Channel.Text);
        VoiceCount = summary.Count(Core.Domain.Channel.Voice);

        // Two numbers, because they are different questions once somebody can tick
        // two channels: how many people hear from you, and how many messages that is.
        SelectedLine = summary.Messages == summary.WillReceive
            ? $"{summary.Chosen} selected"
            : $"{summary.Chosen} selected \u2014 {summary.Messages} messages";

        var cost = TextCount * TextCost + VoiceCount * CallCost;
        CostTotal = cost.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        CostLine = $"{TextCount} texts at $0.0079 · {VoiceCount} calls at about $0.014 · email is free.";
        PaceLine = TextCount > 1
            ? $"Texts go out 2 to 11 seconds apart, so {TextCount} of them take {TextPacing.Describe(TextPacing.Estimate(TextCount))}. Leave Yours Truly open until it says it has finished."
            : "";

        AnyUnreachable = summary.Unreachable.Count > 0;
        if (AnyUnreachable)
        {
            var names = summary.Unreachable.Take(3).Select(u => u.Person.LastName);
            var more = summary.Unreachable.Count > 3 ? $" and {summary.Unreachable.Count - 3} more" : "";
            UnreachableLine =
                $"{summary.Unreachable.Count} selected {(summary.Unreachable.Count == 1 ? "person has" : "people have")} " +
                $"no way to receive this — {string.Join(", ", names)}{more}. They will be skipped and listed afterwards.";
        }

        SendLabel = summary.WillReceive == 1 ? "Send to 1 person" : $"Send to {summary.WillReceive} people";
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        CheckRoute();
        if (IsBlocked)
        {
            Log.Record("send.blocked", Log.Details(("reason", BlockedReason)));
            Status = BlockedReason;
            return;
        }

        var chosen = Chosen;
        if (chosen.Count == 0 || string.IsNullOrWhiteSpace(Body)) return;

        Sending = true;
        try
        {
            var settings = store.Load();
            var senders = new Dictionary<Core.Domain.Channel, IMessageSender>
            {
                [Core.Domain.Channel.Email] = new EmailSender(new SmtpTransport(), settings.EmailAs),
                [Core.Domain.Channel.Text] = settings.TextVia switch
                {
                    TextTransport.MacMessages => new MessagesTextSender(new AppleScriptRunner()),
                    TextTransport.AndroidGateway =>
                        new AndroidGatewayTextSender(new HttpClient(), settings.AndroidGateway),
                    _ => new TextSender(new TwilioGateway(settings.Twilio), settings.Twilio),
                },
                [Core.Domain.Channel.Voice] = new VoiceSender(new TwilioGateway(settings.Twilio), settings.Twilio),
            };

            await using var db = services.Db();
            var service = new BroadcastService(db, senders);
            var progress = new Progress<BroadcastProgress>(p => Status = p.NextTextIn is { } gap
                ? $"Sending… {p.Done} of {p.Total}. Next text in {Math.Ceiling(gap.TotalSeconds):0} seconds — they go out a few seconds apart so your number is not flagged as spam."
                : $"Sending… {p.Done} of {p.Total} ({p.Who})");

            Log.Record("send.start", Log.Details(
                ("chosen", chosen.Count), ("email", EmailCount), ("text", TextCount), ("voice", VoiceCount),
                ("via", Override?.ToWire() ?? "preference"),
                ("textVia", settings.TextVia.ToString()),
                ("voiceMode", HasRecording ? "recording" : SpeakAloud ? "spoken" : "none"),
                ("body", Redact.Text(Signed)), ("subject", Redact.Text(Subject))));

            var batch = await service.SendAsync(
                Subject, Signed, Describe(chosen.Count), chosen, progress, Override,
                HasRecording ? RecordingUrl : null, SpeakAloud);

            var sent = await CountAsync(db, batch.Id, Entities.DeliveryStatus.Sent);
            var failed = await CountAsync(db, batch.Id, Entities.DeliveryStatus.Failed);
            var skipped = await CountAsync(db, batch.Id, Entities.DeliveryStatus.Skipped);

            Log.Record("send.done", Log.Details(
                ("batch", batch.Id), ("sent", sent), ("failed", failed), ("skipped", skipped)));

            Status = $"Sent to {sent} {(sent == 1 ? "person" : "people")}."
                   + (failed > 0 ? $" {failed} failed." : "")
                   + (skipped > 0 ? $" {skipped} skipped." : "");
        }
        catch (Exception e)
        {
            Log.Failure("send.stopped", e);
            Status = $"The send stopped. {e.Message} Anything already sent is recorded and will not go out twice.";
        }
        finally { Sending = false; }
    }

    private static Task<int> CountAsync(AppDbContext db, Guid batchId, Entities.DeliveryStatus status) =>
        Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            db.MessageDeliveries, d => d.MessageBatchId == batchId && d.Status == status);

    private string Describe(int count)
    {
        var parts = new List<string>();
        if (Group?.Id is not null) parts.Add(Group.Name);
        if (Channel != AnyChannel) parts.Add(Channel);
        if (BirthdayMonth != AnyMonth) parts.Add($"birthdays in {BirthdayMonth}");
        if (MinAge.Length > 0 || MaxAge.Length > 0) parts.Add($"ages {(MinAge.Length > 0 ? MinAge : "any")}–{(MaxAge.Length > 0 ? MaxAge : "any")}");
        if (Search.Length > 0) parts.Add($"matching “{Search}”");
        return parts.Count == 0 ? $"Everyone active ({count})" : $"{string.Join(", ", parts)} ({count})";
    }
}
