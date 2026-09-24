using System.Text.RegularExpressions;

namespace YoursTruly.Core.Import;

/// <summary>Reads a page of repeated records — a table row, a card, a form, a stanza of
/// a mainframe dump — without being told which of those it is looking at.
///
/// This replaced a reader per layout, which is a race nobody wins: every unusual file
/// was a new family and a new reader, and the fallback underneath them all never failed,
/// so it hid every failure above it and imported fifteen people called "COMLINK MESSAGE
/// ADDRESS" behind a green button.
///
/// What generalises is not the layout but the fact of a repeating unit, and four things
/// follow from it:
///
///  1. <b>Anchor on the contact details.</b> An address announces itself, so n addresses
///     means n records. That alone fixes the counting — hunting line by line found 5 and
///     11 people in two files that hold 15.
///  2. <b>Give every run to its nearest anchor, in units taken from the anchors
///     themselves.</b> A table's records are wide and short, a grid of cards is neither.
///     Measuring the spacing rather than assuming it is what lets one rule fit both.
///  3. <b>Take the name from the front of a run</b> — see <see cref="PersonName"/>.
///  4. <b>Look for it at the offset the records agree on.</b> In a repeating layout every
///     record puts the name in the same place relative to its address, and that beats any
///     per-record guess: it is what tells a name column from an affiliation column, and
///     what lets a one-word name win over the "Rebel Alliance" printed beside it.</summary>
public static partial class RecordReader
{
    /// <summary>Below this many anchors there is no repetition to find the pattern in.</summary>
    private const int FewestRecords = 2;

    /// <summary>How finely an offset is bucketed when records vote on where the name
    /// sits, as a fraction of the spacing between records.</summary>
    private const int Buckets = 4;

    private readonly record struct Run(double X, double Y, int Page, string Text);

    /// <summary>The people on these pages, or null when there is no repeated record to
    /// be found.</summary>
    public static IReadOnlyList<SheetRow>? Read(IReadOnlyList<IReadOnlyList<TextLine>> pages)
    {
        var runs = Split(pages);
        if (runs.Count == 0) return null;

        // An address is the better anchor — it is unique to a person and unmistakable.
        // A phone number will do where a file carries no addresses at all.
        var anchors = runs.Where(r => Email().IsMatch(r.Text)).ToList();
        var byEmail = anchors.Count >= FewestRecords;
        if (!byEmail) anchors = runs.Where(r => Phone().IsMatch(r.Text)).ToList();
        if (anchors.Count < FewestRecords) return null;

        var (sx, sy) = Spacing(anchors);
        var mine = Partition(runs, anchors, sx, sy);
        var agreed = WhereTheNameSits(anchors, mine, sx, sy);

        var people = new List<SheetRow>();
        foreach (var anchor in anchors)
        {
            var own = mine[anchor];
            var name = NameFor(anchor, runs, own, agreed, sx, sy);
            var mail = byEmail
                ? Email().Match(anchor.Text).Value
                : own.Select(r => Email().Match(r.Text)).FirstOrDefault(m => m.Success)?.Value ?? "";
            var tel = byEmail
                ? own.Select(r => Phone().Match(r.Text)).FirstOrDefault(m => m.Success)?.Value.Trim() ?? ""
                : Phone().Match(anchor.Text).Value.Trim();

            people.Add(new SheetRow([name ?? "", mail, tel], anchor.Page));
        }
        return people;
    }

    private static List<Run> Split(IReadOnlyList<IReadOnlyList<TextLine>> pages)
    {
        var runs = new List<Run>();
        for (var page = 0; page < pages.Count; page++)
            foreach (var line in pages[page])
            {
                var size = line.TextSize;
                if (size <= 0) continue;
                foreach (var run in line.Runs(size * 1.2))
                    runs.Add(new Run(run.X, line.Baseline, page + 1, run.Text));
            }
        return runs;
    }

    /// <summary>How far apart records sit, across and down, measured from the anchors.
    /// The median gap rather than the mean, so one page break does not set the scale.</summary>
    private static (double X, double Y) Spacing(List<Run> anchors)
    {
        static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var gaps = sorted.Zip(sorted.Skip(1), (a, b) => b - a).Where(g => g > 1).ToList();
            return gaps.Count == 0 ? 1 : gaps[gaps.Count / 2];
        }
        return (Median(anchors.Select(a => a.X)), Median(anchors.Select(a => a.Y)));
    }

    /// <summary>Every run to its nearest anchor, distances measured in units of the
    /// spacing between records so that neither direction dominates by accident.</summary>
    private static Dictionary<Run, List<Run>> Partition(
        List<Run> runs, List<Run> anchors, double sx, double sy)
    {
        var mine = anchors.ToDictionary(a => a, _ => new List<Run>());
        foreach (var run in runs)
        {
            Run? best = null;
            var nearest = double.MaxValue;
            foreach (var anchor in anchors)
            {
                if (anchor.Page != run.Page) continue;
                var d = Squared(anchor, run, sx, sy);
                if (d >= nearest) continue;
                nearest = d;
                best = anchor;
            }
            if (best is { } owner) mine[owner].Add(run);
        }
        return mine;
    }

    private static double Squared(Run a, Run b, double sx, double sy) =>
        Math.Pow((a.X - b.X) / sx, 2) + Math.Pow((a.Y - b.Y) / sy, 2);

    /// <summary>The offset from an anchor at which most records carry a name, or null
    /// when they do not agree on one.</summary>
    private static (int X, int Y)? WhereTheNameSits(
        List<Run> anchors, Dictionary<Run, List<Run>> mine, double sx, double sy)
    {
        var votes = new Dictionary<(int, int), int>();
        foreach (var anchor in anchors)
            foreach (var run in mine[anchor].Where(r => PersonName.LeadingName(r.Text) is not null))
            {
                var slot = Slot(anchor, run, sx, sy);
                votes[slot] = votes.GetValueOrDefault(slot) + 1;
            }

        return votes.Count == 0 ? null : votes.OrderByDescending(v => v.Value).First().Key;
    }

    private static (int X, int Y) Slot(Run anchor, Run run, double sx, double sy) =>
        ((int)Math.Round((run.X - anchor.X) / sx * Buckets),
         (int)Math.Round((run.Y - anchor.Y) / sy * Buckets));

    /// <summary>This record's name.
    ///
    /// Looked for at the agreed offset across every run on the page, not only the ones
    /// this record was awarded: in a two-up directory both names on a line sit nearer
    /// the left-hand record, and an exclusive carve-up leaves the right-hand person with
    /// no name at all. Failing that, the first name in the record in reading order —
    /// and failing that, nothing, which the import screen says out loud rather than
    /// inventing something.</summary>
    private static string? NameFor(
        Run anchor, List<Run> runs, List<Run> own, (int X, int Y)? agreed, double sx, double sy)
    {
        if (agreed is { } slot)
            foreach (var run in runs)
                if (run.Page == anchor.Page && Slot(anchor, run, sx, sy) == slot
                    && PersonName.LeadingName(run.Text) is { } found)
                    return found;

        return own
            .OrderByDescending(r => r.Y).ThenBy(r => r.X)
            .Select(r => PersonName.LeadingName(r.Text))
            .FirstOrDefault(n => n is not null);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<![\d-])(\+?1[ .\-]?)?\(?\d{3}\)?[ .\-]\d{3}[ .\-]\d{4}(?![\d-])")]
    private static partial Regex Phone();
}
