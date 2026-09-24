using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace YoursTruly.Tests;

/// <summary>Builds a printed household directory, laid out the way a real one is: not a
/// table anywhere on the page, but blocks of people in side-by-side bands.
///
/// Every measurement is taken from a real 32-page printout of 436 people. The sizes are
/// what carry the structure — 11pt for the household, 9pt for a person's name, 7pt for
/// what is written under them, and 8pt for the address, which sits between the two and
/// must fall out rather than be read as somebody's details. The bands are at x=50 and
/// x=409 for the adults and x=50, 289 and 529 for the children, and the page has its
/// own header and footer at sizes of their own.</summary>
internal static class SyntheticDirectory
{
    private const double Household = 11, Person = 9, Address = 8, Detail = 7;

    public static byte[] Build()
    {
        var b = new PdfDocumentBuilder();
        var font = b.AddStandard14Font(Standard14Font.Helvetica);
        var one = b.AddPage(612, 792);
        var two = b.AddPage(612, 792);

        void Put(PdfPageBuilder page, string text, double x, double y, double size)
        {
            if (text.Length > 0) page.AddText(text, size, new PdfPoint(x, y), font);
        }

        void Header(PdfPageBuilder page)
        {
            Put(page, "Bramblewick 3rd Ward - 12564", 31, 561, 13);
            Put(page, "© 2026 by Some Body, Inc. All rights reserved.", 276, 31.5, 5);
        }

        Header(one);
        Put(one, "A", 31, 534.8, 12);                       // a letter divider

        // Two adults with their own details, and two children, all sharing lines.
        Put(one, "677 E Union St", 400, 508.5, Address);
        Put(one, "Ashgrove, Tyler & Whitney Leigh", 32, 504.8, Household);
        Put(one, "Tyler", 50, 486.8, Person);
        Put(one, "Whitney Leigh", 409, 486.8, Person);
        Put(one, "Sunday School President", 50, 475.5, Detail);
        Put(one, "Young Women Specialist", 409, 475.5, Detail);
        Put(one, "(435) 851-3732", 50, 465.8, Detail);
        Put(one, "Primary Teacher", 409, 465.8, Detail);
        Put(one, "tyler.ashgrove@example.com", 50, 456.0, Detail);
        Put(one, "(801) 995-5902", 409, 456.0, Detail);
        Put(one, "Laynie Leigh", 50, 432.0, Person);
        Put(one, "Carson Tyler", 289, 432.0, Person);

        // One adult on their own.
        Put(one, "190 N 800 E", 400, 342.8, Address);
        Put(one, "Quilley, Emma", 32, 339.0, Household);
        Put(one, "Emma", 50, 320.3, Person);
        Put(one, "(435) 851-5954", 50, 309.0, Detail);
        Put(one, "emma.quilley@example.com", 50, 299.3, Detail);

        // A household whose children run to three bands and two lines.
        Put(two, "Winslade, Jonathan & Samantha", 32, 700.0, Household);
        Put(two, "Jonathan", 50, 681.3, Person);
        Put(two, "Samantha", 409, 681.3, Person);
        Put(two, "(435) 851-5343", 50, 670.0, Detail);
        Put(two, "samantha.winslade@example.com", 409, 670.0, Detail);
        Put(two, "jonathan.winslade@example.com", 50, 660.3, Detail);
        Put(two, "Melinda Jane", 50, 636.3, Person);
        Put(two, "James", 289, 636.3, Person);
        Put(two, "Cora Lucille", 529, 636.3, Person);
        Put(two, "Lydia Jean", 50, 612.3, Person);
        Header(two);

        return b.Build();
    }
}
