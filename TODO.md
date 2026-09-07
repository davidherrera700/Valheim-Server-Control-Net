# Valheim Control - To-Do / Roadmap

Running list of planned work, kept outside the codebase so it survives
across sessions.

## Open

### Settings window
Held over from the original Phase 2 plan - app-wide preferences window.
Scope agreed so far:
- Polling interval (currently hardcoded 30s in MainWindow)
- Guardrails: confirm-before-destructive (partially already built via
  MessageBox confirms on Stop/Backup+Reboot), "block stop while players
  online" (best-effort only - we can't reliably know who's currently
  connected, only who's *joined* since last restart)
- Accent color swatches (cosmetic theme swap)
- Retention settings for *our own* systemd backup script (frequent/daily
  counts) - NOT Valheim's native `-backups`/`-backupshort`/`-backuplong`
  flags, which would be a second, redundant backup system running
  alongside the one we already have

### Flesh out the Files page (Runestones)
`adminlist.txt`, `permittedlist.txt`, and `bannedlist.txt` are currently
just raw text boxes with placeholder comments. Worth a friendlier,
structured editor instead of raw text:
- One row per Steam ID, with an "Add" field and a delete button per row,
  rather than hand-editing lines in a text box
- Possibly a name/nickname column next to each ID, stored as a comment on
  the same line (Valheim itself ignores anything after the ID, so this is
  safe) so the list is actually readable months later
- Consider whether to pull Steam IDs automatically from recent Saga Log /
  journal join events (we already parse "Got character ZDOID from ...",
  though that gives names, not IDs - the log format for the SteamID64
  itself would need separate verification before relying on it)

### User roles & permissions
Bigger one - flagged for careful design before building, not a quick add.

**The idea:** everyone who opens the app on any of the 4 PCs gets
assigned to a profile. An "Owner" role has full access; other roles get a
configurable, restricted permission set (e.g. a "Moderator" role that can
Start/Restart/Backup but not Stop, Delete World, or change the Update/
Delete-password settings).

**Why this needs real design work, not just quick wiring:**
- **Where do roles live?** Needs to be shared across all 4 PCs for the
  same reason the delete-password moved server-side - a role stored only
  in one PC's local `config.json` wouldn't actually restrict anything on
  the other 3. Likely needs a small server-side profile store (a JSON
  file the app reads/writes over SSH, similar pattern to the delete
  password's hash file) rather than per-PC storage.
- **What identifies "who's using this PC right now"?** The app currently
  has no login step at all - it's just "whoever's sitting at this
  Windows PC." Would need some kind of profile picker/login on launch
  (a name + a shared or per-person passphrase, most likely, rather than
  anything resembling real account security - this is a home LAN/
  Tailscale tool, not a public service).
- **Permission enforcement points:** every button that performs an SSH
  action (Start/Stop/Restart/Backup/Reboot/Update/world Create-Swap-
  Delete/config file writes) would need a permission check before it's
  even clickable, not just before the SSH call fires - otherwise a
  restricted user could still see a live-but-disabled button and be
  confused, or worse, find a path that skips the check.
- **Who manages roles?** Presumably only Owner can create/edit other
  profiles and their permissions - needs its own small management UI.
- **Relationship to the existing delete-password gate:** once roles
  exist, does the delete password become redundant (folded into a
  "can delete worlds" permission), or does it stay as an extra
  independent barrier on top? Worth deciding explicitly rather than
  ending up with two overlapping, inconsistent gates.

Recommend scoping this as its own multi-session project once picked up -
likely: (1) design the profile/permission data model and where it lives,
(2) build the server-side storage + a ProfileService, (3) build a login/
profile-picker screen, (4) wire permission checks into every existing
action, (5) build the Owner-only management UI last, once the
enforcement plumbing is proven.

### Enforced app updates
When you push an official update to the project, every PC should be
required to update before continuing to use the app - not just a
"new version available" nag.

**Why this matters beyond convenience:** the server-side scripts
(`read_config.sh`, `write_config.sh`, `delete_world.sh`, etc.) are now
shared infrastructure across all 4 PCs. If one PC is running an older
version of the app that assumes a different script contract (missing a
script, different expected arguments), it could fail confusingly or, worse,
do something unintended. Forcing everyone onto the same version keeps the
client/server contract consistent.

**Design decisions to make before building:**
- **Where "latest required version" lives:** querying GitHub's API directly
  runs into trouble if the repo is private (needs auth) and adds a new
  external dependency. Simpler and more consistent with how we already
  handle shared state (e.g. the delete password): store a small version
  file on the Ubuntu server itself (e.g. `/opt/valheim/app_version.txt`,
  containing the required version number, release notes, and a link),
  updated manually whenever you cut an official release. The app then
  checks it over the SSH connection it already has, rather than adding a
  second trust relationship with GitHub.
- **How the app knows its own version:** needs a version constant embedded
  at build time (simple - e.g. an `AppVersion` constant bumped each
  release, or wired to the assembly version).
- **Hard block vs. soft nag:** given the shared-script-compatibility
  reasoning above, this should probably be a real block - a modal "Update
  Required" screen shown before the main window loads, not a dismissible
  banner - but only once a mismatch is *confirmed*. If the version check
  itself fails (server unreachable, etc.), the app should fail open and let
  you in rather than lock you out over a network hiccup.
- **Getting the actual update onto each PC:** this app isn't distributed
  through anything auto-update-capable (no MSIX/Squirrel/ClickOnce) - it's
  a manually-published folder per PC. Realistic first version: the "Update
  Required" screen links to the GitHub release/repo with instructions,
  rather than attempting to download and hot-swap the running .exe (doable
  later via a small separate updater process, but real added complexity -
  not worth it for a 4-PC home tool unless it becomes annoying to do
  manually).
- **Publishing flow for you:** bump the version constant, update the
  server-side version file, and ideally cut an actual GitHub Release with
  the built `.exe` attached so there's somewhere concrete to point people.

## Done
(See conversation history for full detail on everything already shipped:
theme, Phase 2 stats, Tailscale remote access, Runestones config editor,
Swap/Create/Delete World with shared server-side delete password, Update
tab + auto-update, World Modifiers popup.)
