using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using YoursTruly.Core.Domain;
using YoursTruly.Messaging.Settings;
using YoursTruly.Messaging.Twilio;

namespace YoursTruly.Messaging.Relay;

/// <summary>Sends texts by asking Yours Truly on a Mac — one with "Let my other computers
/// text through this Mac" switched on — to send them from that Mac's Messages app.
///
/// The same arrangement as sending through Messages on this Mac, except the Mac is
/// reached over the network: so this works from Windows or Linux, or from home while
/// the Mac is at the office. iMessages go from the Mac itself; texts to people without
/// iMessage are relayed through the user's iPhone, which has to be on and online but
/// need not be anywhere near the Mac.</summary>
public sealed class RelayTextSender(HttpClient http, MacRelaySettings settings) : IMessageSender
{
    /// <summary>How long one request may take. The Mac sends one text at a time and
    /// leaves a short gap after each, so a request can wait behind another computer's.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    public Channel Channel => Channel.Text;

    public bool IsConfigured => settings.IsComplete;

    public async Task<SendOutcome> SendAsync(
        string address, OutgoingMessage message, CancellationToken cancellation = default)
    {
        if (!IsConfigured)
            return SendOutcome.Failed(
                "Yours Truly does not know where your Mac is yet. On the Mac, open Yours Truly's Setup, switch on “Let my other computers text through this Mac”, and copy the address and key it shows onto this computer's Setup screen.");

        if (!PhoneNumbers.IsDiallable(address))
            return SendOutcome.Failed(PhoneNumbers.Complaint(address));

        var number = address.Trim();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Root}{RelayProtocol.TextPath}")
            {
                Content = JsonContent.Create(new RelayTextRequest(number, message.Body)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Key.Trim());

            using var response = await http.SendAsync(request, cancellation);
            var answer = await ReadAsync(response, cancellation);

            return response.StatusCode == HttpStatusCode.OK && answer?.Sent == true
                ? SendOutcome.Sent(null)
                : SendOutcome.Failed(Explain(response.StatusCode, answer, number));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return SendOutcome.Failed(
                $"Your Mac took too long to answer about {number}. It may still have sent it — look in Messages on the Mac before sending to this person again.");
        }
        catch (HttpRequestException)
        {
            return SendOutcome.Failed(Unreachable);
        }
        catch (Exception)
        {
            return SendOutcome.Failed(
                $"Yours Truly could not reach your Mac at {settings.Address.Trim()}. Check the address on the Setup screen.");
        }
    }

    public async Task<CredentialCheck> TestAsync(string address, CancellationToken cancellation = default)
    {
        if (!IsConfigured)
            return CredentialCheck.Broken("Fill in your Mac's address and key first, then try again.");

        if (string.IsNullOrWhiteSpace(address))
            return CredentialCheck.Broken("Add your own mobile number on the Setup screen so Yours Truly has somewhere to send the test.");

        var outcome = await SendAsync(
            address,
            new OutgoingMessage("", "This is a test from Yours Truly, sent through Messages on your Mac. Nobody else was sent anything."),
            cancellation);

        return outcome.Status == SendStatus.Sent
            ? CredentialCheck.Working($"Your Mac sent a test message to {address}. It should appear in your own conversation with yourself.")
            : CredentialCheck.Broken(outcome.Error ?? "Your Mac would not send the test message.");
    }

    /// <summary>The address as people paste it: spaces either side, a trailing slash, no
    /// http:// on the front, or no port — in which case it is the relay's usual one.</summary>
    private string Root
    {
        get
        {
            var root = settings.Address.Trim().TrimEnd('/');
            if (!root.Contains("://", StringComparison.Ordinal)) root = $"http://{root}";
            var saysPort = System.Text.RegularExpressions.Regex.IsMatch(
                root, @"^[a-zA-Z]+://(\[[^\]]+\]|[^/:]+):\d+");
            return saysPort
                ? root
                : new UriBuilder(root) { Port = RelayProtocol.DefaultPort }.Uri.ToString().TrimEnd('/');
        }
    }

    private string Unreachable =>
        $"Yours Truly could not reach your Mac at {settings.Address.Trim()}. Check the Mac is awake and logged in, that “Let my other computers text through this Mac” is still on in its Setup, and that this computer is on the same network or the same Tailscale network as the Mac.";

    private static string Explain(HttpStatusCode status, RelayAnswer? answer, string number) => answer?.Reason switch
    {
        RelayReasons.WrongKey =>
            "Your Mac did not accept the key. Copy it again from Setup on the Mac — it changes if somebody pressed “Make a new key” there.",
        RelayReasons.LockedOut =>
            "Your Mac has stopped answering this computer for a few minutes after too many wrong keys. Check the key on the Setup screen, wait ten minutes, and try again.",
        RelayReasons.NotPrivate =>
            "Your Mac only answers computers on its own network or on your Tailscale network, and this computer is neither. Join the same Wi-Fi as the Mac, or put both on Tailscale.",
        RelayReasons.Paused =>
            "Your Mac has paused texting for other computers. Click the Yours Truly icon in the Mac's menu bar and choose Resume.",
        RelayReasons.Hour =>
            $"Your Mac has sent {answer.Limit} texts in the last hour, the most it allows, so it stopped here to keep your number from being flagged as spam. Send the rest in an hour.",
        RelayReasons.Day =>
            $"Your Mac has sent {answer.Limit} texts today, the most it allows, so it stopped here to keep your number from being flagged as spam. Send the rest tomorrow.",
        RelayReasons.BadRequest =>
            "Your Mac did not understand the request. Update Yours Truly on both computers to the same version.",
        _ when answer?.Sent == false && answer.Error is { Length: > 0 } said =>
            $"Your Mac could not send to {number}. On the Mac: {said}",
        _ => status == HttpStatusCode.NotFound || answer is null
            ? $"That address reached something, but not Yours Truly on a Mac. Check it matches what the Mac's Setup screen shows, including the port — usually {RelayProtocol.DefaultPort}."
            : $"Your Mac would not send to {number}. Update Yours Truly on both computers and try again.",
    };

    private static async Task<RelayAnswer?> ReadAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<RelayAnswer>(cancellation);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
