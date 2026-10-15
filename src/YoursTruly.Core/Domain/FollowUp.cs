namespace YoursTruly.Core.Domain;

/// <summary>Sending a message again, to the people an earlier send did not reach or
/// did not include. The people who already have it are left unticked rather than
/// hidden, and the Send screen says so — otherwise a list with forty people mysteriously
/// unticked looks like a fault.</summary>
public static class FollowUp
{
    /// <summary>"Left out 42 people who already got this on 3 Oct." Earlier today is
    /// said as such; a different year carries the year, so last October is not read as
    /// this one.</summary>
    public static string Describe(int leftOut, DateTime sentOn, DateTime today)
    {
        var when = sentOn.Date == today.Date ? "earlier today"
            : sentOn.Year == today.Year ? $"on {sentOn:d MMM}"
            : $"on {sentOn:d MMM yyyy}";

        return leftOut switch
        {
            0 => $"Nobody who is still on your list got this {when}, so nobody has been left out.",
            1 => $"Left out 1 person who already got this {when}.",
            _ => $"Left out {leftOut} people who already got this {when}.",
        };
    }

    /// <summary>What the sender typed, from the message as it went out: the signature
    /// <see cref="Signature.Compose"/> added is taken off again, so it is not written
    /// into the box and then signed a second time. A message signed with a signature
    /// that has since changed is left exactly as it was; guessing which lines were the
    /// old signature risks cutting off part of the message.</summary>
    public static string Unsign(string sent, string? signature)
    {
        var text = sent.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (Signature.Clean(signature) is not { } sign) return text;

        var signed = $"\n— {sign}";
        if (text == $"— {sign}") return "";
        return text.EndsWith(signed, StringComparison.Ordinal) ? text[..^signed.Length] : text;
    }
}
