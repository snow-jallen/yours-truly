using YoursTruly.App;
using YoursTruly.App.Relay;
using YoursTruly.App.ViewModels;
using YoursTruly.Messaging.Settings;

namespace YoursTruly.Tests;

/// <summary>The iPhone route drives the Messages app, which only exists on macOS. These
/// pin down that it is refused everywhere else rather than accepted and then failing
/// once per recipient at the moment somebody presses Send.</summary>
public sealed class TextRouteTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"yourstruly-route-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;
    private readonly AppServices _services;

    public TextRouteTests()
    {
        Directory.CreateDirectory(_folder);
        _store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        _services = AppServices.Start(Path.Combine(_folder, "contacts.db"), Path.Combine(_folder, "settings.json"));
    }

    private SetupViewModel Setup(bool isMac) => new(_services, _store, isMac);

    [Fact]
    public void Off_a_mac_the_iphone_route_cannot_even_be_chosen()
    {
        var setup = Setup(isMac: false);
        Assert.False(setup.IphoneRouteAvailable);

        setup.UseIphone = true;

        Assert.False(setup.UseIphone);
        Assert.True(setup.UseTwilioForText);
    }

    [Fact]
    public void On_a_mac_it_can()
    {
        var setup = Setup(isMac: true);
        setup.UseIphone = true;

        Assert.True(setup.UseIphone);
        Assert.False(setup.UseTwilioForText);
        Assert.False(setup.TextRouteBlocked);
    }

    [Fact]
    public void The_android_route_is_offered_everywhere_because_it_is_just_a_web_call()
    {
        var setup = Setup(isMac: false);
        setup.UseAndroid = true;
        Assert.True(setup.UseAndroid);
    }

    [Fact]
    public void An_iphone_owner_off_a_mac_can_still_send_through_bluebubbles()
    {
        var setup = Setup(isMac: false);
        setup.UseBlueBubbles = true;

        Assert.True(setup.UseBlueBubbles);
        Assert.False(setup.UseTwilioForText);
        Assert.False(setup.TextRouteBlocked);
        Assert.Equal("Text myself through BlueBubbles", setup.TestTextLabel);
        Assert.Equal("Used for phone calls — your texts go out from your own phone", setup.TwilioPurpose);
    }

    [Fact]
    public void The_bluebubbles_details_are_saved_with_everything_else()
    {
        var setup = Setup(isMac: false);
        setup.UseBlueBubbles = true;
        setup.BlueBubblesUrl = "  http://office-mac:1234  ";
        setup.BlueBubblesPassword = "correct horse";
        Assert.True(setup.IsDirty);

        setup.SaveCommand.Execute(null);

        var saved = _store.Load();
        Assert.Equal(TextTransport.BlueBubbles, saved.TextVia);
        Assert.Equal("http://office-mac:1234", saved.BlueBubbles.ServerUrl);
        Assert.Equal("correct horse", saved.BlueBubbles.Password);
        Assert.False(setup.IsDirty);
    }

    [Fact]
    public async Task Sending_through_bluebubbles_is_never_blocked_off_a_mac()
    {
        _store.Save(_store.Load() with { TextVia = TextTransport.BlueBubbles });

        var send = new SendViewModel(_services, _store, isMac: false);
        await send.LoadAsync();

        Assert.False(send.IsBlocked);
    }

    [Fact]
    public void The_blocked_iphone_route_points_at_the_one_that_works_here()
    {
        _store.Save(_store.Load() with { TextVia = TextTransport.MacMessages });
        var setup = Setup(isMac: false);

        Assert.Contains("through Yours Truly on a Mac", setup.TextRouteBlockedMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_choice_carried_over_from_a_mac_is_flagged_rather_than_silently_wrong()
    {
        _store.Save(_store.Load() with { TextVia = TextTransport.MacMessages });

        var setup = Setup(isMac: false);

        Assert.True(setup.TextRouteBlocked);
        Assert.Contains("needs Yours Truly running on a Mac", setup.TextRouteBlockedMessage, StringComparison.Ordinal);

        // Kept, so going back to the Mac restores it rather than making them choose again.
        Assert.Equal(TextTransport.MacMessages, _store.Load().TextVia);
    }

    [Fact]
    public async Task Sending_is_refused_outright_rather_than_failing_once_per_person()
    {
        _store.Save(_store.Load() with { TextVia = TextTransport.MacMessages });

        var send = new SendViewModel(_services, _store, isMac: false);
        await send.LoadAsync();

        Assert.True(send.IsBlocked);
        Assert.Contains("needs to run on a Mac", send.BlockedReason, StringComparison.Ordinal);

        send.Body = "Dinner is Friday.";
        await send.SendCommand.ExecuteAsync(null);
        Assert.Equal(send.BlockedReason, send.Status);
    }

    [Fact]
    public async Task On_a_mac_nothing_is_blocked()
    {
        _store.Save(_store.Load() with { TextVia = TextTransport.MacMessages });

        var send = new SendViewModel(_services, _store, isMac: true);
        await send.LoadAsync();

        Assert.False(send.IsBlocked);
    }

    [Fact]
    public async Task Twilio_is_never_blocked_wherever_the_app_is_running()
    {
        _store.Save(_store.Load() with { TextVia = TextTransport.Twilio });

        var send = new SendViewModel(_services, _store, isMac: false);
        await send.LoadAsync();

        Assert.False(send.IsBlocked);
    }

    // ---- only the guidance that applies to the chosen route ------------------------

    [Fact]
    public void Twilio_advice_is_labelled_as_being_about_twilio()
    {
        var setup = Setup(isMac: true);
        Assert.True(setup.UseTwilioForText);
        Assert.Equal("Used for your texts and your phone calls", setup.TwilioPurpose);
        Assert.Equal("Send myself a test text", setup.TestTextLabel);
    }

    [Fact]
    public void Choosing_a_phone_says_the_twilio_account_is_now_only_for_calls()
    {
        var setup = Setup(isMac: true);
        setup.UseIphone = true;

        // The account is still needed — calls always go through it — so it must not read
        // as a leftover from a route no longer in use.
        Assert.Equal("Used for phone calls — your texts go out from your own phone", setup.TwilioPurpose);
        Assert.Equal("Text myself through Messages", setup.TestTextLabel);
    }

    [Fact]
    public void The_android_route_names_the_phone_rather_than_the_service()
    {
        var setup = Setup(isMac: false);
        setup.UseAndroid = true;

        Assert.Equal("Text myself through my phone", setup.TestTextLabel);
        Assert.DoesNotContain("texts", setup.TwilioPurpose.Split('—')[0], StringComparison.OrdinalIgnoreCase);
    }

    // ---- through Yours Truly on a Mac: the asking side --------------------------------

    [Fact]
    public void Any_computer_can_send_through_yours_truly_on_a_mac()
    {
        var setup = Setup(isMac: false);
        setup.UseMacRelay = true;

        Assert.True(setup.UseMacRelay);
        Assert.False(setup.TextRouteBlocked);
        Assert.Equal("Text myself through my Mac", setup.TestTextLabel);
        Assert.Equal("Used for phone calls — your texts go out from your own phone", setup.TwilioPurpose);
    }

    [Fact]
    public void The_macs_address_and_key_are_saved_with_everything_else()
    {
        var setup = Setup(isMac: false);
        setup.UseMacRelay = true;
        setup.MacRelayAddress = "  http://office-mac:47474 ";
        setup.MacRelayKey = " ABCDE-FGHJK-MNPQR-STUVW-XYZ23 ";
        setup.SaveCommand.Execute(null);

        var saved = _store.Load();
        Assert.Equal(TextTransport.MacRelay, saved.TextVia);
        Assert.Equal("http://office-mac:47474", saved.MacRelay.Address);
        Assert.Equal("ABCDE-FGHJK-MNPQR-STUVW-XYZ23", saved.MacRelay.Key);
    }

    [Fact]
    public async Task Sending_through_a_mac_is_never_blocked_off_a_mac()
    {
        _store.Save(_store.Load() with { TextVia = TextTransport.MacRelay });

        var send = new SendViewModel(_services, _store, isMac: false);
        await send.LoadAsync();

        Assert.False(send.IsBlocked);
    }

    // ---- through Yours Truly on a Mac: the Mac's side --------------------------------

    /// <summary>Stands in for the login item, so nothing here touches launchd.</summary>
    private sealed class FakeAgent : IRelayAgent
    {
        public bool IsSupported => true;
        public bool IsOn { get; private set; }
        public int Restarts { get; private set; }
        public string? Refuse { get; init; }

        public void TurnOn()
        {
            if (Refuse is not null) throw new InvalidOperationException(Refuse);
            IsOn = true;
        }

        public void TurnOff() => IsOn = false;
        public void Restart() => Restarts++;
        public void EnsureRunning() { }
    }

    private SetupViewModel MacSetup(IRelayAgent agent) =>
        new(_services, _store, isMac: true, relayAgent: agent);

    [Fact]
    public void Off_a_mac_there_is_nothing_to_switch_on()
    {
        Assert.False(Setup(isMac: false).RelayHostAvailable);
        Assert.False(new SetupViewModel(_services, _store, isMac: false, relayAgent: new FakeAgent()).RelayHostAvailable);
    }

    [Fact]
    public void Switching_on_makes_a_key_keeps_it_and_starts_the_login_item()
    {
        var agent = new FakeAgent();
        var setup = MacSetup(agent);
        Assert.True(setup.RelayHostAvailable);
        Assert.False(setup.RelayOn);
        Assert.False(setup.RelayHasKey);

        setup.TurnOnRelayCommand.Execute(null);

        Assert.True(agent.IsOn);
        Assert.True(setup.RelayOn);
        Assert.Matches("^[A-Z2-9]{5}(-[A-Z2-9]{5}){4}$", setup.RelayKey);
        Assert.Equal(setup.RelayKey, _store.Load().RelayHost.Key);
        Assert.Contains("somebody has to be at this Mac to say OK", setup.RelayStatus, StringComparison.Ordinal);
        Assert.False(setup.IsDirty);
    }

    [Fact]
    public void Switching_off_and_on_again_keeps_the_same_key()
    {
        var agent = new FakeAgent();
        var setup = MacSetup(agent);
        setup.TurnOnRelayCommand.Execute(null);
        var key = setup.RelayKey;

        setup.TurnOffRelayCommand.Execute(null);
        Assert.False(agent.IsOn);
        Assert.False(setup.RelayOn);

        setup.TurnOnRelayCommand.Execute(null);
        Assert.Equal(key, setup.RelayKey);
    }

    [Fact]
    public void A_new_key_replaces_the_old_one_and_restarts_the_relay()
    {
        var agent = new FakeAgent();
        var setup = MacSetup(agent);
        setup.TurnOnRelayCommand.Execute(null);
        var old = setup.RelayKey;

        setup.NewRelayKeyCommand.Execute(null);

        Assert.NotEqual(old, setup.RelayKey);
        Assert.Equal(setup.RelayKey, _store.Load().RelayHost.Key);
        Assert.Equal(1, agent.Restarts);
        Assert.Contains("needs the new one", setup.RelayStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void When_macos_will_not_start_it_the_screen_says_so_rather_than_claiming_it_is_on()
    {
        var setup = MacSetup(new FakeAgent { Refuse = "macOS would not start the background part of Yours Truly." });

        setup.TurnOnRelayCommand.Execute(null);

        Assert.False(setup.RelayOn);
        Assert.Contains("would not start", setup.RelayStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void Whether_it_is_on_comes_from_the_login_item_not_the_settings()
    {
        var agent = new FakeAgent();
        agent.TurnOn();

        Assert.True(MacSetup(agent).RelayOn);
    }

    [Fact]
    public void The_login_item_starts_this_app_in_relay_mode_and_restarts_it_unless_it_was_quit()
    {
        var plist = LaunchAgentRelay.PlistFor("/Applications/Yours & Truly.app/Contents/MacOS/YoursTruly.App");

        var doc = System.Xml.Linq.XDocument.Parse(plist);
        var strings = doc.Descendants("string").Select(e => e.Value).ToList();
        Assert.Contains(LaunchAgentRelay.Label, strings);
        Assert.Contains("/Applications/Yours & Truly.app/Contents/MacOS/YoursTruly.App", strings);
        Assert.Contains("--relay", strings);
        Assert.Contains("<key>SuccessfulExit</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>RunAtLoad</key>", plist, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
        catch (IOException) { /* a temp folder left behind harms nothing */ }
    }
}
