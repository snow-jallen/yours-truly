<img src="assets/mark.png" alt="" width="132">

# Yours Truly

A small desktop app for keeping a list of people current and getting a message to every
one of them — by e-mail, by text, by a recorded phone call, or by as many of those at
once as each person wants.

Built for one person on one computer, with no server and no account to sign into.

## What it does

1. **Import.** Drop in a PDF with people's names, e-mail addresses and phone numbers in
   it — a roster, a class list, a directory export, a printed phone tree. Yours Truly works
   the columns out, shows you what it made of each one so you can correct it, and shows
   exactly what would change before anything is written. A column it does not recognise
   — Team, Year, Unit — becomes groups you can send to. People who drop out of a later
   file are marked inactive, never deleted.
   There are six files in [`samples/`](samples/) to try it on, holding the same fifteen
   invented people printed six completely different ways.
2. **Keep several lists.** A soccer team, a primary class and your neighbours are three
   different sets of people, so they are three files. Name them what you call them and
   switch between them from the bottom of the rail.
3. **Correct.** Add people no file carries, and fix anyone's e-mail or phone on the
   People screen. Somebody added by hand is never removed by an import, because their
   absence from a file says nothing about them. Yours Truly never writes over what the file
   said — it records the correction, which is what makes it survive every future import
   and what puts it on the **Changes since import** list, so you can put it back into
   wherever your list comes from. That list clears itself once an import comes back
   carrying the detail.
4. **Send.** Choose who by group, channel, age, birthday month, or a search that reads
   notes as well as names — so writing "choir" or "#ride-needed" in somebody's note is
   all the tagging system there is, and all there needs to be. Write a message once. It
   goes out by e-mail, text and phone call, split by what each person chose — and
   somebody who chose two gets it both ways, which is the point. Or all one way, when
   something is urgent enough to text everybody. Calls read the message out, or play a
   recording of you reading it off the screen, chosen per message. Either way you can
   have Yours Truly ring you first and play back exactly what everyone else will hear.
5. **Look back.** Every send is kept: what it said, when it went, who it went to, on
   which channel, at which address, and what came back. Searchable per person and
   copyable as text.

## Setting it up

**[SETUP.md](SETUP.md) is the step-by-step guide** — creating the accounts, getting each
value Yours Truly asks for, and registering so the texts actually arrive. Written for
somebody who does not work with this sort of thing. About an hour, plus a few days of
waiting for one registration.

## Running it

    dotnet run --project src/YoursTruly.App

## Releasing

Every push to `main` becomes a release. Nothing to tag and no version to decide: the
build number supplies it, so versions always go up.

    git push            # that is the whole release process

Put `[skip release]` in the commit message to push without building one.

The workflow runs the tests, then builds and packages on three real runners — Windows,
Linux and Apple silicon — because an installer has to be made on the system it installs
onto. There is no Intel Mac build; GitHub's Intel runners now queue for a long time and
often never start, and the workflow says where to add it back.

It publishes the installers to a GitHub release, with named download links at the top of
the notes, alongside the manifest and package Yours Truly reads to notice that a newer
version exists. A release is a flat list of files with no folders to tidy into, so the
only lever is publishing fewer: the Squirrel-era `RELEASES` files and the
`assets.*.json` notes are dropped, because nothing reads them. **If an update ever stops
being found, putting those back is the first thing to try.**

The installers are unsigned — signing needs a paid Apple or Microsoft developer account
— so both operating systems warn on first launch. The release notes say how to get past
it. Updates after that are silent, because the app writes them itself and they are never
quarantined.

Yours Truly then updates itself: **Setup → Updates → Check for updates** downloads it and
offers to restart into it. That only works in a copy installed from a release; run from
a build folder there is nothing to replace, and it says so rather than failing quietly.

To build one locally without packaging:

    dotnet publish src/YoursTruly.App -c Release -r osx-arm64 -o publish

## Tests

    dotnet test

About 470 tests, a few seconds. The suite never needs a real directory. The PDF reader
is tested two ways: against synthetic files built in code with the same geometry as real
ones — three laid out like directory exports, one like a soccer roster, one with no
table in it at all — and against the six real PDFs in [`samples/`](samples/), which are
the same files anybody is invited to try the app on, so a sample that stops importing
fails the build. The window itself is tested headless, so a mistyped binding fails the
build rather than the user. If you drop a genuine export in
`tests/YoursTruly.Tests/Fixtures/private/` (gitignored), extra tests run against it and
check that every person the file's own footer counts comes back with a readable name,
phone, e-mail and birthday.

## Gmail's daily limit

Gmail will not slow you down as you approach its cap. It refuses outright, and then
refuses everything else for up to 24 hours — so a send can break off two thirds of the
way through a list with no obvious way to find out why.

So the Send screen carries a small bar: how many e-mails have gone out in the last 24
hours, against what the account is allowed. Google publishes two numbers —
[500 a day for a free account](https://support.google.com/mail/answer/22839) and
[2,000 for Google Workspace](https://knowledge.workspace.google.com/admin/gmail/gmail-sending-limits-in-google-workspace)
— and which applies is read off your address, since a free account is always at
gmail.com and a Workspace account always has a domain of its own. Past four fifths of
the limit it says so; past the limit it says how many will still fit and stops offering
to send.

Two things it cannot know. The window is a rolling 24 hours rather than a calendar day,
which is in its favour — the room comes back gradually instead of at midnight. But it
counts only what **this list** has sent: mail from the same account in another list, or
in Gmail in a browser, counts against the same quota and is invisible from here. Treat
the number as a floor. If you send from something that is not Gmail, the bar does not
appear at all, because a made-up limit shown confidently is worse than none.

## Sending as yourself

Yours Truly is one person's tool. It is not an organisation's system and should not look
like one: every message is sent to **one person at a time**, addressed only to them, and
signed with whatever you write in the signature box on the Setup screen.

    Practice is Friday at 6:30 at the field.
    — Jonathan Allen, Thunder FC

Nobody ever sees who else a message went to — not because recipients are hidden, but
because each message really is its own message.

### Can the texts come from your own number?

**Yes, through your own phone — and which phone decides how.** Setup has a toggle:

- **iPhone** — Yours Truly asks the Messages app on your Mac to send. **This only works with
  Yours Truly running on a Mac**; Messages exists nowhere else, so on Windows or Linux the
  option is refused rather than accepted and then failing at the moment you press Send.
- **Android** — install [SMS Gateway for Android](https://sms-gate.app/), turn on Local
  Server, and paste the address and sign-in it shows into Setup. Yours Truly then asks your
  phone over your own Wi-Fi. This works from any computer, and in local-server mode the
  numbers never leave your network.

Either way it is the same arrangement as Phone Link: the computer asks, the phone's own
line delivers.

**On a Mac with an iPhone, in detail:** Setup can send texts by asking Messages to
send them, which is how Phone Link works on Windows: the computer asks, the phone's own
line delivers. Messages then genuinely come from your number and replies arrive in your
own Messages app. Messages must be open and signed in, your iPhone needs Text Message
Forwarding switched on for that Mac, and macOS will ask once for permission to control
Messages.

The catch, for both phones, is that a personal line is meant for person-to-person
texting. A burst of hundreds is exactly what carrier spam systems look for, and what
they do about it is flag or block the number — your real one. So on those two routes
Yours Truly leaves a random 2-to-11-second gap between texts, which means a hundred of
them take about ten minutes with the window open. **Keep it to a few dozen** — a team, a
committee, the people who did not reply — and use Twilio for a whole directory.

**Twilio texts go out with no gaps at all.** The number is rented for the purpose,
Twilio paces its own sending against the carrier limits, and its whole business is
volume. Waiting would buy nothing and cost the evening: four hundred texts would be
three quarters of an hour of sitting there.

**Through a service, no.** Verify your mobile in the Twilio console as a caller ID and outgoing
calls show *your* number. People see you ringing, and returning the call reaches you
directly.

**Texts: no, and no service will let you.** Sending a text that appears to come from a
number you have not proven you control is spoofing; carriers block it and Twilio
forbids it. Hosting your own mobile number on Twilio is the supported way to send from
it, but it does not apply here twice over: ordinary mobile numbers from the big US
carriers generally cannot be hosted, and if yours could, Twilio would then receive your
personal texts instead of your phone.

What to do instead, which gets most of the way there:

1. **Buy a Twilio number in your own area code** so it reads as local rather than
   out-of-state.
2. **Forward its replies to your phone.** In the Twilio console, point the number's
   incoming-message webhook at a TwiML Bin containing
   `<Response><Message to="+1435...">{{From}}: {{Body}}</Message></Response>`. Replies
   then arrive in your normal Messages app with the sender's number in front of them. No
   server, no code.
3. **Let the signature do the recognising.** People do not recognise numbers; they
   recognise names. That is what the signature is for.

### RCS: making the sender recognisable

Paste a Twilio **Messaging Service** with an RCS sender attached into Setup, and texts
go out through it: phones that support RCS show a named, verified sender with proper
formatting, and every other phone gets exactly the SMS it would have got anyway, from
the same request. Leave it empty and nothing changes. There is no new channel and no new
preference — the people who chose "Text" simply get a better text where their phone
allows it.

One thing to check before spending time on it: **RCS sender registration is built for
businesses**, and Yours Truly exists to serve one person rather than an organisation. Ask
Twilio whether you can register an agent as an individual, and say plainly that the
sender name would be a person's. If the answer is no, leave the field empty — the
signature is doing that job already.

### Before the first real broadcast

Texting a list from a US long code requires **A2P 10DLC registration** — a one-off form
in the Twilio console and about $2 a month. Registered as a sole proprietor you get one
number and one campaign, roughly one message per second and a few thousand segments a
day. Sending to a few hundred people is comfortably inside that, and takes a few
minutes.

Unregistered traffic is filtered by the carriers, so this is not optional.

## Checking it is wired up without messaging anyone

**Twilio publishes test credentials.** In the Twilio console, under Account → API keys
& tokens, there is a second *Test* Account SID and Auth Token alongside the live pair.
Put those into Setup and Yours Truly's calls are fully validated but nothing is sent, nothing
is delivered, and nothing is charged. Twilio also reserves "magic" numbers that force a
particular outcome, which is how each failure message can be seen without waiting for a
real one:

| Number          | What Twilio does                    | What Yours Truly says |
| --------------- | ----------------------------------- | ----------------- |
| +15005550006    | succeeds                            | Sent              |
| +15005550001    | rejects it as an invalid number     | "Twilio does not recognise … as a phone number" |
| +15005550004    | reports the number as unsubscribed  | "…has replied STOP…text START…" |
| +15005550009    | reports it cannot receive texts     | "…is a landline, so it cannot receive a text" |
| +15005550002    | reports it as unroutable            | "…may be switched off, disconnected…" |

Put one of those in "Your own mobile (for tests)" and press the Test button.

Two caveats. The test credentials only validate requests — they will not place a real
call, so the **voice recording flow cannot be exercised with them**; that one needs the
live credentials and a real call to your own phone. And an invalid *From* number is
+15005550001, while +15005550007 is a number the account does not own.

**Email has no equivalent.** Google publishes no sandbox: an app password is against a
real mailbox. To try it without involving anyone, either send to yourself — which is all
the Test button does — or point Host and Port at a local mail catcher such as smtp4dev
or MailHog and watch the message arrive there.

## When somebody needs help

Yours Truly keeps a log beside its settings, one JSON object per line, thirty days of them.
It records what the app was asked to do and what failed: which screens were opened, how
big an import was, how many people a send reached, which credential test passed, the
full exception chain when something broke.

**It is safe to send to whoever is helping.** Names never go in it at all. An address is
masked to `a…e@e…m` or `…42 (11 digits)` — enough to match against a person on screen,
not enough to write to them. A provider's complaint has any address it quoted back taken
out, because those messages routinely repeat the number that failed. Messages are
recorded as a length and nothing else. A test asserts that a failed send leaves no name,
address or message text anywhere in the file.

Setup has a **Copy the recent log** button for pasting into an email.

## Privacy

Each list is a single unencrypted SQLite file, kept **wherever you put it**. There is no
default and no folder of its own: you choose the place when you import, the file picker
merely suggests Documents, and every list you have opened stays on the switcher at the
bottom of the rail. Nothing is written anywhere you did not choose.

Settings and the log are the app's own and live in the usual per-user place —
`%APPDATA%\Yours Truly` on Windows, `~/Library/Application Support/Yours Truly` on a Mac,
`~/.config/yours-truly` on Linux. They hold a mail app password and a Twilio token, which
are not documents and have no business sitting in Documents where they can be tidied away
or synced to a cloud drive.

Unencrypted is a deliberate choice: the files a list is imported from are unencrypted too,
so encrypting the copy would move the weak point rather than remove it. What matters
instead is that whoever uses Yours Truly understands what they are holding. The app says
so plainly, as the first card in Setup, and this repository will not accept a real export:
`tests/YoursTruly.Tests/Fixtures/private/` is gitignored, and so is `*.db`.

Treat any file you imported the way you would treat the directory itself.
