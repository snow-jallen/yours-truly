using System.Net;
using System.Text.Json;
using YoursTruly.Core.Domain;
using YoursTruly.Messaging;
using YoursTruly.Messaging.BlueBubbles;
using YoursTruly.Messaging.Settings;

namespace YoursTruly.Tests;

public sealed class BlueBubblesTests
{
    /// <summary>Stands in for BlueBubbles Server on the Mac. Answers the two things the
    /// app asks — can this number get iMessage, and send this — and records every
    /// request, because the shape of what reaches the Mac is the only part of this route
    /// that can be pinned down without a Mac, an Apple account and somebody's phone.</summary>
    private sealed class StubMac : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Received { get; } = [];

        /// <summary>The availability check only answers when the Private API is set up.
        /// Null means it is not: the server refuses the question with a 500.</summary>
        public bool? IMessageAvailable { get; init; }

        public Func<string, (HttpStatusCode, string)>? SendAnswer { get; init; }
        public HttpStatusCode? Everything { get; init; }
        public string EverythingBody { get; init; } = "";
        public Exception? Throws { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throws is not null) throw Throws;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Received.Add((request, body));

            if (Everything is { } status)
                return new HttpResponseMessage(status) { Content = new StringContent(EverythingBody) };

            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/handle/availability/imessage", StringComparison.Ordinal))
                return IMessageAvailable is { } available
                    ? Json(HttpStatusCode.OK, """{"status":200,"message":"Success","data":{"available":""" + (available ? "true" : "false") + "}}")
                    : Json(HttpStatusCode.InternalServerError,
                        """{"status":500,"message":"Please make sure you have completed the setup for the Private API, and your helper is connected!","error":{"type":"iMessage Error","message":"Private API is not enabled"}}""");

            if (path.EndsWith("/message/text", StringComparison.Ordinal))
            {
                var chat = JsonDocument.Parse(body!).RootElement.GetProperty("chatGuid").GetString()!;
                var (code, answer) = SendAnswer?.Invoke(chat) ?? (HttpStatusCode.OK, Sent);
                return Json(code, answer);
            }

            return Json(HttpStatusCode.NotFound, """{"status":404,"message":"Not found"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

        public IEnumerable<JsonElement> Sends =>
            Received.Where(r => r.Request.RequestUri!.AbsolutePath.EndsWith("/message/text", StringComparison.Ordinal))
                .Select(r => JsonDocument.Parse(r.Body!).RootElement);
    }

    private const string Sent =
        """{"status":200,"message":"Message sent!","data":{"guid":"5A1B-ABCD","text":"Dinner Friday at 6:30.","error":0}}""";

    /// <summary>Messages refused: BlueBubbles attaches the message Messages marked as
    /// failed, with Apple's error code on it.</summary>
    private const string NotDelivered =
        """{"status":500,"message":"Message Send Error","error":{"type":"iMessage Error","message":"Failed to send message! See attached message error code."},"data":{"guid":"5A1B-FAIL","error":22}}""";

    /// <summary>BlueBubbles gave up waiting for Messages: the same words, but no message.</summary>
    private const string GaveUpWaiting =
        """{"status":500,"message":"Message Send Error","error":{"type":"iMessage Error","message":"Failed to send message! See attached message error code."}}""";

    private static readonly BlueBubblesSettings Configured = new()
    {
        ServerUrl = "http://office-mac.example.ts.net:1234",
        Password = "correct horse",
    };

    private static BlueBubblesTextSender Sender(StubMac mac, BlueBubblesSettings? settings = null) =>
        new(new HttpClient(mac), settings ?? Configured);

    private static OutgoingMessage Message(string body = "Dinner Friday at 6:30.") => new("", body);

    [Fact]
    public async Task Asks_the_mac_to_send_in_the_shape_bluebubbles_expects()
    {
        var mac = new StubMac();
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Sent, outcome.Status);
        Assert.Equal("5A1B-ABCD", outcome.ProviderMessageId);

        var send = mac.Received.Single(r => r.Request.RequestUri!.AbsolutePath == "/api/v1/message/text");
        Assert.Equal(HttpMethod.Post, send.Request.Method);

        var sent = JsonDocument.Parse(send.Body!).RootElement;
        Assert.Equal("iMessage;-;+14355550101", sent.GetProperty("chatGuid").GetString());
        Assert.Equal("Dinner Friday at 6:30.", sent.GetProperty("message").GetString());
        Assert.Equal("apple-script", sent.GetProperty("method").GetString());

        // AppleScript sends are refused without one; it is how BlueBubbles tells two
        // identical messages apart.
        Assert.True(Guid.TryParse(sent.GetProperty("tempGuid").GetString(), out _));
    }

    [Fact]
    public async Task Signs_in_with_the_server_password_the_way_bluebubbles_reads_it()
    {
        var mac = new StubMac();
        await Sender(mac).SendAsync("+14355550101", Message());

        // BlueBubbles takes the password as a query parameter, not a header, so a space
        // in it has to survive the trip.
        foreach (var (request, _) in mac.Received)
            Assert.Contains("password=correct%20horse", request.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_message_gets_its_own_temporary_id()
    {
        var mac = new StubMac();
        var sender = Sender(mac);
        await sender.SendAsync("+14355550101", Message());
        await sender.SendAsync("+14355550101", Message());

        var ids = mac.Sends.Select(s => s.GetProperty("tempGuid").GetString()).ToList();
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public async Task Without_the_private_api_a_failed_imessage_is_tried_again_as_a_text()
    {
        var mac = new StubMac
        {
            SendAnswer = chat => chat.StartsWith("iMessage;", StringComparison.Ordinal)
                ? (HttpStatusCode.InternalServerError, NotDelivered)
                : (HttpStatusCode.OK, Sent),
        };

        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Sent, outcome.Status);
        Assert.Equal(
            new[] { "iMessage;-;+14355550101", "SMS;-;+14355550101" },
            mac.Sends.Select(s => s.GetProperty("chatGuid").GetString() ?? "").ToArray());
    }

    [Fact]
    public async Task With_the_private_api_somebody_without_imessage_goes_straight_to_a_text()
    {
        var mac = new StubMac { IMessageAvailable = false };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Sent, outcome.Status);
        var only = Assert.Single(mac.Sends);
        Assert.Equal("SMS;-;+14355550101", only.GetProperty("chatGuid").GetString());
        Assert.Equal("private-api", only.GetProperty("method").GetString());
    }

    [Fact]
    public async Task With_the_private_api_an_imessage_user_gets_an_imessage_and_nothing_is_retried()
    {
        var mac = new StubMac
        {
            IMessageAvailable = true,
            SendAnswer = _ => (HttpStatusCode.InternalServerError, NotDelivered),
        };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        // Apple said this number has iMessage, so a failure is a real failure — sending
        // it again as SMS would be guessing, and might deliver it twice.
        Assert.Equal(SendStatus.Failed, outcome.Status);
        var only = Assert.Single(mac.Sends);
        Assert.Equal("iMessage;-;+14355550101", only.GetProperty("chatGuid").GetString());
    }

    [Fact]
    public async Task The_availability_question_carries_the_number()
    {
        var mac = new StubMac { IMessageAvailable = true };
        await Sender(mac).SendAsync("+14355550101", Message());

        var asked = mac.Received.First().Request.RequestUri!;
        Assert.Equal("/api/v1/handle/availability/imessage", asked.AbsolutePath);
        Assert.Contains("address=%2B14355550101", asked.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_both_ways_fail_it_says_what_messages_said_without_the_error_code()
    {
        var mac = new StubMac { SendAnswer = _ => (HttpStatusCode.InternalServerError, NotDelivered) };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Failed, outcome.Status);
        Assert.Contains("Messages on your Mac would not send to +14355550101", outcome.Error!, StringComparison.Ordinal);
        Assert.Contains("Text Message Forwarding", outcome.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("500", outcome.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("22", outcome.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("error code", outcome.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_bluebubbles_gave_up_waiting_nothing_is_retried()
    {
        var mac = new StubMac { SendAnswer = _ => (HttpStatusCode.InternalServerError, GaveUpWaiting) };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Failed, outcome.Status);
        Assert.Contains("may still have sent it", outcome.Error!, StringComparison.Ordinal);
        Assert.Single(mac.Sends);
    }

    [Fact]
    public async Task A_request_this_computer_gave_up_on_is_not_retried_either()
    {
        var mac = new StubMac { Throws = new TaskCanceledException("timed out") };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Contains("may still have sent it", outcome.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Requests_are_given_longer_than_bluebubbles_waits_for_messages()
    {
        // BlueBubbles holds a send open for up to two minutes; HttpClient's default
        // 100 seconds would report a failure for a message that went out.
        Assert.True(BlueBubblesTextSender.Patience > TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData("http://office-mac.example.ts.net:1234/", "http://office-mac.example.ts.net:1234/api/v1/message/text")]
    [InlineData("office-mac.example.ts.net:1234", "http://office-mac.example.ts.net:1234/api/v1/message/text")]
    [InlineData("  https://abc.trycloudflare.com  ", "https://abc.trycloudflare.com/api/v1/message/text")]
    public async Task The_address_is_taken_the_way_people_paste_it(string typed, string expected)
    {
        var mac = new StubMac();
        await Sender(mac, Configured with { ServerUrl = typed }).SendAsync("+14355550101", Message());

        var send = mac.Received.Single(r => r.Request.RequestUri!.AbsolutePath.EndsWith("/message/text", StringComparison.Ordinal));
        Assert.Equal(expected, send.Request.RequestUri!.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task A_mac_that_is_asleep_or_unreachable_says_what_to_check()
    {
        var mac = new StubMac { Throws = new HttpRequestException("connection refused") };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Failed, outcome.Status);
        Assert.Contains("could not reach your Mac", outcome.Error!, StringComparison.Ordinal);
        Assert.Contains("BlueBubbles", outcome.Error!, StringComparison.Ordinal);
        Assert.Contains("changes every time", outcome.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_password_points_at_where_bluebubbles_shows_it()
    {
        var mac = new StubMac { Everything = HttpStatusCode.Unauthorized };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Contains("did not accept the password", outcome.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("401", outcome.Error!, StringComparison.Ordinal);

        // Nothing goes on to try sending once the password has been refused.
        Assert.Empty(mac.Sends);
    }

    [Fact]
    public async Task Too_many_wrong_passwords_says_to_wait()
    {
        var mac = new StubMac { Everything = HttpStatusCode.TooManyRequests };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Contains("wrong passwords", outcome.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("429", outcome.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_address_pointing_at_the_wrong_thing_says_so()
    {
        var mac = new StubMac { Everything = HttpStatusCode.NotFound, EverythingBody = "<html>nope</html>" };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Contains("not BlueBubbles", outcome.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData((HttpStatusCode)530)]
    public async Task A_tunnel_with_nothing_behind_it_reads_as_the_mac_being_unreachable(HttpStatusCode status)
    {
        var mac = new StubMac { Everything = status, EverythingBody = "<html>Cloudflare error</html>" };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Contains("could not reach your Mac", outcome.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_send_that_timed_out_warns_against_sending_it_again()
    {
        var mac = new StubMac { SendAnswer = _ => (HttpStatusCode.GatewayTimeout, """{"status":504,"message":"timeout"}""") };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.Equal(SendStatus.Failed, outcome.Status);
        Assert.Contains("may still have sent it", outcome.Error!, StringComparison.Ordinal);

        // A timeout is not a refusal — trying again as SMS could deliver it twice.
        Assert.Single(mac.Sends);
    }

    [Fact]
    public async Task Nothing_is_sent_before_the_mac_has_been_set_up()
    {
        var mac = new StubMac();
        var sender = Sender(mac, new BlueBubblesSettings());

        Assert.False(sender.IsConfigured);
        var outcome = await sender.SendAsync("+14355550101", Message());

        Assert.Contains("does not know where your Mac is", outcome.Error!, StringComparison.Ordinal);
        Assert.Empty(mac.Received);
    }

    [Fact]
    public async Task A_number_that_cannot_be_dialled_never_reaches_the_mac()
    {
        var mac = new StubMac();
        var outcome = await Sender(mac).SendAsync("555-0144", Message());

        Assert.Equal(SendStatus.Failed, outcome.Status);
        Assert.Empty(mac.Received);
    }

    [Fact]
    public async Task The_password_never_appears_in_what_the_user_is_shown()
    {
        var mac = new StubMac { Throws = new HttpRequestException("connection refused") };
        var outcome = await Sender(mac).SendAsync("+14355550101", Message());

        Assert.DoesNotContain("correct horse", outcome.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("correct%20horse", outcome.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_test_button_sends_through_the_mac_to_the_users_own_number()
    {
        var mac = new StubMac();
        var check = await Sender(mac).TestAsync("+14355550164");

        Assert.True(check.Ok);
        Assert.Equal("iMessage;-;+14355550164", mac.Sends.First().GetProperty("chatGuid").GetString());
    }

    [Fact]
    public async Task The_test_button_asks_for_your_own_number_first()
    {
        var mac = new StubMac();
        var check = await Sender(mac).TestAsync("");

        Assert.False(check.Ok);
        Assert.Empty(mac.Received);
    }

    [Fact]
    public void It_is_a_text_sender_like_any_other()
    {
        Assert.Equal(Channel.Text, Sender(new StubMac()).Channel);
    }

    [Fact]
    public void It_sends_from_the_users_own_number_so_it_is_paced()
    {
        Assert.True(TextTransport.BlueBubbles.NeedsPacing());
    }
}
