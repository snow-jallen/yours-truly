# Texting from your iPhone, from any computer

Yours Truly can send texts that genuinely come from your own iPhone number, with the
replies arriving in your own Messages app. On a Mac it can do that by itself, by asking
the Messages app to send. This guide is for every other case:

- Yours Truly runs on **Windows or Linux**, or
- it runs on a Mac that is **not** the one signed in to Messages, or
- the Mac signed in to Messages is **somewhere else**, such as at the office while you
  are at home.

The answer is Yours Truly itself. Install it on a Mac signed in to Messages, switch on
**Let my other computers text through this Mac**, and that Mac sends texts for your
other computers. There is nothing else to install.

**Set aside about twenty minutes.** Nothing here costs money.

## How it fits together

    Yours Truly on your laptop (Windows, Linux or Mac)
          │  "send this to +1 435 555 0101"  — with the key
          ▼
    Yours Truly on a Mac signed in to Messages  (an icon in its menu bar)
          │
          ├─ iMessage (blue) ─────────────► goes straight from the Mac through Apple
          │
          └─ text message (green) ───► your iPhone, wherever it is ───► the carrier

- **iMessages do not need your phone at all.** The Mac sends them itself.
- **Texts to people without an iPhone go through your iPhone**, by Text Message
  Forwarding. The phone has to be switched on and online, on Wi-Fi or mobile data, but it
  does **not** have to be near the Mac.
- **The Mac has to be awake, with you logged in.** If it is asleep, nothing goes out,
  and Yours Truly says so.

## What it can and cannot do

This is deliberately small, so it is easy to trust:

- **It can only send.** It has no way to read your messages. It never asks for Full Disk
  Access, the macOS permission that reading them would need.
- **It only answers computers on the Mac's own network or on your Tailscale network**,
  and only those that have the key. A request from anywhere else on the internet is
  refused before the key is even checked.
- **It locks out a computer that gets the key wrong five times**, for ten minutes.
- **It sends at most 60 texts an hour and 200 a day**, to protect your number from
  being flagged as spam, and to limit the damage if the key ever got out.
- **While it is on, a Yours Truly icon sits in the Mac's menu bar.** It shows how many
  texts were sent today and has a **Pause** button.

The usual limit for texting from your own number still applies: **a few dozen at a
time**, not a whole directory. Carriers watch personal numbers for bursts. Yours Truly
leaves a gap of a few seconds between texts for that reason.

## What you need

- A Mac signed in to Messages with **the same Apple Account as your iPhone**. An old Mac
  mini that stays on is ideal.
- Your iPhone.
- Yours Truly on the Mac **and** on whichever computer you want to send from.
- For sending from outside the Mac's own network, **Tailscale** on both, signed in to
  the same account. It is free for personal use, at <https://tailscale.com>. It is what
  makes "at home while the Mac is at the office" work, without opening anything to the
  internet.

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

### Keeping it awake and logged in

The Mac only texts while it is awake and you are logged in. If it lives somewhere you
are not:

- **System Settings → Energy** (or **Battery → Options** on a laptop): switch on
  **Prevent automatic sleeping when the display is off**, and **Start up automatically
  after a power failure** if it is offered.
- **System Settings → Users & Groups → Automatically log in as**: choose yourself, so a
  power cut does not leave it waiting at the login screen. If FileVault is on, macOS
  will not offer this. In that case somebody has to type the password after a restart.

## Part 2 · Switching it on, on the Mac

1. Install Yours Truly on the Mac from the
   [Releases page](https://github.com/snow-jallen/yours-truly/releases), the
   `YoursTruly-osx-Setup.pkg` file, and open it.
2. Open **Setup** and scroll to **Let my other computers text through this Mac**.
3. Make sure **Your own mobile (for tests)**, in the Twilio card, has your own number in
   full: `+1`, then the area code, then the number. The test in step 6 goes there.
4. Press **Switch on**.
5. macOS may tell you about it, which is expected:
   - **"Background Items Added"** is macOS telling you Yours Truly will now start at
     login. If you ever turn it off by accident, it is under **System Settings → General →
     Login Items & Extensions → Allow in the Background**.
   - **"Do you want the application Yours Truly to accept incoming network
     connections?"** appears only if the Mac's firewall is on. Press **Allow**.
6. Press **Text myself through this Mac**. macOS asks **"Yours Truly wants access to
   control Messages"**. Press **OK**. The test should arrive on your iPhone a few
   seconds later. If you missed the question, the setting is **System Settings →
   Privacy & Security → Automation → Yours Truly → Messages**.

A Yours Truly icon is now in the menu bar at the top of the screen. Leave it there. It
starts by itself when you log in.

The card now shows **addresses** and a **key**. Keep that screen open for Part 3.

## Part 3 · Which address to use

The card lists every address the Mac has, and says what each one is:

| The address says | Works from |
| --- | --- |
| **Tailscale** (starts `http://100.`) | **Anywhere**, from any computer on your Tailscale network. Never changes. Use this one if it is there. |
| **this network** (starts `http://192.168.`, `http://10.` or `http://172.`) | Only a computer on the **same Wi-Fi or office network** as the Mac. Can change when the Mac reconnects. |
| **this Mac's name** (ends `.local`) | The same network, by name instead of number. Survives the number changing. |

With Tailscale's MagicDNS switched on, which is the default, you can also use the Mac's
Tailscale name, such as `http://office-mac:47474`. The name is in the Tailscale app.

**Copy the addresses and key** puts all of it on the clipboard, ready to paste into
something only you can see, such as a note to yourself. **Anybody with the address and
the key can send texts as you**, so do not paste it anywhere public.

## Part 4 · Filling it into Yours Truly on your other computer

On the computer you want to send from, open **Setup**.

1. Under **Text messages → Where texts go out from**, choose **From my iPhone, through
   Yours Truly on a Mac**.
2. Paste the **address** into *Your Mac's address*. You can leave off the `:47474` on
   the end, because Yours Truly adds it.
3. Paste the **key** into *Key*. Capitals, dashes and stray spaces do not matter.
4. Make sure **Your own mobile (for tests)** has your number, as on the Mac.
5. Press **Text myself through my Mac**. The test should appear on your iPhone within a
   few seconds.
6. Press **Save**.

From then on, everybody who chose texts gets theirs from your number, and the History
screen records each one.

Phone calls still go through Twilio if you set it up, whichever route texts take.

---

## Looking after it

- **Pause** in the menu-bar icon stops all sending until you press **Resume**. Use it
  if something looks wrong.
- **Stop until I next log in** in the same menu does what it says.
- **Switch off** in Setup on the Mac removes the login item altogether. The key is kept,
  so switching on again needs nothing changed on your other computers.
- **Make a new key** in Setup on the Mac is for a key that has gone somewhere it should
  not have. The old one stops working at once, and every other computer needs the new
  one.
- **Updates look after themselves.** When Yours Truly updates on the Mac, the menu-bar
  part restarts on the new version within a few minutes.

## What to expect

**Somebody without iMessage.** The Mac tries iMessage first, then a text message if
Messages refuses. Sometimes Messages accepts the iMessage and only marks it **Not
Delivered** a little later, after Yours Truly has already recorded it as sent. After your
first send to a new group, glance at Messages on the Mac. If a conversation shows a red
**Not Delivered**, that person needs reaching another way: tick Email or Call for them on
the People screen.

**"Sent" means "handed to Messages."** Yours Truly cannot see what happened after that,
because seeing it would mean reading your messages, which this deliberately cannot do.

**Replies** arrive in Messages on your iPhone and on the Mac, like any other text. Yours
Truly does not see them.

---

## When something goes wrong

Yours Truly says what happened in plain words. The ones you might meet:

| What Yours Truly says | What to do |
| --- | --- |
| …could not reach your Mac at… | The Mac is asleep or off, you are not logged in on it, or the relay has been switched off or stopped from its menu. Or this computer is not on the Mac's network or Tailscale. Try the Tailscale address. |
| Your Mac did not accept the key | Copy the key again from Setup on the Mac. Somebody may have pressed **Make a new key**. |
| …stopped answering this computer for a few minutes | Five wrong keys. Fix the key, wait ten minutes, try again. |
| …only answers computers on its own network or on your Tailscale network | This computer is reaching the Mac from outside. Put both on Tailscale and use the Tailscale address. |
| Your Mac has paused texting for other computers | Click the Yours Truly icon in the Mac's menu bar and choose **Resume**. |
| Your Mac has sent 60 texts in the last hour… | The hourly limit. Everything up to that point went out; send the rest in an hour. |
| …On the Mac: macOS has not let Yours Truly control the Messages app yet… | On the Mac: **System Settings → Privacy & Security → Automation → Yours Truly**, and switch on **Messages**. |
| …On the Mac: The Messages app is not running… | Open Messages on the Mac and make sure you are signed in. |
| That address reached something, but not Yours Truly on a Mac | The address or port is wrong. The port is usually `:47474`. |
| Your Mac took too long to answer… | It may still have sent it. Look in Messages on the Mac before sending to that person again, so they do not get it twice. |
