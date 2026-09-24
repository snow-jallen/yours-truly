# How Yours Truly is put together

    src/YoursTruly.Core       pure logic: reading the PDF, normalising it, diffing an
                           import, and choosing who a message goes to
    src/YoursTruly.Messaging  sending over each channel, and the settings file
    src/YoursTruly.Data       EF Core entities, migrations, applying an import, sending
                           a batch and writing down what happened
    src/YoursTruly.App        Avalonia user interface
    tests/YoursTruly.Tests    all of the tests

`Core` depends on nothing but PdfPig. `Messaging` depends on `Core`. `Data` depends on
both. Nothing depends on the UI, so every rule below is testable without opening a
window — and the UI itself is tested headless, which is the only way a mistyped binding
gets caught before a user finds it.

## Reading a file of people

This is the hard part, and it is worth knowing why before changing any of it.

A report exported as a PDF is a rendered HTML table. The PDF contains no rules, no cell
boundaries, and no reading order that matches the table — pulling the text out linearly
interleaves the columns into nonsense. The only structure available is where each glyph
sits on the page.

Two measured facts drive `PdfTableReader`, and both hold for every file seen so far:

* **Cells are centred vertically within their row, not aligned to its top.** A cell
  wrapping to three lines sits at the row's centre plus and minus one line height; a
  two-line cell sits at plus and minus half of one. So a row's baselines land on a grid
  of *half* the line height, and no cell's first line lines up with any other cell's.
  This is why a row cannot be found by looking for where its name begins.
* **Rows are separated by more vertical space than the lines inside them.** How much
  more differs from file to file.

### Working out where one row ends

How much more used to be a number written down per report, and it was the single thing
stopping Yours Truly reading anything else: a file nobody had measured could not be read at
all. It is now read off the page.

`PdfTableReader.RowBreak` collects the gaps between consecutive lines and looks for two
heaps in them — the small gaps inside a wrapped row, the big gaps between rows. Otsu's
method finds the split, which is the same one-dimensional two-means question as choosing
black from white in a scan.

The trap a relative rule normally falls into is a page where every row is a single line.
There is only one heap, and any split of it cuts rows in half. So the split is
disbelieved unless both heaps hold at least three gaps and at least a fifth of them, and
their averages are at least 1.35x apart. When it is disbelieved the threshold falls back
to **1.55 times the text size** — measured, not guessed: across every file to hand the
lines inside one row sit 0.98 to 1.44 times the text size apart and consecutive rows sit
1.68 to 3.5 times it apart, and nothing observed falls between. On a page with nothing
wrapped on it, that gives one line per row, which is right.

Gaps larger than four times the text size are left out of the reckoning entirely: a
heading, a footer or the end of the table, never a wrapped cell.

### Finding the heading row

`Headings.Detect` is offered each group of lines. A group is the heading row when at
least **two** of its phrases name one of the things a list of people is built around — a
name, an e-mail address, a phone number. Two is the bar, and it is what keeps a smaller
table printed above the real one from claiming the page: a callings table carries a Name
of its own and nothing else recognisable.

Every other phrase on that same row becomes a column too, whatever it says. That is the
whole trick: Yours Truly does not need to know what a Team column or a Unit column is to
give the user the option of importing it as groups.

Details that each cost a debugging session:

* Glyph positions come from the **advance box** (`StartBaseLine`/`EndBaseLine`), not the
  ink bounding box. Ink bounds leave gaps inside a word wide enough to look like spaces,
  which turns `17 Jan` into `1 7 Jan` and breaks every birthday.
* Space glyphs are **kept**, because the files write real spaces and honouring them
  beats inferring every space from a gap.
* Words on the heading row are joined into one heading while they are less than 0.6 of
  the text size apart, so "Phone Number" is one column and not two.
* Phrases on different lines of the heading row are **stacked by x position**, so
  "Preferred" over "Name" is one column called "Preferred Name".
* Headings are detected **one row group at a time**, never merged across a page. One
  export carries a `Name` heading in two different tables on page 1, at x=225.6 and
  x=54.1; merged, the columns run right to left and nothing is readable.
* Body lines are taken **only from below the heading**. That same page prints a smaller
  table above the people, and those rows have names in them.
* A phrase carrying an `@`, or made mostly of digits, is **not** a heading candidate.
  Without that, somebody called `name@example.com` reads as a line naming a person and
  the first row of data is mistaken for the heading row.
* The detected positions must increase left to right or the layout is rejected.
* A column may be found and its contents **discarded**. One report prints Gender between
  the name and the age. Yours Truly has no use for it, but without an edge there the `F`
  joins the name and everyone reads "Ashdown, Marigold F".
* The lines a cell wrapped over are rejoined **with a space, unless joining them with
  nothing makes an e-mail address or a phone number**. "812 North 700" and "East" are
  two words of one value; a long e-mail wrapped mid-address is not, and a space put back
  between the halves makes something that looks fine on screen right up until the send
  fails. This is decided from the value rather than from which column it came out of,
  which is what lets it work on a file nobody has described.
* Page furniture is recognised by **position**, not by wording: every cell of the first
  column is left-aligned on that column, so a line starting to the left of it is not in
  the table. That catches a footer's date, its copyright line and its page number
  without anybody having to list the words, which change with the report and the year.
  Three patterns are checked as well, for a footer that starts inside the table: a page
  number, a count, and a bare URL.

### When there is no table at all

A file with no heading row Yours Truly can find is searched line by line for an e-mail
address and a phone number, and whatever is left on the line is the name. Only lines
carrying a way of reaching somebody are kept — without a heading there is nothing to say
a line of prose is not a person, and a page of prose would otherwise import as a hundred
people with no contact details. The sheet says `HeadingsFound: false`, and the import
screen says so out loud, because those columns are Yours Truly's invention and not the
file's.

A PDF with nobody findable in it fails loudly with `ImportException`, naming what was
looked for — and naming a scanned picture of a list, which is the failure most worth
calling out: it looks like exactly the right file and has no text in it whatsoever.

## What the columns mean

Nothing above decides that. `ContactSheetReader` hands over what the file said;
`FieldGuess` proposes a reading of each heading; the **import screen shows the proposal
and the user has the last word** before anything is written.

That split is what replaced three hand-measured report descriptors, and it also settled
an argument the descriptors could not. One export prints an Age column and fills it on 2
rows of 160: importing it would blank 158 recorded ages to honour 2. That used to be a
judgment written into the code, per report, by whoever had the export in front of them.
It is a drop-down now, which is the right place for a judgment about one file.

A heading nothing recognises defaults to **Groups**: every distinct value in the column
becomes a group and everyone with that value joins it. A Team column makes a group per
team; a Unit column makes one per unit. Where nothing at all reads as a name, the first
unrecognised column is taken for one — a roster headed "Player" or "Scout" names its
people in a word nobody can list in advance, and it is nearly always the first column.

Addresses are read like every other column and then thrown away. Yours Truly posts nothing,
so carrying where people live earns it nothing and costs a directory's worth of home
addresses sitting in a file on a desk.

## What an import may and may not touch

`ImportPlanner` produces an `ImportPlan` — added, updated, deactivated, reactivated,
unchanged — and changes nothing. `ImportService.ApplyAsync` writes it in one
transaction, or not at all.

A file of people issues no stable identifier, so people are matched on **name plus
birthday**, then on name alone for anyone left over. Where a birthday is printed it is
the strongest signal available: it does not change, and it is what tells two people of
the same name apart.

The split that matters:

* **An import owns the fields it is being imported for.** The mapping says which, and
  those are overwritten every time. A field the file carried and left blank is still
  cleared — the file said something about it. A field the file has no column for is left
  alone: silence is not an instruction to blank anything.
* **Yours Truly owns** the chosen channels, notes, and any contact details typed in by hand.
  No import may touch them. There are tests for this.
* **Groups are added to and never emptied.** An import puts people into the groups its
  mapping asks for. It never takes anybody out, because the groups are full of people
  put there by hand and a file that does not mention them is not an instruction to
  remove them.

Nobody is ever deleted. Falling out of a file sets `IsActive = false` and
`DeactivatedOn`; reappearing clears both and keeps the original `FirstSeenOn`, the chosen
channels, and every past delivery. The import screen can turn that off for a file that
is a second list rather than the whole of one — adding a committee on top of a roster
must not mark the roster as having left.

## Changes since import

A person has many `ContactPoint`s, each marked `Imported` or `Local`.

The **Changes since import** screen is every contact point that is `Local`, has never
been seen in an imported file, and has not been ticked off by hand. It answers "what
does Yours Truly know that my list does not?", and it exists because of the rule above: a
correction is recorded alongside what the file said rather than over it, so the two stay
visibly out of step until something reconciles them.

Two things reconcile them. `CopiedBackOn` is the user saying they have put the detail
back into wherever the list comes from — which for a spreadsheet nobody will re-export
is the only way it will ever clear. An import carrying the same value clears it on its
own: the existing row is marked as seen rather than duplicated, so the list empties
itself the moment the entry has actually been made.

## Several lists, and no default

A soccer team, a class and a street are three different sets of people with nothing to do
with each other, so they are three database files. The settings remember the name and the
path of each; the rail switches between them; Setup names them, starts them and forgets
them. Forgetting a list never deletes its file.

There is no default list and no default place to keep one. `AppServices.DatabasePath` is
nullable, and null is the ordinary state on a fresh install: a list is the user's
document, and an app that quietly makes one somewhere of its own choosing has decided
something it was not asked to decide. Every screen that reads a list is routed through
`MainWindowViewModel.ShowNoList` when there is none, so no screen needs an empty state of
its own and none can reach a database that is not there.

A path remembered in the settings is opened only if the file is still there. A list on a
drive that is not plugged in, or one somebody moved, leaves the app open and asking —
recreating an empty file over the place where a directory used to be is the worst thing
it could do.

Which list an import writes into is chosen on the import screen, per import, because it
genuinely is a decision per import: an updated roster goes back into the file it came
from, and a different set of people belongs in a file of its own where importing one can
never mark the other as gone. The plan is computed against the chosen list through
`AppServices.DbAt`, and applying it makes that list the open one.

The settings cannot live in any of them, for the obvious reason: a list cannot be the
thing that remembers where the other lists are. They live in `AppPaths.Settings` — the
per-user application-data folder, which is `~/Library/Application Support` on a Mac
rather than the `~/.config` that .NET's `SpecialFolder.ApplicationData` reports there.

## Sending

`Audience` turns the directory into a chosen set: filter, sort, and — the part that
matters — **reachability**, which carries a reason rather than a bool. Someone who chose
e-mail and has no e-mail address is named on screen before the send, not dropped
silently during it.

A person's choice is a `ChannelSet`, not a channel, and every channel in it happens.
Somebody who ticks Text and Email gets the message twice, once each way. Two numbers are
therefore worth saying and the summary carries both: how many people hear from you, and
how many messages that is.

`BroadcastService` writes each delivery before attempting the next. A send that is
interrupted half way therefore leaves an honest record: the messages already sent cannot
be unsent, and the user has to be able to see which those were.

Every sender turns its provider's failures into a sentence the user can act on. "535
5.7.8" and "21610" tell the people this app is for exactly nothing; "Gmail needs an app
password, here is where to make one" and "they replied STOP and must text START first"
do. The tests assert the codes themselves never reach the screen.

The voice channel plays a recording of the user, never text-to-speech. Yours Truly rings
them with inline TwiML that records them, Twilio keeps the recording, and the broadcast
plays it back by its Twilio address. Passing TwiML inline when a call is created is
what lets a desktop app place calls at all — the usual arrangement needs a public web
server for Twilio to fetch instructions from, which this app has no business running.

Credentials live in a JSON file beside the settings, never inside a list: they are not
directory data and should not ride along in its backups. The file is owner-readable
only, because unlike the directory a Twilio token can be used to spend money.

## Conventions

* Enums are stored **by name**, never by number, so adding a channel cannot renumber
  existing rows. A set of them is stored as its names joined by commas — "email,text" —
  for the same reason.
* Every key is a `Guid` set in the entity's initializer, and every key is configured
  `ValueGeneratedNever()`. Without that, EF reads an already-set key as proof the row
  exists and issues an UPDATE where an INSERT was meant — anything attached through a
  navigation property fails to save, silently at the model level and loudly at runtime.
* `Yours TrulySettings` writes out its own `Equals`, because a record compares a list by
  reference and one read back off disk is never the same object as the one written.
  Without it Setup reports unsaved changes the moment it opens, every time.
* Instants are `DateTimeOffset` in the entities, converted to UTC text in SQLite so
  `ORDER BY` and `WHERE` work in SQL instead of in memory.
* A day that a person or a record belongs to is a `DateOnly`, in the user's local
  calendar. Never derive one from an instant.
* After changing an entity: `dotnet dotnet-ef migrations add <Name> -p src/YoursTruly.Data
  -s src/YoursTruly.Data`. The suite fails if the migrations and the model disagree — and
  read what it scaffolds before trusting it, because it matches columns by position.
* Warnings are errors. Run `dotnet test` before committing.
