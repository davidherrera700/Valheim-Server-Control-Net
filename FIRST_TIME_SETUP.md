# First-Time Setup Guide

This walks you through installing Valheim Control on your PC for the
first time. It takes about 5 minutes.

## Before you start

Two things need to happen **before** you can finish setup - ask
whoever's the Owner of the server (the person who set this up) for:

1. **The `valheim-control` SSH password** - a one-time password used to
   connect this PC to the server. You'll only need this once, during
   setup.
2. **Your own personal account** - a username and password just for you.
   The Owner creates this from inside the app (Account → Manage Users &
   Roles), so ask them to set this up for you before you start.

Don't have either of these yet? Get them from the Owner first - the
installer will get partway through and stop without them.

## Step 1 - Download the installer

Go to the [Releases page](https://github.com/davidherrera700/Valheim-Server-Control-Net/releases/latest)
and download **`ValheimControl-win-Setup.exe`** from the latest release
(it'll be listed under "Assets").

## Step 2 - Run the installer

Double-click `ValheimControl-win-Setup.exe`.

**Nothing visible happens for a few seconds - this is normal.** The
installer runs silently in the background rather than showing a wizard.
Give it about 10 seconds, then check your Start Menu - type
"ValheimControl" and you should see it show up.

> If Windows shows a blue "Windows protected your PC" SmartScreen
> warning, click **More info**, then **Run anyway**. This happens
> because the app isn't digitally signed (a paid certificate this
> project doesn't have) - it doesn't mean anything is actually wrong.

## Step 3 - Connect to the server

Launch **ValheimControl** from the Start Menu. The first time you open
it, you'll see a connection setup screen with a few fields already
filled in:

| Field | Value |
|---|---|
| Server IP or hostname | Already filled in (the server's local network address) |
| SSH username | Already filled in (`valheim-control`) |
| Port | Already filled in (`22`) |
| Password | **Enter the `valheim-control` password the Owner gave you** |

Click **Install / Connect**. You'll see a log of what's happening -
generating a security key for this PC, connecting to the server, and
testing the connection.

If everything connects cleanly, the app **automatically moves on to the
sign-in screen after a couple seconds** - no button to click. If it
shows a warning instead (connection test didn't fully succeed), it'll
wait for you to review the message and click **Continue** yourself
before moving on.

## Step 4 - Sign in

This is a separate step from the connection above - it's your **personal
account**, not the server password you just used.

Enter the username and password the Owner created for you, then click
**Sign In**. The app remembers this afterward, so you won't need to
enter it again on future launches unless you explicitly log out.

## You're in

The main dashboard should open. What you can actually do from here
depends on the role the Owner assigned you - check the **Account** page
in the app to see exactly what you have access to.

## Something not working?

- **"Incorrect username or password" at sign-in** - double check with
  the Owner that your account was actually created, and that you're
  typing your *personal* account, not the `valheim-control` server
  password from Step 3.
- **Connection test fails in Step 3** - double-check the `valheim-control`
  password with the Owner; it's easy to mistype.
- **Anything else** - reach out to the Owner directly. They can check
  server-side logs that aren't visible from your PC.
