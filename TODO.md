# Valheim Control - To-Do / Roadmap

Running list of planned work, kept outside the codebase so it survives
across sessions.

## Open

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

### Silent delta-based auto-updates (Velopack)
Follow-up to the enforced-update system already shipped (see Done below).
Right now, an outdated PC gets *blocked* with a screen linking to GitHub -
the person still has to manually download and replace the .exe themselves.
This item is about making that step automatic and only transferring
what actually changed between versions, instead of the whole app.

**The tool:** [Velopack](https://velopack.io) (actively maintained
successor to Squirrel.Windows). Confirmed via research, not just recalled
from training data:
- Generates real binary delta patches between versions - users only
  download the changed bytes, not a full reinstall
- Has built-in support for GitHub Releases as the update source
  (`GithubSource` in `UpdateManager`), fitting the release workflow
  already in use
- Handles the "a running .exe can't overwrite itself" problem internally -
  no need to hand-roll a separate updater helper process
- Written in Rust internally, exposed as a normal C# NuGet package

**What integrating it actually involves (this is a real pipeline change,
not a small addition):**
1. Add the `velopack` NuGet package
2. Add `VelopackApp.Build().Run()` as the literal first line of the app's
   startup, before anything else executes
3. Replace the current `dotnet publish` release step with Velopack's own
   `vpk` CLI, which packages build output into its release format
4. Each GitHub Release needs Velopack's structure (one full package + one
   delta patch per release) - can't just attach a bare `.exe` as a release
   asset the way the current manual process does
5. Add `UpdateManager.CheckForUpdatesAsync()` / `DownloadUpdatesAsync()`
   calls in the app

**Relationship to what's already built:** this would largely *replace*
the current mechanism (`AppVersion.cs`, `app_version.txt` on the server,
`AppUpdateRequiredWindow`) as the primary path - Velopack would update
people silently in the background, and the existing hard-block screen
would become more of a rare safety net (for a PC that somehow missed a
silent update) than the main line of defense.

**Decided:** hold off until the app's feature set is mostly settled, then
revisit - see how much friction the current manual-update-required flow
actually causes in practice first before taking on a real packaging
pipeline change.

## Done
(See conversation history for full detail on everything already shipped:
theme, Phase 2 stats, Tailscale remote access, Runestones config editor
with structured adminlist/permittedlist/bannedlist editing, Swap/Create/
Delete World with shared server-side delete password, World Modifiers
popup, Settings window (polling interval, guardrails, accent color, real
backup retention controls), Update tab + auto-update, and enforced app
updates - a version check on launch that blocks an outdated PC with a
screen linking to the latest release, backed by a small version file on
the server.)
