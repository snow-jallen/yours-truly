using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace YoursTruly.Tests;

/// <summary>The files the app was not built for and now has to read: a soccer roster
/// with headings nobody could have listed in advance, and a printed phone tree with no
/// table in it at all.
///
/// Both write names the way most of the world does — "Adelaide Ashgrove", not
/// "Ashgrove, Adelaide" — which is the other half of not being built for one report.</summary>
internal static class SyntheticRoster
{
    /// <summary>A table, but not one anybody taught the app: "Player", "Parent Email",
    /// "Cell", "Team". One line per row, so there is nothing wrapped on the page to
    /// tell a wrapped line apart from a new row.</summary>
    public static byte[] Table()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(612, 792);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        void Put(string text, double x, double y)
        {
            if (text.Length > 0) page.AddText(text, 10, new PdfPoint(x, y), font);
        }

        Put("Thunder FC — Spring Roster", 50, 740);

        Put("Player", 50, 700); Put("Parent Email", 180, 700);
        Put("Cell", 360, 700); Put("Team", 470, 700);

        Put("Adelaide Ashgrove", 50, 680); Put("a.ashgrove@example.com", 180, 680);
        Put("(435) 555-0101", 360, 680); Put("U12 Blue", 470, 680);

        Put("Barnaby Quilley", 50, 660); Put("bq@example.com", 180, 660);
        Put("435-555-0102", 360, 660); Put("U12 Blue", 470, 660);

        Put("Verity Winslade", 50, 640); Put("v.winslade@example.com", 180, 640);
        Put("", 360, 640); Put("U14 Red", 470, 640);

        Put("Page 1 of 1", 500, 40);
        return builder.Build();
    }

    /// <summary>No table and no headings — a list somebody typed and printed, with a
    /// name, an address and a number on each line, punctuated three different ways,
    /// and two lines of prose that are not people.</summary>
    public static byte[] Loose()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(612, 792);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        void Put(string text, double y) => page.AddText(text, 11, new PdfPoint(60, y), font);

        Put("Neighbourhood phone tree", 720);
        Put("Adelaide Ashgrove - a.ashgrove@example.com - (435) 555-0101", 690);
        Put("Barnaby Quilley  bq@example.com  435.555.0102", 670);
        Put("Verity Winslade, v.winslade@example.com", 650);
        Put("Please keep this list to yourself.", 600);
        return builder.Build();
    }

    /// <summary>A list the app can reach but cannot name: everybody is an id and a way
    /// of contacting them, and nothing on the page is name-shaped. What a badly-read
    /// file looks like, because the addresses survive any misreading and the names do
    /// not.</summary>
    public static byte[] Nameless()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(612, 792);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        void Put(string text, double y) => page.AddText(text, 11, new PdfPoint(60, y), font);

        for (var i = 0; i < 5; i++)
            Put($"REF-{i:000}   ref{i:000}@example.com   (435) 555-01{i:00}", 700 - i * 22);
        return builder.Build();
    }
}
