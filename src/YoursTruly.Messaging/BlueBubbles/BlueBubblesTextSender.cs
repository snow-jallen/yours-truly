using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using YoursTruly.Core.Domain;
using YoursTruly.Messaging.Settings;
using YoursTruly.Messaging.Twilio;

namespace YoursTruly.Messaging.BlueBubbles;

/// <summary>Sends texts by asking BlueBubbles Server, on a Mac signed in to the user's
/// Apple account, to have that Mac's Messages app send them.
///
/// The same arrangement as <see cref="Mac.MessagesTextSender"/> — the message really
/// comes from the user's own number and replies arrive in their own Messages — except
/// that the Mac is reached over the network. So Yours Truly can run on Windows or Linux,
/// or at home while the Mac is at the office, as long as the address reaches it. iMessages
/// go out from the Mac itself; texts to people without iMessage are relayed through the
/// user's iPhone by Text Message Forwarding, so the phone has to be on and online, but
/// not anywhere near the Mac.
///
/// What the API looks like is BlueBubbles Server's own: the server password as a query
/// parameter, a chat named "iMessage;-;+14355550101" or "SMS;-;+14355550101", and a
/// temporary id the server uses to match the sent message up again.</summary>
public sealed class BlueBubblesTextSender(HttpClient http, BlueBubblesSettings settings) : IMessageSender
{
    /// <summary>How long one request may take. BlueBubbles answers a send only once the
    /// message has appeared in Messages, and waits up to two minutes for that; the
    /// default HttpClient gives up at 100 seconds, which would report a failure for a
    /// message that went out a few seconds later.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(2.5);

    /// <summary>What BlueBubbles says when it gave up waiting for Messages, as opposed to
    /// Messages refusing. After this the message may still go, so it is never retried.</summary>
    private const string WaitedTooLong = "See attached message error code";

    public Channel Channel => Channel.Text;

    public bool IsConfigured => settings.IsComplete;

    public async Task<SendOutcome> SendAsync(
        string address, OutgoingMessage message, CancellationToken cancellation = default)
    {
        if (!IsConfigured)
            return SendOutcome.Failed(
                "Yours Truly does not know where your Mac is yet. Copy the address and the server password from BlueBubbles Server on that Mac onto the Setup screen.");

        if (!PhoneNumbers.IsDiallable(address))
            return SendOutcome.Failed(PhoneNumbers.Complaint(address));

        var number = address.Trim();
        try
        {
            var asked = await AskWhichServiceAsync(number, cancellation);
            if (asked.Refusal is { } refused) return SendOutcome.Failed(refused);

            // Apple answered, so there is exactly one right way to send. Without the
            // Private API there is no asking, so iMessage is tried and a refusal falls
            // back to a text — the same order the Messages app on this Mac uses.
            string[] services = asked.Service is { } known ? [known] : ["iMessage", "SMS"];
            var method = asked.Service is null ? "apple-script" : "private-api";

            SendOutcome outcome = SendOutcome.Failed("Nothing was sent.");
            for (var i = 0; i < services.Length; i++)
            {
                var (result, worthRetrying) = await SendOnceAsync(services[i], number, message.Body, method, cancellation);
                outcome = result;
                if (result.Status == SendStatus.Sent || !worthRetrying) break;
            }
            return outcome;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return SendOutcome.Failed(TookTooLong(number));
        }
        catch (HttpRequestException)
        {
            return SendOutcome.Failed(Unreachable);
        }
        catch (Exception)
        {
            // Not the exception's own words: they can quote the request, and the request
            // carries the server password.
            return SendOutcome.Failed(
                $"Yours Truly could not reach BlueBubbles at {settings.ServerUrl.Trim()}. Check the address on the Setup screen.");
        }
    }

    public async Task<CredentialCheck> TestAsync(string address, CancellationToken cancellation = default)
    {
        if (!IsConfigured)
            return CredentialCheck.Broken("Fill in the address and server password from BlueBubbles Server first, then try again.");

        if (string.IsNullOrWhiteSpace(address))
            return CredentialCheck.Broken("Add your own mobile number on the Setup screen so Yours Truly has somewhere to send the test.");

        var outcome = await SendAsync(
            address,
            new OutgoingMessage("", "This is a test from Yours Truly, sent through BlueBubbles on your Mac. Nobody else was sent anything."),
            cancellation);

        return outcome.Status == SendStatus.Sent
            ? CredentialCheck.Working($"Your Mac sent a test message to {address}. It should appear in your own conversation with yourself.")
            : CredentialCheck.Broken(outcome.Error ?? "Your Mac would not send the test message.");
    }

    // --- the two requests ----------------------------------------------------------

    private sealed record Asked(string? Service, string? Refusal);

    /// <summary>Asks Apple, through BlueBubbles, whether this number has iMessage. Only
    /// the Private API can answer; without it the server refuses the question, which
    /// means "don't know" rather than a failure. A wrong password or an unreachable Mac
    /// is a failure, and is reported before anything is sent.</summary>
    private async Task<Asked> AskWhichServiceAsync(string number, CancellationToken cancellation)
    {
        using var response = await http.GetAsync(
            Endpoint("handle/availability/imessage", ("address", number)), cancellation);

        if (Refusal(response.StatusCode) is { } refused) return new Asked(null, refused);
        if (response.StatusCode != HttpStatusCode.OK) return new Asked(null, null);

        var body = await response.Content.ReadAsStringAsync(cancellation);
        try
        {
            var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
            return data.GetProperty("available").ValueKind switch
            {
                JsonValueKind.True => new Asked("iMessage", null),
                JsonValueKind.False => new Asked("SMS", null),
                _ => new Asked(null, null),
            };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new Asked(null, null);
        }
    }

    /// <summary>One attempt over one service. Worth retrying only when Messages itself
    /// refused — never after a timeout, when the message may yet go out and a second
    /// attempt as SMS would deliver it twice.</summary>
    private async Task<(SendOutcome Outcome, bool WorthRetrying)> SendOnceAsync(
        string service, string number, string body, string method, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("message/text"))
        {
            Content = JsonContent.Create(new
            {
                chatGuid = $"{service};-;{number}",
                tempGuid = Guid.NewGuid().ToString(),
                message = body,
                method,
            }),
        };

        using var response = await http.SendAsync(request, cancellation);
        var answer = await response.Content.ReadAsStringAsync(cancellation);

        if (response.IsSuccessStatusCode) return (SendOutcome.Sent(IdFrom(answer)), false);
        if (Refusal(response.StatusCode) is { } refused) return (SendOutcome.Failed(refused), false);
        if (response.StatusCode == HttpStatusCode.GatewayTimeout) return (SendOutcome.Failed(TookTooLong(number)), false);

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            if (GaveUpWaiting(answer)) return (SendOutcome.Failed(TookTooLong(number)), false);
            return (SendOutcome.Failed(
                $"Messages on your Mac would not send to {number}. If that person has no iMessage, your iPhone has to be on, online, and set up to forward text messages to that Mac — on the phone, Settings, Apps, Messages, Text Message Forwarding."),
                true);
        }

        // What BlueBubbles said is not passed on: it is an error code and a stack of
        // jargon, and the one thing it nearly always means is an out-of-date server.
        return (SendOutcome.Failed(
            $"Your Mac would not send to {number}. Open BlueBubbles Server on the Mac, check it is running and up to date, and try again."), false);
    }

    // --- what goes on the wire -----------------------------------------------------

    /// <summary>The address as BlueBubbles Server shows it, tolerating what pasting
    /// does to it: spaces either side, a trailing slash, no http:// on the front.</summary>
    private string Root
    {
        get
        {
            var root = settings.ServerUrl.Trim().TrimEnd('/');
            return root.Contains("://", StringComparison.Ordinal) ? root : $"http://{root}";
        }
    }

    private string Endpoint(string path, params (string Name, string Value)[] query)
    {
        var parameters = query.Append(("password", settings.Password))
            .Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}");
        return $"{Root}/api/v1/{path}?{string.Join('&', parameters)}";
    }

    // --- what comes back -----------------------------------------------------------

    /// <summary>The answers that mean nothing will work until something is fixed, so no
    /// point trying the other service.</summary>
    private string? Refusal(HttpStatusCode status) => (int)status switch
    {
        401 or 403 =>
            "BlueBubbles on your Mac did not accept the password. It is the server password in BlueBubbles Server's settings — copy it onto the Setup screen again.",
        429 =>
            "BlueBubbles has stopped answering this computer for a few minutes after too many wrong passwords. Check the password on the Setup screen, wait a little, and try again.",
        404 =>
            "That address reached something, but not BlueBubbles. Check it matches the address BlueBubbles Server shows, including the port — usually 1234.",
        502 or 503 or 520 or 521 or 522 or 523 or 524 or 530 => Unreachable,
        _ => null,
    };

    private string Unreachable =>
        $"Yours Truly could not reach your Mac at {settings.ServerUrl.Trim()}. Check the Mac is awake, BlueBubbles Server is running on it, and the address is the one BlueBubbles shows now — a Cloudflare address changes every time BlueBubbles restarts.";

    private static string TookTooLong(string number) =>
        $"Your Mac took too long to answer about {number}. Messages may still have sent it — look in Messages on the Mac before sending to this person again.";

    /// <summary>True when BlueBubbles stopped waiting for Messages rather than Messages
    /// refusing: it then has no message to attach, where a refusal comes with the
    /// message Messages marked as failed.</summary>
    private static bool GaveUpWaiting(string body)
    {
        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            var noMessage = !root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null;
            return noMessage
                   && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                   && error.TryGetProperty("message", out var text)
                   && (text.GetString() ?? "").Contains(WaitedTooLong, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? IdFrom(string body)
    {
        try
        {
            return JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("guid").GetString();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
