namespace YoursTruly.Core.Domain;

/// <summary>How hard this send is leaning on Gmail's daily limit.</summary>
public enum QuotaPressure
{
    /// <summary>Plenty of room. Worth showing, not worth mentioning.</summary>
    Fine,

    /// <summary>Close enough that the next send might not fit.</summary>
    Close,

    /// <summary>This send would go past the limit, and Gmail would stop part way.</summary>
    Over,
}

/// <summary>What Gmail lets one account send in a day.
///
/// Worth knowing because the failure is nasty: Gmail does not slow you down as you
/// approach the limit, it refuses outright, and it then refuses everything else for up
/// to 24 hours. A send that stops two thirds of the way through leaves the user with no
/// way to finish it and no obvious way to find out why.
///
/// Two numbers, both from Google:
///
/// * A free account is <b>500 messages a day</b>.
///   https://support.google.com/mail/answer/22839
/// * A Google Workspace account is <b>2,000 a day</b> — except a trial account, which is
///   500 and which nothing here can detect.
///   https://knowledge.workspace.google.com/admin/gmail/gmail-sending-limits-in-google-workspace
///
/// Google's other published caps do not bite here. The limits on recipients per message
/// (100 over SMTP) and on external recipients per day exist because most senders put
/// many people in one message. Yours Truly gives every person their own message with
/// their own address in To — which is what stops 427 people seeing each other's
/// addresses, and is not a thing to trade away for quota — so one message is one
/// recipient, and the message count is the only one that runs out.
///
/// The window is a rolling 24 hours, not midnight — so this counts back 24 hours from
/// now rather than from the start of the day.</summary>
public sealed record GmailQuota(int Limit, string Plan)
{
    public const int FreeAccount = 500;
    public const int Workspace = 2_000;

    /// <summary>How long Gmail looks back. Not a calendar day: Google applies the limit
    /// "over a rolling 24-hour period, not a set time of day".</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>The share of the limit at which it is worth saying something.</summary>
    public const double Loud = 0.8;

    /// <summary>The quota that applies to this account, or null when it is not Gmail
    /// and Yours Truly has nothing to say about the limits.
    ///
    /// Which of the two numbers applies is read off the address rather than asked,
    /// because it cannot be got wrong: a free account is always at gmail.com, and a
    /// Workspace account is always at a domain of its own. The failure that matters
    /// would be assuming 2,000 for an account that has 500, and that cannot happen.</summary>
    public static GmailQuota? For(string? smtpHost, string? address)
    {
        if (!IsGoogle(smtpHost)) return null;

        // No address yet is not a Workspace account — it is somebody who has not set
        // e-mail up. The host defaults to smtp.gmail.com whether or not anything has
        // been filled in, so without this the bar appears on a fresh install and
        // confidently offers a 2,000 limit for an account that does not exist.
        var at = (address ?? "").LastIndexOf('@');
        if (at <= 0 || at == address!.Length - 1) return null;
        var domain = address[(at + 1)..];

        return IsGoogle(domain)
            ? new GmailQuota(FreeAccount, "a free Gmail account")
            : new GmailQuota(Workspace, "a Google Workspace account");
    }

    private static bool IsGoogle(string? host)
    {
        var h = (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        return h is "gmail.com" or "googlemail.com"
            || h.EndsWith(".gmail.com", StringComparison.Ordinal)
            || h.EndsWith(".googlemail.com", StringComparison.Ordinal);
    }

    /// <summary>Where this account stands, having already sent <paramref name="sent"/>
    /// in the last 24 hours and being about to send <paramref name="adding"/> more.</summary>
    public QuotaUse After(int sent, int adding = 0) => new(Math.Max(0, sent), Math.Max(0, adding), this);
}

/// <summary>A reading of the quota at one moment, for showing on screen.</summary>
public sealed record QuotaUse(int Sent, int Adding, GmailQuota Quota)
{
    public int Limit => Quota.Limit;

    /// <summary>What the bar fills to, counting what is about to go out. Capped at 1 —
    /// a bar past its own end says nothing the wording does not say better.</summary>
    public double Fill => Limit <= 0 ? 0 : Math.Min(1, (double)(Sent + Adding) / Limit);

    public int Remaining => Math.Max(0, Limit - Sent);

    public QuotaPressure Pressure =>
        Sent + Adding > Limit ? QuotaPressure.Over
        : (double)(Sent + Adding) / Limit >= GmailQuota.Loud ? QuotaPressure.Close
        : QuotaPressure.Fine;

    /// <summary>Just the numbers, for the line above the bar. Short on purpose: it
    /// shares that line with a label, and the two used to overlap.</summary>
    public string Count() => $"{Sent} of {Limit:N0}";

    /// <summary>What the numbers mean, for the line below the bar.</summary>
    public string Describe() =>
        "sent in the last 24 hours"
        + (Adding > 0 ? $", and this send adds {Adding}" : "");

    /// <summary>What to do about it, or empty when there is nothing to say. Only the
    /// two loud cases get a sentence; at a tenth of the limit, silence is the right
    /// amount of comment.</summary>
    public string Advise() => Pressure switch
    {
        QuotaPressure.Over =>
            $"This is more than Gmail allows in a day on {Quota.Plan}. It stops accepting mail at "
            + $"{Limit:N0} and stays that way for up to 24 hours, so a send this big would break off "
            + $"part way through. There is room for {Remaining} more right now. Send to fewer people, "
            + "or wait — the limit is a rolling 24 hours, so it frees up gradually rather than at midnight.",
        QuotaPressure.Close =>
            $"That is most of what Gmail allows in a day on {Quota.Plan}. Going over stops sending "
            + "for up to 24 hours. The limit is a rolling 24 hours, so it frees up gradually.",
        _ => "",
    };
}
