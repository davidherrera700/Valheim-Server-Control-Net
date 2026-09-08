# Valheim Control - To-Do / Roadmap

Running list of planned work, kept outside the codebase so it survives
across sessions.

## Open

### Phase 4: migrate the WPF app to the new API (in progress)
The backend is fully built and proven (see Done) - real accounts, JWTs,
salted passwords, and genuine per-request permission enforcement, all
tested end to end via curl and confirmed working from inside the actual
WPF app (Login window + one live authenticated call). What's left:

- **Login gating + remember-me.** Right now the Login window is a
  side "beta" button - it doesn't actually gate anything yet. Needs to
  become the real startup flow: no valid session -> Login window blocks
  everything (same no-bypass pattern as the enforced-update screen,
  closing it exits rather than sneaking through) -> valid session skips
  straight to the dashboard. "Remember me" = persist the JWT locally
  (never the password), valid for its normal 7-day life; expires or
  logout -> back to login.
- **Account page.** Shows the current user's username + role, a
  **change my own password** action (small new endpoint - update your
  *own* password without needing Owner involved), and a **logout**
  button.
- **Manage Users & Roles window (Owner-only).** A real UI over the
  `/api/users` and `/api/roles` endpoints already built and tested -
  list/create/delete users, assign roles, list/create/edit/delete roles
  with permission checkboxes. Opened from the dashboard, visible only
  when signed in as Owner.
- **Migrate each window's real functionality** from `SshService` calls
  to `ApiClient` calls, one at a time: main dashboard first, then
  Runestones, Update, Settings. Once done, the SSH-based path (and the
  shared `valheim-control` key) can actually be retired.

### Self-service account creation (deferred)
Considered and deliberately set aside for now: **Owner provisions
accounts** (via the Manage Users & Roles window above) rather than an
open "Create Account" button on the login screen. Reasoning: this is a
small trusted group, not a public service - self-registration would mean
anyone who can reach the Tailscale network could create their own
account, which undermines the whole point of real per-person accounts.
Handing someone a username/password directly (same as sharing a WiFi
password) is a five-second admin action, not a real bottleneck.

If ever revisited, would need some kind of gate on public registration
(an invite code/token, most likely) rather than fully open signup - and
notably, that gate starts to converge with the "approval" idea in the
next item below, so the two might end up being the same underlying
mechanism.

### Role promotion requests + notification/mailbox system (future)
From the Account page: a non-Owner user could request a different role
(e.g. "please upgrade me from Guest to Moderator"), and Owner would see
and act on these requests somewhere, rather than requests happening
out-of-band (a text message, etc.).

**What this actually requires - bigger than it first sounds:**
- **Data model:** a request record (from user, requested role, optional
  note, status: Pending/Approved/Denied, timestamps). Worth designing as
  a general-purpose Notification/Message system rather than a narrow
  single-purpose table, since the same mechanism would naturally extend
  to other things later (account created, role changed, a backup
  failure alert, etc.) - the immediate need is just promotion requests,
  but the underlying plumbing is the same either way.
- **New endpoints:** submit a request (any user), list pending requests
  (Owner - this is the "mailbox"), approve/deny (Owner - approving
  actually changes the user's RoleId), and "my requests" (so a user can
  see their own request's status).
- **How Owner actually notices a new request exists** - the real
  "notification" part. Simplest approach, consistent with patterns
  already used elsewhere in this app: periodic polling (e.g. every 30s,
  same idea as the existing status-refresh timer) with a badge/count
  shown somewhere Owner will see it, rather than building real push
  notifications (WebSockets/SignalR) - that's a genuinely bigger, more
  complex addition that's very likely overkill for a 4-person tool.
- **UI needed:** a "Request Role Change" control on the Account page
  (non-Owner), and a pending-requests panel with approve/deny, most
  naturally living inside the Manage Users & Roles window (Owner) -
  approving a request IS a role-assignment action, so it fits there
  rather than as a separate screen.
- **Open questions worth deciding when this gets picked up:** should a
  user be limited to one outstanding request at a time (simpler, avoids
  spam) or allow multiple? Should denial require/support a reason?
  Should this stay scoped to "request a different role" (picking from
  Owner-defined roles, matching how Roles were designed to keep
  permission complexity away from end users) rather than exposing raw
  individual permissions to request - recommend yes, keep it role-scoped.

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

**Also worth revisiting once EF Core migrations replace `EnsureCreated()`
on the API** (currently: schema changes require a fresh database, which
has meant re-bootstrapping the Owner account a few times during this
build-out). Not urgent while the schema's still actively evolving, but
worth doing once it stabilizes.

## Done
(See conversation history for full detail on everything already shipped:
theme, Phase 2 stats, Tailscale remote access, Runestones config editor
with structured adminlist/permittedlist/bannedlist editing, Swap/Create/
Delete World with shared server-side delete password, World Modifiers
popup, Settings window (polling interval, guardrails, accent color, real
backup retention controls), Update tab + auto-update, enforced app
updates, and the full ValheimControlApi backend - a real ASP.NET Core
service deployed on the Ubuntu server itself with genuine salted-password
authentication, JWT sessions, every action from the old SSH-based app
ported natively, and a real Roles/Users permission system with per-request
enforcement checked fresh against the database every time, proven working
both via curl and from inside the actual WPF app.)
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
