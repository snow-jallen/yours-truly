using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using YoursTruly.Core.Domain;
using YoursTruly.Messaging;
using YoursTruly.Messaging.Relay;
using YoursTruly.Messaging.Settings;

namespace YoursTruly.Tests;

/// <summary>The Mac end and the asking end of "Let my other computers text through this
/// Mac", run against each other for real over this machine's loopback. Only the Messages
/// app itself is stood in for.</summary>
public sealed class RelayTests : IAsyncDisposable
{
    /// <summary>Stands in for the Mac's Messages app.</summary>
    private sealed class StubMessages : IMessageSender
    {
        public List<(string To, string Text)> Sent { get; } = [];
        public string? Refuse { get; init; }
        public Channel Channel => Channel.Text;
        public bool IsConfigured => true;

        public Task<SendOutcome> SendAsync(string address, OutgoingMessage message, CancellationToken cancellation = default)
        {
            if (Refuse is not null) return Task.FromResult(SendOutcome.Failed(Refuse));
            Sent.Add((address, message.Body));
            return Task.FromResult(SendOutcome.Sent(null));
        }

        public Task<CredentialCheck> TestAsync(string address, CancellationToken cancellation = default) =>
            throw new NotSupportedException();
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private const string Key = "ABCDE-FGHJK-MNPQR-STUVW-XYZ23";

    private readonly List<RelayServer> _servers = [];

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private RelayServer Mac(
        IMessageSender messages, SendAllowance? allowance = null, TimeProvider? clock = null,
        Func<IPAddress, bool>? allowed = null)
    {
        var server = new RelayServer(Key, messages, allowance, FreePort(), "localhost", clock, allowed, "1.0.99");
        server.Start();
        _servers.Add(server);
        return server;
    }

    private static RelayTextSender Asker(RelayServer mac, string key = Key) =>
        new(new HttpClient(), new MacRelaySettings { Address = $"http://localhost:{mac.Port}", Key = key });

    private static OutgoingMessage Message(string body = "Dinner Friday at 6:30.") => new("", body);

    // --- end to end ------------------------------------------------------------------

    [Fact]
    public async Task A_text_asked_for_on_one_computer_is_sent_by_the_macs_messages_app()
    {
        var messages = new StubMessages();
        var mac = Mac(messages);

        var outcome = await Asker(mac).SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Sent, outcome.Status);
        Assert.Equal(("+14355550101", "Dinner Friday at 6:30."), Assert.Single(messages.Sent));
    }

    [Fact]
    public async Task A_wrong_key_sends_nothing_and_says_where_to_find_the_right_one()
    {
        var messages = new StubMessages();
        var mac = Mac(messages);

        var outcome = await Asker(mac, "WRONG-WRONG-WRONG-WRONG-WRONG").SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Failed, outcome.Status);
        Assert.Contains("did not accept the key", outcome.Error!, StringComparison.Ordinal);
        Assert.Empty(messages.Sent);
    }

    [Fact]
    public async Task The_key_is_forgiving_about_case_and_dashes_but_nothing_else()
    {
        var messages = new StubMessages();
        var mac = Mac(messages);

        var outcome = await Asker(mac, " abcdefghjkmnpqrstuvwxyz23 ").SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Sent, outcome.Status);
    }

    [Fact]
    public async Task Too_many_wrong_keys_locks_that_computer_out_even_with_the_right_one()
    {
        var messages = new StubMessages();
        var mac = Mac(messages);

        for (var i = 0; i < RelayServer.WrongKeysAllowed; i++)
            await Asker(mac, "WRONG").SendAsync("+14355550101", Message());

        var outcome = await Asker(mac).SendAsync("+14355550101", Message());

        Assert.Contains("too many wrong keys", outcome.Error!, StringComparison.Ordinal);
        Assert.Empty(messages.Sent);
    }

    [Fact]
    public async Task The_lockout_lifts_after_a_while()
    {
        var clock = new Clock(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));
        var messages = new StubMessages();
        var mac = Mac(messages, clock: clock);

        for (var i = 0; i < RelayServer.WrongKeysAllowed; i++)
            await Asker(mac, "WRONG").SendAsync("+14355550101", Message());

        clock.Now += RelayServer.LockoutWindow;
        var outcome = await Asker(mac).SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Sent, outcome.Status);
    }

    [Fact]
    public async Task A_request_from_outside_the_network_is_refused_before_the_key_is_looked_at()
    {
        var messages = new StubMessages();
        var mac = Mac(messages, allowed: _ => false);

        var outcome = await Asker(mac).SendAsync("+14355550101", Message());

        Assert.Contains("own network or on your Tailscale network", outcome.Error!, StringComparison.Ordinal);
        Assert.Empty(messages.Sent);
    }

    [Fact]
    public async Task While_paused_nothing_goes_and_it_says_how_to_resume()
    {
        var messages = new StubMessages();
        var mac = Mac(messages);
        mac.Paused = true;

        var outcome = await Asker(mac).SendAsync("+14355550101", Message());

        Assert.Contains("choose Resume", outcome.Error!, StringComparison.Ordinal);
        Assert.Empty(messages.Sent);
    }

    [Fact]
    public async Task Past_the_hourly_allowance_it_stops_and_says_when_the_rest_can_go()
    {
        var messages = new StubMessages();
        var mac = Mac(messages, new SendAllowance(perHour: 2, perDay: 10));
        var asker = Asker(mac);

        await asker.SendAsync("+14355550101", Message());
        await asker.SendAsync("+14355550102", Message());
        var third = await asker.SendAsync("+14355550103", Message());

        Assert.Equal(2, messages.Sent.Count);
        Assert.Contains("sent 2 texts in the last hour", third.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_messages_on_the_mac_refuses_its_own_words_come_through()
    {
        var messages = new StubMessages
        {
            Refuse = "The Messages app is not running. Open Messages, make sure you are signed in, and try again.",
        };
        var mac = Mac(messages);

        var outcome = await Asker(mac).SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Failed, outcome.Status);
        Assert.Contains("On the Mac: The Messages app is not running", outcome.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_text_is_counted_for_the_menu_bar()
    {
        var messages = new StubMessages();
        var mac = Mac(messages);
        var handled = 0;
        mac.TextHandled += _ => Interlocked.Increment(ref handled);

        await Asker(mac).SendAsync("+14355550101", Message());

        Assert.Equal(1, handled);
        Assert.Equal(1, mac.Allowance.SentToday);
    }

    [Fact]
    public async Task Texts_from_two_computers_at_once_go_out_one_at_a_time()
    {
        var inside = 0;
        var most = 0;
        var slow = new SlowMessages(() =>
        {
            var now = Interlocked.Increment(ref inside);
            most = Math.Max(most, now);
        }, () => Interlocked.Decrement(ref inside));
        var mac = Mac(slow);

        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(i => Asker(mac).SendAsync($"+1435555010{i}", Message())));

        Assert.Equal(1, most);
    }

    private sealed class SlowMessages(Action enter, Action leave) : IMessageSender
    {
        public Channel Channel => Channel.Text;
        public bool IsConfigured => true;

        public async Task<SendOutcome> SendAsync(string address, OutgoingMessage message, CancellationToken cancellation = default)
        {
            enter();
            await Task.Delay(50, cancellation);
            leave();
            return SendOutcome.Sent(null);
        }

        public Task<CredentialCheck> TestAsync(string address, CancellationToken cancellation = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task The_ping_needs_the_key_too()
    {
        var mac = Mac(new StubMessages());
        using var http = new HttpClient();

        var anonymous = await http.GetAsync($"http://localhost:{mac.Port}{RelayProtocol.PingPath}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{mac.Port}{RelayProtocol.PingPath}");
        request.Headers.Authorization = new("Bearer", Key);
        var answer = await (await http.SendAsync(request)).Content.ReadFromJsonAsync<RelayAnswer>();
        Assert.Equal("1.0.99", answer!.Version);
    }

    [Fact]
    public async Task A_request_too_big_or_malformed_is_refused_without_sending()
    {
        var messages = new StubMessages();
        var mac = Mac(messages);
        using var http = new HttpClient();

        foreach (var body in new[] { "not json", """{"to":"+14355550101"}""", $$"""{"to":"+14355550101","text":"{{new string('x', RelayProtocol.LongestText + 1)}}"}""" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{mac.Port}{RelayProtocol.TextPath}")
            {
                Content = new StringContent(body),
            };
            request.Headers.Authorization = new("Bearer", Key);
            var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty(messages.Sent);
    }

    [Fact]
    public async Task A_mac_that_is_not_there_says_what_to_check()
    {
        var asker = new RelayTextSender(new HttpClient(),
            new MacRelaySettings { Address = $"http://localhost:{FreePort()}", Key = Key });

        var outcome = await asker.SendAsync("+14355550101", Message());

        Assert.Contains("could not reach your Mac", outcome.Error!, StringComparison.Ordinal);
        Assert.Contains("Tailscale", outcome.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, outcome.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_sent_before_the_mac_has_been_set_up()
    {
        var outcome = await new RelayTextSender(new HttpClient(), new MacRelaySettings())
            .SendAsync("+14355550101", Message());

        Assert.Contains("does not know where your Mac is", outcome.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_test_button_sends_through_the_mac_to_the_users_own_number()
    {
        var messages = new StubMessages();
        var mac = Mac(messages);

        var check = await Asker(mac).TestAsync("+14355550164");

        Assert.True(check.Ok);
        Assert.Equal("+14355550164", Assert.Single(messages.Sent).To);
    }

    // --- the address as people type it ----------------------------------------------

    private sealed class Recorder : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new RelayAnswer { Sent = true }),
            });
        }
    }

    [Theory]
    [InlineData("http://office-mac:47474", "http://office-mac:47474/yours-truly/text")]
    [InlineData("office-mac", "http://office-mac:47474/yours-truly/text")]
    [InlineData("  office-mac:5000/ ", "http://office-mac:5000/yours-truly/text")]
    [InlineData("100.84.1.5", "http://100.84.1.5:47474/yours-truly/text")]
    [InlineData("http://[fd7a:115c:a1e0::1]:47474", "http://[fd7a:115c:a1e0::1]:47474/yours-truly/text")]
    public async Task The_address_is_taken_the_way_people_paste_it(string typed, string expected)
    {
        var recorder = new Recorder();
        await new RelayTextSender(new HttpClient(recorder), new MacRelaySettings { Address = typed, Key = Key })
            .SendAsync("+14355550101", Message());

        Assert.Equal(expected, recorder.Asked!.ToString());
    }

    [Fact]
    public void It_sends_from_the_users_own_number_so_it_is_paced()
    {
        Assert.True(TextTransport.MacRelay.NeedsPacing());
    }

    // --- the pieces --------------------------------------------------------------------

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.9", true)]
    [InlineData("172.32.0.9", false)]
    [InlineData("192.168.1.20", true)]
    [InlineData("100.84.224.121", true)]   // Tailscale
    [InlineData("100.128.0.1", false)]     // just past Tailscale's range
    [InlineData("fd7a:115c:a1e0::1", true)] // Tailscale, IPv6
    [InlineData("fe80::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2001:4860:4860::8888", false)]
    [InlineData("::ffff:192.168.1.20", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void Only_this_network_and_tailscale_are_private(string address, bool expected)
    {
        Assert.Equal(expected, NetworkScope.IsPrivate(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("2601:681:4100:12a0::5", "2601:681:4100:12a0:abcd::9", 64, true)]
    [InlineData("2601:681:4100:12a0::5", "2601:681:4100:12a1::5", 64, false)]
    [InlineData("144.38.10.7", "144.38.10.200", 24, true)]
    [InlineData("144.38.11.7", "144.38.10.200", 24, false)]
    [InlineData("144.38.10.7", "144.38.10.200", 30, false)]
    [InlineData("::ffff:144.38.10.7", "144.38.10.200", 24, true)]
    [InlineData("144.38.10.7", "2601:681:4100:12a0::5", 24, false)]
    public void Next_door_is_the_same_subnet_as_one_of_the_macs_own(string from, string mine, int prefix, bool expected)
    {
        Assert.Equal(expected, NetworkScope.SameSubnet(IPAddress.Parse(from), IPAddress.Parse(mine), prefix));
    }

    [Fact]
    public void Private_addresses_are_always_nearby()
    {
        Assert.True(NetworkScope.IsNearby(IPAddress.Parse("100.84.224.121")));
        Assert.True(NetworkScope.IsNearby(IPAddress.Loopback));
    }

    [Fact]
    public void A_new_key_is_readable_and_never_the_same_twice()
    {
        var key = RelayKey.New();

        Assert.Matches("^[A-Z2-9]{5}(-[A-Z2-9]{5}){4}$", key);
        Assert.DoesNotContain(key, c => c is '0' or 'O' or '1' or 'I' or 'L');
        Assert.NotEqual(key, RelayKey.New());
    }

    [Fact]
    public void An_empty_key_matches_nothing_not_even_another_empty_one()
    {
        Assert.False(RelayKey.Matches("", ""));
        Assert.False(RelayKey.Matches(null, "  "));
        Assert.Throws<ArgumentException>(() => new RelayServer("", new StubMessages()));
    }

    [Fact]
    public void The_daily_allowance_is_counted_over_a_rolling_day()
    {
        var clock = new Clock(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));
        var allowance = new SendAllowance(perHour: 100, perDay: 3, clock: clock);

        for (var i = 0; i < 3; i++) Assert.True(allowance.TryTake(out _, out _));
        Assert.False(allowance.TryTake(out var reason, out var limit));
        Assert.Equal((RelayReasons.Day, 3), (reason, limit));

        clock.Now += TimeSpan.FromDays(1);
        Assert.True(allowance.TryTake(out _, out _));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var server in _servers) await server.DisposeAsync();
    }
}
