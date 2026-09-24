namespace YoursTruly.Core.Domain;

/// <summary>How far apart texts go out. A burst of identical texts from one number,
/// seconds apart to the millisecond, is what carriers' spam filters look for, and a
/// personal phone (iPhone Messages, the Android gateway) can get its number flagged
/// or blocked for it. A random gap between each looks like a person sending them.</summary>
public static class TextPacing
{
    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(11);

    /// <summary>A gap anywhere from <see cref="Shortest"/> to <see cref="Longest"/>,
    /// to the millisecond, so no two gaps are the same.</summary>
    public static TimeSpan NextGap(Random random) =>
        TimeSpan.FromMilliseconds(random.Next(
            (int)Shortest.TotalMilliseconds, (int)Longest.TotalMilliseconds + 1));

    /// <summary>Roughly how long the gaps add up to for this many texts: none before
    /// the first, an average one before each of the rest.</summary>
    public static TimeSpan Estimate(int texts) =>
        texts <= 1 ? TimeSpan.Zero : (Shortest + Longest) / 2 * (texts - 1);

    /// <summary>The estimate as people say it: "about 20 seconds", "about 4 minutes".</summary>
    public static string Describe(TimeSpan span) => span.TotalSeconds switch
    {
        < 60 => $"about {Math.Max(5, (int)Math.Round(span.TotalSeconds / 5) * 5)} seconds",
        < 90 => "about a minute",
        _ => $"about {(int)Math.Round(span.TotalMinutes)} minutes",
    };
}
