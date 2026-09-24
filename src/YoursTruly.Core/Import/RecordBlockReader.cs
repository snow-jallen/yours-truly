using System.Text.RegularExpressions;

namespace YoursTruly.Core.Import;

/// <summary>Reads a printed directory that is not a table: blocks of people, several
/// to a line, laid out down the page like a phone book.
///
/// The shape it is looking for is a household — a heading carrying the surname, then
/// the people under it in side-by-side bands, each with their own details beneath them:
///
///     Albee, Tyler &amp; Whitney Leigh                      677 E Union St
///       Tyler                              Whitney Leigh
///       Sunday School President            Young Women Specialist
///       (435) 851-3732                     Primary Teacher
///       tyler.jayson.albee@gmail.com       (801) 995-5902
///       Laynie Leigh      Carson Tyler
///
/// Read as lines that is nonsense: the two adults share every line, so their names,
/// their callings and their numbers all run together and one of them disappears. Read
/// as bands it comes apart cleanly, because a person's name and their details share an
/// x position and nothing else on the page does.
///
/// Three text sizes carry the whole structure, and they are read off the document
/// rather than written down: the heading, the names, and the details under them.</summary>
public static partial class RecordBlockReader
{
    /// <summary>Two runs within this many points of each other are in the same band.
    /// Bands on a real printout are a hundred points apart; the slack is for a name
    /// that starts a shade left of the detail beneath it.</summary>
    private const double BandWidth = 12;

    /// <summary>A gap this many times the text size splits one person's run from the
    /// next person's, across the page. Generous, because the gap between bands is
    /// enormous and the gap inside a name is one space.</summary>
    private const double BetweenBands = 3.0;

    /// <summary>Below this many households it is not this kind of document, and the
    /// caller should try something else.</summary>
    private const int FewestBlocks = 3;

    private sealed record Run(double X, double Y, double Size, string Text, int Page);

    /// <summary>The people this document holds, or null when it is not laid out this
    /// way at all.</summary>
    public static IReadOnlyList<SheetRow>? Read(IReadOnlyList<IReadOnlyList<TextLine>> pages)
    {
        var runs = Split(pages);
        if (runs.Count == 0) return null;

        if (HeadingSize(runs) is not double heading) return null;
        if (NameAndDetail(runs, heading) is not var (nameSize, detailSize)) return null;

        var people = new List<SheetRow>();
        var blocks = 0;
        string? surname = null;
        var block = new List<Run>();

        foreach (var run in runs)
        {
            if (Same(run.Size, heading) && run.Text.Contains(',', StringComparison.Ordinal))
            {
                people.AddRange(PeopleIn(block, surname, nameSize, detailSize));
                block.Clear();
                surname = run.Text.Split(',')[0].Trim();
                blocks++;
                continue;
            }
            block.Add(run);
        }
        people.AddRange(PeopleIn(block, surname, nameSize, detailSize));

        return blocks >= FewestBlocks && people.Count >= FewestBlocks ? people : null;
    }

    /// <summary>Every line broken into the runs standing side by side on it, in reading
    /// order down the document.</summary>
    private static List<Run> Split(IReadOnlyList<IReadOnlyList<TextLine>> pages)
    {
        var runs = new List<Run>();
        for (var page = 0; page < pages.Count; page++)
            foreach (var line in pages[page])
            {
                var size = line.TextSize;
                if (size <= 0) continue;
                foreach (var run in line.Runs(size * BetweenBands))
                    runs.Add(new Run(run.X, line.Baseline, size, run.Text, page + 1));
            }
        return runs;
    }

    /// <summary>The size a household heading is set in.
    ///
    /// Found by what it says rather than by how big it is: it carries a surname and a
    /// comma, over and over. The biggest text on these pages is the page header, which
    /// is on every page and is not a household.</summary>
    private static double? HeadingSize(List<Run> runs)
    {
        var candidates = runs
            .GroupBy(r => Math.Round(r.Size))
            .Where(g => g.Count() >= FewestBlocks
                     && g.Count(r => r.Text.Contains(',', StringComparison.Ordinal)) * 2 > g.Count())
            .OrderByDescending(g => g.Key)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0].Key;
    }

    /// <summary>The two sizes under the heading that carry the people: the commonest
    /// two, larger first. On the document this was written for they are the names and
    /// the callings, and the addresses — set between the two — fall out of the
    /// reckoning, which is exactly where Yours Truly wants them.</summary>
    private static (double Name, double Detail)? NameAndDetail(List<Run> runs, double heading)
    {
        var sizes = runs
            .Where(r => Math.Round(r.Size) < heading)
            .GroupBy(r => Math.Round(r.Size))
            .Where(g => g.Count() >= FewestBlocks)
            .OrderByDescending(g => g.Count())
            .Take(2)
            .Select(g => g.Key)
            .OrderByDescending(s => s)
            .ToList();

        return sizes.Count < 2 ? null : (sizes[0], sizes[1]);
    }

    /// <summary>The people in one household. A name starts a person and the details
    /// beneath it in the same band belong to them, until the next name in that band.</summary>
    private static IEnumerable<SheetRow> PeopleIn(
        List<Run> block, string? surname, double nameSize, double detailSize)
    {
        if (surname is null) yield break;

        var bands = block
            .Where(r => Same(r.Size, nameSize) || Same(r.Size, detailSize))
            .GroupBy(r => (int)Math.Round(r.X / BandWidth));

        foreach (var band in bands)
        {
            string? given = null;
            string mail = "", tel = "";

            foreach (var run in band.OrderBy(r => r.Page).ThenByDescending(r => r.Y))
            {
                if (Same(run.Size, nameSize))
                {
                    if (given is not null) yield return Person(surname, given, mail, tel);
                    given = run.Text;
                    mail = tel = "";
                    continue;
                }

                if (given is null) continue;
                if (mail.Length == 0 && EmailAnywhere().Match(run.Text) is { Success: true } m)
                    mail = m.Value;
                if (tel.Length == 0 && PhoneAnywhere().Match(run.Text) is { Success: true } t)
                    tel = t.Value.Trim();
            }

            if (given is not null) yield return Person(surname, given, mail, tel);
        }
    }

    private static SheetRow Person(string surname, string given, string mail, string tel) =>
        new([$"{surname}, {given}", mail, tel], 1);

    private static bool Same(double a, double b) => Math.Abs(a - b) < 0.6;

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailAnywhere();

    [GeneratedRegex(@"(?<![\d-])(\+?1[ .\-]?)?\(?\d{3}\)?[ .\-]\d{3}[ .\-]\d{4}(?![\d-])")]
    private static partial Regex PhoneAnywhere();
}
