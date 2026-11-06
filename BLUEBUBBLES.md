# Texting from your iPhone through BlueBubbles

Yours Truly can send texts that genuinely come from your own iPhone number, with the
replies arriving in your own Messages app. On a Mac it can do that by itself, by asking
the Messages app to send. This guide is for every other case:

- Yours Truly runs on **Windows or Linux**, or
- it runs on a Mac that is **not** the one signed in to Messages, or
- the Mac signed in to Messages is **somewhere else** — at the office while you are at
  home.

The piece that makes it work is **BlueBubbles Server**, a free, open-source app for the
Mac. It lets other computers ask that Mac's Messages app to send. Yours Truly asks it over
the network, so the Mac can be in another building, as long as Yours Truly can reach it.

**Set aside about half an hour.** Nothing here costs money.

## How it fits together

    Yours Truly (any computer)
          │  "send this to +1 435 555 0101"
          ▼
    BlueBubbles Server, on a Mac signed in to Messages
          │
          ├─ iMessage (blue) ─────────────► goes straight from the Mac through Apple
          │
          └─ text message (green) ───► your iPhone, wherever it is ───► the carrier

- **iMessages do not need your phone at all.** The Mac sends them itself.
- **Texts to people without an iPhone go through your iPhone**, by Text Message
  Forwarding. The phone has to be switched on and online, on Wi-Fi or mobile data, but it
  does **not** have to be near the Mac.
- **The Mac has to be awake, signed in, and running BlueBubbles.** If the Mac is asleep,
  nothing goes out, and Yours Truly says so.

The usual limit for texting from your own number still applies: **a few dozen at a
time**, not a whole directory. Carriers watch personal numbers for bursts. Yours Truly
leaves a gap of a few seconds between texts for that reason.

## What you need

- A Mac running macOS 10.15 (Catalina) or newer, signed in to Messages with **the same
  Apple Account as your iPhone**. An old Mac mini that stays on is ideal.
- Your iPhone.
- Yours Truly, on whichever computer you like.

Keep a note of two things as you go, somewhere private. Together they let somebody send
messages as you:

- [ ] The **server address** BlueBubbles shows
- [ ] The **server password** you choose for it

---

## Part 1 · Getting the Mac and the iPhone ready

1. On the Mac, open **Messages** and sign in with your Apple Account if it is not
   already.
2. In Messages, open **Settings → iMessage**. Under **Start new conversations from**,
   choose **your phone number**, not your email address. Otherwise people receive
   iMessages from an email address they do not recognise.
3. On the iPhone, go to **Settings → Apps → Messages → Text Message Forwarding** and
   switch on the Mac. Without this, texts to people who do not have an iPhone will not
   go.
4. On the Mac, send yourself a message from Messages, then send one to a friend with an
   Android phone. If both arrive, the Mac is ready. If the second does not, step 3 is
   the reason.

## Part 2 · Installing BlueBubbles Server

1. On the Mac, open <https://github.com/BlueBubblesApp/bluebubbles-server/releases>.
2. Under the newest release, open **Assets** and download the file ending in `.dmg`.
3. Open it and drag **BlueBubbles** into **Applications**.
4. Open BlueBubbles from Applications. If macOS asks whether you are sure you want to
   open an app downloaded from the internet, say **Open**.

## Part 3 · The BlueBubbles setup screens

BlueBubbles walks you through its setup screens the first time it opens. Here is what
each one is for, and what Yours Truly needs from it.

### Permissions

BlueBubbles needs **Full Disk Access**, so it can see the messages Messages has sent.
Press the button beside it. macOS opens **System Settings → Privacy & Security → Full
Disk Access**. Switch BlueBubbles on there, then return to BlueBubbles.

**Accessibility** is optional and Yours Truly does not need it.

### Connection

This is the screen that matters most.

1. **Server Password.** Choose a long one, at least four or five words strung together,
   and write it down. Anybody with this password and the address can send messages as
   you and read your conversations, so do not reuse a password from anywhere else.
2. **Proxy service.** This decides how other computers reach the Mac. Choose using the
   table in Part 4 below, then come back.

### Notifications & Firebase

**Skip this.** It is for BlueBubbles' own phone apps, to tell them when a message
arrives. Yours Truly only sends, so it never needs it. BlueBubbles will warn that
Firebase is not set up. That is fine.

### Private API (advanced, optional)

**Skip this too.** It unlocks extras like typing indicators and reactions. Setting it up
means switching off a macOS security protection (System Integrity Protection), which is
not worth doing for Yours Truly. Part 6 explains the one thing it would improve.

### Configurations

Tick both of these:

- **Startup with macOS**, so BlueBubbles starts again after the Mac restarts.
- **Keep macOS Awake**, so the Mac does not sleep and drop off the network. If it is a
  laptop, also make sure it does not sleep when the lid is closed.

If the Mac is somewhere you cannot reach easily, also turn on **System Settings → Energy
→ Start up automatically after a power failure**. The setting is under **Battery** or
**Energy Saver** on some Macs. Also set the Mac to log in automatically, so a power cut
does not leave it waiting at the login screen.

---

## Part 4 · How Yours Truly reaches the Mac

The **Proxy service** choice in BlueBubbles decides what address Yours Truly uses. Pick
the first row that fits:

| Your situation | Choose in BlueBubbles | The address to give Yours Truly | Notes |
| --- | --- | --- | --- |
| You already use **Tailscale** on both computers | **LAN URL** (or any) | `http://` + the Mac's Tailscale name + `:1234`, e.g. `http://office-mac:1234` | Recommended. Nothing is exposed to the internet, and the address never changes. Install Tailscale on the Mac and sign in to the same tailnet. |
| Yours Truly and the Mac are always on **the same home or office network** | **LAN URL** | What BlueBubbles shows, e.g. `http://192.168.1.20:1234` | Stops working the moment you are on a different network. |
| The Mac is somewhere else, and you want an address that **stays the same** | **Ngrok** (free account, with your free static domain) or **Zrok** (tick reserve) | What BlueBubbles shows, starting `https://` | Needs a free account with that service. BlueBubbles asks for its token. |
| You just want to try it | **Cloudflare** (the default) | What BlueBubbles shows, ending `trycloudflare.com` | No account needed, but **the address changes every time BlueBubbles restarts**. Paste the new one into Yours Truly each time. |

Whichever you chose, the address is on BlueBubbles' home screen, labelled **Server
Address**, with a button to copy it.

To check it from the computer running Yours Truly, open a web browser on that computer
and go to the address with `/api/v1/ping?password=` and your password on the end, e.g.
`http://office-mac:1234/api/v1/ping?password=your-password`. A page saying
**"Ping received!"** means Yours Truly will be able to reach the Mac.

---

## Part 5 · Filling it into Yours Truly

On the computer running Yours Truly, open **Setup**.

1. Under **Text messages → Where texts go out from**, choose **From my iPhone, through
   BlueBubbles on a Mac**.
2. Paste the **server address** into *Your Mac's address in BlueBubbles*.
3. Type the **server password** into *Server password*.
4. Make sure **Your own mobile (for tests)**, in the Twilio card above it, has your own
   number written in full: `+1`, then the area code, then the number. That is where the
   test goes.
5. Press **Text myself through my Mac**. Within a few seconds the test should appear on
   your iPhone, in your conversation with yourself.
6. Press **Save**.

From then on, everybody who chose texts gets theirs from your number, and the History
screen records each one as sent through your Mac.

Phone calls still go through Twilio if you set it up, whichever route texts take.

---

## Part 6 · What to expect

**Somebody without iMessage.** Yours Truly first tries iMessage, then a text message if
the Mac refuses. Sometimes the Mac does not refuse straight away. It accepts the
iMessage and only marks it **Not Delivered** in Messages a little later, after Yours
Truly has already recorded it as sent. After your first send to a new group, glance at
Messages on the Mac. If a conversation shows a red **Not Delivered**, that person needs
reaching another way: tick Email or Call for them on the People screen.

The Private API (Part 3) would fix this, because it lets Yours Truly ask Apple
beforehand whether a number has iMessage. Yours Truly uses it automatically if it is
set up. It is not worth switching off macOS security protections for this alone.

**Replies** arrive in Messages on your iPhone and on the Mac, like any other text. Yours
Truly does not see them.

**Sending is not instant.** BlueBubbles waits until Messages confirms each message, and
Yours Truly leaves a gap of a few seconds between texts. A few dozen take a few minutes.

---

## When something goes wrong

Yours Truly says what happened in plain words. The ones you might meet:

| What Yours Truly says | What to do |
| --- | --- |
| …could not reach your Mac at… | The Mac is asleep or off, BlueBubbles is not running, or, with Cloudflare, the address has changed since BlueBubbles last restarted. Copy the current **Server Address** from BlueBubbles again. |
| …did not accept the password | Copy the **Server Password** from BlueBubbles' settings again, exactly. |
| …stopped answering this computer for a few minutes | Too many wrong passwords. BlueBubbles locks this computer out briefly. Fix the password, wait five minutes, try again. |
| That address reached something, but not BlueBubbles | The address is wrong, or the port is missing. It usually ends `:1234` on Tailscale or a LAN. |
| Messages on your Mac would not send to… | That person has no iMessage and the text could not go either. Check the iPhone is on and online, and that Text Message Forwarding is still on for the Mac (Part 1, step 3). |
| Your Mac took too long to answer… | The Mac may still have sent it. Look in Messages on the Mac before sending to that person again, so they do not get it twice. |
| Your Mac would not send to… Open BlueBubbles Server… | Update BlueBubbles: on the Mac, open BlueBubbles, and it offers updates on its home screen. Or download the newest release again (Part 2). |
