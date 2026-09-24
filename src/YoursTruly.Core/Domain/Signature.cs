namespace YoursTruly.Core.Domain;

public static class Signature
{
    /// <summary>The longest a signature may be. Long enough for a name, a role and a
    /// phone number on three lines; short enough that it cannot quietly double the
    /// length of every text.</summary>
    public const int MaxLength = 200;

    /// <summary>The signature as it will be stored, or null when there is none.
    /// Trailing blank lines go; the lines themselves are left exactly as typed, because
    /// a signature is the one piece of the message the sender writes to be read
    /// verbatim.</summary>
    public static string? Clean(string? signature)
    {
        var text = (signature ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (text.Length == 0) return null;
        return text.Length <= MaxLength ? text : text[..MaxLength].TrimEnd();
    }

    /// <summary>The body as the recipient will read it, signed.
    ///
    /// Composed once, in one place, so the character count on screen, the preview and
    /// what actually leaves are the same string. A signature already present — because
    /// the sender typed it themselves — is left alone rather than doubled.</summary>
    public static string Compose(string? body, string? signature)
    {
        var text = (body ?? "").TrimEnd();
        var sign = Clean(signature);
        if (sign is null) return text;
        if (text.Length == 0) return $"— {sign}";

        return AlreadySigned(text, sign) ? text : $"{text}\n— {sign}";
    }

    /// <summary>Who the messages are from, in a few words — the first line of the
    /// signature, up to any comma. Used where there is only room for a name: the rail,
    /// and the name an e-mail arrives under.</summary>
    public static string SenderName(string? signature)
    {
        var sign = Clean(signature);
        if (sign is null) return "";
        var firstLine = sign.Split('\n')[0];
        return firstLine.Split(',')[0].Trim();
    }

    /// <summary>True when the sender has already written themselves in at the end.
    ///
    /// Checks the last couple of lines rather than the whole message, so mentioning
    /// yourself in passing does not suppress the signature — and checks for the name
    /// rather than for the whole signature, because somebody who signs off by hand
    /// writes "Thanks, Jonathan", not their name and their club and their number.</summary>
    private static bool AlreadySigned(string text, string signature)
    {
        var opening = SenderName(signature);
        if (opening.Length == 0) return false;

        var lines = text.Split('\n');
        var tail = string.Join(' ', lines.Skip(Math.Max(0, lines.Length - 2)));
        return tail.Contains(opening, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>How many 160-character segments a text will be billed as. Segments drop
    /// to 153 characters once a message needs more than one, because each carries a
    /// header saying how they fit together.</summary>
    public static int TextSegments(string text) =>
        text.Length == 0 ? 0 : text.Length <= 160 ? 1 : (int)Math.Ceiling(text.Length / 153.0);
}
