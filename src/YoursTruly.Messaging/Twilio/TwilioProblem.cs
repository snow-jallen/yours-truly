using System.Net.Sockets;
using YoursTruly.Messaging.Settings;
using Twilio.Exceptions;

namespace YoursTruly.Messaging.Twilio;

/// <summary>Turns Twilio's failures into the sentence the user sees. Twilio answers
/// with numbers — 21211, 20003 — which tell the people this app is for exactly
/// nothing. Every one of these says what happened and what they can do about it.</summary>
internal static class TwilioProblem
{
    public static string Explain(Exception failure, string address, TwilioSettings settings) => failure switch
    {
        ApiException api => ExplainApi(api, address, settings),

        SocketException or HttpRequestException or TimeoutException =>
            "Yours Truly could not reach Twilio. Check that this computer is connected to the internet, then try again.",

        _ => $"Yours Truly could not reach {address}, and does not recognise the reason. Twilio said: {failure.Message}",
    };

    private static string ExplainApi(ApiException api, string address, TwilioSettings settings) => api.Code switch
    {
        20003 =>
            "Twilio would not accept your account details. Check the Account SID and Auth Token on the Setup screen against the ones on your Twilio dashboard — the token is easy to copy short.",

        21211 or 21214 =>
            $"Twilio does not recognise {address} as a phone number. Check it on this person's page, or reach them another way.",

        21408 or 21215 =>
            $"Your Twilio account is not allowed to reach {address}. If that number is outside the United States, turn its region on in the Twilio console under Geo Permissions.",

        21606 or 21605 =>
            $"Twilio will not send from {settings.FromNumber}. Make sure the number on the Setup screen is one you bought in Twilio and that it can send messages.",

        21608 => TrialRestriction(address, settings),

        21610 =>
            $"{address} has replied STOP to your Twilio number, so Twilio will not deliver to them. They have to text START to that number before Yours Truly can reach them again.",

        21612 or 30003 or 30005 =>
            $"{address} could not be reached — the number may be switched off, disconnected, or unable to receive messages. Try phoning instead.",

        21614 =>
            $"{address} is a landline, so it cannot receive a text. Set this person to a phone call instead.",

        20429 =>
            "Twilio is asking Yours Truly to slow down. Everything sent so far has gone out; wait a minute and send the rest.",

        20404 =>
            "Twilio could not find that recording any more. Record your message again on the Send screen.",

        _ when api.Message.Contains("balance", StringComparison.OrdinalIgnoreCase) =>
            "Your Twilio account is out of credit. Add funds in the Twilio console, then send again — nothing was charged for this attempt.",

        _ when IsATrialRestriction(api.Message) => TrialRestriction(address, settings),

        _ => $"Twilio refused to reach {address}. It said: {api.Message}",
    };

    /// <summary>A trial account refusing an unverified number, whichever way Twilio has
    /// worded it today.
    ///
    /// 21608 is the documented code and is matched above, but Twilio has more than one.
    /// A trial with no number of its own answers "No Twilio trial phone number is
    /// assigned for messaging to this destination number. Please add the 'to' number as
    /// a verified recipient" under a different code entirely, and that went straight to
    /// the fallback and put Twilio's own sentence in front of the user. Matching what it
    /// says rather than the number it says it under survives the next renumbering.</summary>
    private static bool IsATrialRestriction(string message) =>
        message.Contains("trial", StringComparison.OrdinalIgnoreCase)
        && message.Contains("verif", StringComparison.OrdinalIgnoreCase);

    /// <summary>What to actually do about a trial account. Longer than the rest of these
    /// on purpose: the others are one thing gone wrong, and this is a setup that has
    /// never worked yet, so it is worth naming every screen.</summary>
    private static string TrialRestriction(string address, TwilioSettings settings) =>
        $"Your Twilio account is still a trial, and a trial can only text numbers you have verified. "
        + $"In the Twilio console open Phone Numbers → Manage → Verified Caller IDs, add {address}, "
        + "and choose SMS as the way to verify it — you will need that phone to read the code. "
        + "A trial also needs a Twilio number of its own that can send texts: check Phone Numbers → "
        + "Manage → Active numbers, and that the number on the Setup screen is one of them."
        + (settings.SendsRichText
            ? " One more thing, because it is the likeliest cause here: Yours Truly is set to send through "
              + "a Messaging Service, which ignores that number and uses the service's own Sender Pool. If "
              + "the pool is empty there is nothing to send from. Clear the Messaging Service SID on the "
              + "Setup screen to get plain texts working first, then put it back."
            : "");
}
