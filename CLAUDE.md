# Yours Truly — working rules

A cross-platform desktop app that imports a file of people into SQLite and sends them
messages by whichever channels each of them chose. Read `docs/architecture.md` before
touching the PDF reader; it encodes measurements that are not obvious from the code.

Yours Truly used to read three reports exported from one church system, and a lot of it was
named after that. It is not church-specific any more: no wards, no callings, no LCR. A
Ward column in a file is now just a column whose values become groups, like a Team
column or a Class column. If you are adding something that only makes sense for one
organisation, it belongs in a group, a note or the signature, not in a new field.

## Never in this repository

- **No real export of anybody's directory, and nothing derived from one.** Those files
  hold phone numbers, e-mail addresses and birthdays for hundreds of people. Real ones
  go in `tests/YoursTruly.Tests/Fixtures/private/`, which is gitignored, and the tests that
  use them skip when they are absent. Fixtures that get committed are synthetic, built
  in code by `SyntheticReport`, `SyntheticCallingsReport`, `SyntheticMemberList` and
  `SyntheticRoster`.
- No `.db` files, no `settings.json`.

## Where things live

A list of people is the **user's** document and the app has no opinion about where it
goes: no default path, no folder of its own, and on a fresh install no list at all until
somebody imports a file or starts one. `AppServices.DatabasePath` is nullable and every
screen that reads a list goes through `MainWindowViewModel.ShowNoList` first. Do not
reintroduce a default — quietly making a file somewhere of the app's choosing is the
thing this deliberately does not do.

Settings and the log are the **app's** and live in `AppPaths.Settings`: `%APPDATA%` on
Windows, `~/Library/Application Support` on a Mac, `~/.config` on Linux. Not
`SpecialFolder.ApplicationData` on a Mac — .NET maps that to `~/.config`, which is a Unix
convention rather than a Mac one.

Which list an import writes into is chosen on the import screen, per import: the plan is
computed against that list, not the open one, and applying it opens it.

## The brand

Two speech bubbles whose overlap is a third colour — purple `#694293`, salmon `#E9A186`,
plum `#381F51`, cream `#FCF3EE`. The palette in `Theme/Palette.axaml` is built from those
and nothing else, except a dark amber for warnings (salmon is a channel colour, and a
caution the same hue as "text message" is worse than one slightly off-brand) and the
usual green and red.

`assets/build-icons.sh` draws the icon at every size and packs the three platform
formats. Run it after touching `assets/draw-icon.swift`; the outputs are committed so
nobody needs a Mac to build.

## The database is not encrypted, on purpose

Do not propose encrypting it. The files it is imported from are unencrypted, so
encrypting the copy moves the weak point instead of removing it. The app's answer is to
tell the user plainly what the file holds — the first card in Setup. Keep that copy
blunt and specific; it is a feature, not boilerplate.

## Reading a file

- **Yours Truly is told nothing about the file.** Any line naming two of the things a list
  of people is built around — a name, an e-mail address, a phone number — is taken as
  the heading row, and every other phrase on that line becomes a column too. A heading
  nothing recognises is offered to the user as a group rather than refused. Failing
  that, the page is searched for e-mail addresses and phone numbers directly.
- `FieldGuess` guesses; the **user decides**, on the import screen, before anything is
  written. Never make the guess authoritative, and never refuse a file because the
  guess was poor — show it and let it be corrected.
- Addresses are read like any other column and thrown away. Yours Truly posts nothing.

## Changes since import

Yours Truly never writes a correction over what a file said; it records the correction
alongside. That is what makes an edit survive the next import, and it is what the
**Changes since import** screen lists: every contact detail typed in by hand that no
imported file has ever carried and nobody has ticked off.

Two things clear a row from it, and both matter. Ticking it off (`CopiedBackOn`) is for
somebody who has put the detail back into wherever their list comes from. An import
turning up carrying the same value clears it **by itself** — the value is marked as seen
rather than duplicated, which is the whole reason that bookkeeping exists. Keep both.

Notes never appear there. A note is nobody else's business.

## Import rules

- An import owns **the fields it is being imported for** — the mapping says which. A
  file with no e-mail column has said nothing about anyone's e-mail address and must not
  blank it; a file that has the column and leaves it blank does clear it.
- Yours Truly owns chosen channels, notes, and contact details typed in by hand. **An import
  must never write to these.** Tests enforce it; keep them.
- An import may **add** somebody to a group and must never take anybody out. The groups
  are full of people put there by hand, and a file that does not mention them is not an
  instruction to remove them.
- Nobody is deleted. Falling out of a file is a soft delete (`IsActive`,
  `DeactivatedOn`); reappearing restores the same row with its history intact. The
  import screen can turn this off, for a file that is a second list rather than the
  whole of one.
- Plan first, apply second. `ImportPlanner` computes; `ImportService` writes, in one
  transaction. The user sees the plan before anything lands.
- **A reader must be able to fail.** Three readers try in order — `Table`, then
  `RecordBlockReader`, then `RecordReader` — and each returns null rather than guessing.
  The line-by-line hunt that used to sit at the bottom never failed, so it hid every
  failure above it and imported fifteen people called `COMLINK MESSAGE ADDRESS` behind a
  green button.
- Rows carrying an address or a number but no name are **counted and shown**
  (`ImportPlan.Unnamed`), not silently dropped. Names are what a misread loses first.
- **Do not add a reader for a layout.** The five files in
  `tests/YoursTruly.Tests/Fixtures/layouts/` are fifteen people printed five ways and
  none of them is taught to the reader; a sixth that fails means the general rule is
  wrong. See docs/architecture.md.

## Sending rules

- **Pacing belongs to the route, not the channel.** The 2-to-11-second gaps exist to
  stop a carrier flagging the user's *own* phone number, so they apply to Mac Messages
  and the Android gateway and not to Twilio, which paces itself. `TextTransports
  .NeedsPacing` is the one place that says so; `BroadcastService` takes it as
  `paceTexts` and defaults to true, so a route added later is protected until somebody
  decides otherwise.
- **Gmail's quota numbers come from Google and nowhere else.** 500 a day free, 2,000 on
  Workspace, over a rolling 24 hours. `GmailQuotaTests` pins them and `GmailQuota` holds
  the links. If a number looks stale, check Google — do not adjust it to match
  observation.
- The quota bar counts only this list's sends and **says so on screen**. It is a floor,
  not a measurement, and must never be presented as the latter.
- One recipient per message, always. It is what stops 427 people seeing each other's
  addresses, and it is why the message count rather than the recipient count is the
  limit that runs out. Do not batch recipients to save quota.

## Schema

- Enums stored by name, never by number. A set of them is stored as its names joined by
  commas, for the same reason.
- Every Guid key is set in the initializer and configured `ValueGeneratedNever()`.
- `dotnet dotnet-ef migrations add <Name> -p src/YoursTruly.Data -s src/YoursTruly.Data`
  after any entity change; the suite fails on drift. **Read what it scaffolds.** The
  last one came out as a rename of `Ward` to `ImportedPhone` — the columns line up that
  way by position — and would have turned every phone number into an e-mail address.

## User interface

This is for people without a technical background. Name things the way they would:
"Send a message", not "dispatch batch". Errors say what went wrong and what to do about
it. Every credential has a Test button that sends a real message to the user's own
address, so "Working" means it actually worked.

Pure logic lives in `Core` with tests. Views do not compute.

## Sending

- **Yours Truly sends as one person. It is not an organisation's system and must not look
  like one.** The rail names the person. Every message goes to one recipient on its own
  and is signed — a text from an unrecognised number gets ignored or reported; a signed
  one gets answered. The signature is one free-text box the sender writes themselves;
  composition lives in `Core/Domain/Signature.cs` so the preview, the length shown and
  what actually leaves are the same string.
- **Several channels per person, and they all happen.** Somebody who ticks Text and
  Email gets the message twice, once each way, as two deliveries with two records.
- A provider's error code must never reach the screen. Translate it into what happened
  and what to do. Tests assert the codes are absent; keep them.
- Reachability carries a reason. Nobody is dropped from a send silently.
- Each delivery is written down before the next is attempted, so an interrupted send
  leaves an honest record.

## Logging

- `Log.Record` / `Log.Failure` from anywhere; the sink is set once at start-up.
- **Nothing identifying goes in the log.** It exists to be sent to whoever is helping,
  which makes it a copy of the directory unless it is deliberately not one. Names never;
  addresses only through `Redact.Address`; provider messages only through
  `Redact.Failure`, since they quote the address back; message bodies only as a length.
  There is a test that a failed send leaks none of it — keep it.
- Log what was done and what failed, not what was typed.

## Seeing the app

`dotnet test --filter ScreenshotHarness` writes a picture of every screen into
`.screenshots/`. Look at them after changing any layout: a window that lays out wrongly
still passes every assertion anybody thinks to write about it, and this has already
caught clipped columns, a control overflowing off the screen and a header whose columns
did not line up with its rows.

The harness dispatches a `Func<Task<int>>`, not a `Func<Task>`, and so does
`UserInterfaceTests.InWindow`. Both have a comment saying why. An async lambda with no
return value binds to an overload that never awaits it, and everything after the first
await that genuinely yields is silently abandoned — which is exactly how one screenshot
stopped being taken while the test went on passing.

## Before committing

`dotnet test` — about 420 tests, a few seconds. Warnings are errors.
Small commits, one concern each, imperative subject with a `feat:`/`fix:`/`docs:`/
`refactor:` prefix; the body explains why.
