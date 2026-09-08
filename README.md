# Valheim Server Control (v3 - Real Accounts & API Backend)

> **Installing this for the first time?** See the
> [First-Time Setup Guide](FIRST_TIME_SETUP.md) - a plain-language
> walkthrough for anyone joining the server, not just developers.

A two-part system for remotely managing a self-hosted Valheim dedicated
server:

- **`src/ValheimControl`** - the Windows desktop app (WPF/.NET 8), what
  you actually run day-to-day.
- **`api/ValheimControlApi`** - a small ASP.NET Core (.NET 10) web
  service that runs **on the Ubuntu server itself**, alongside
  `valheim.service`. It's what actually performs every server action now
  (start/stop/backup/world management/config editing/updates), behind
  real user authentication.

This is a genuine architecture change from earlier versions of this
project, which talked to the server directly over SSH using one shared
key across all PCs. That model is now retired for day-to-day use - see
"What changed in v3" below.

## What changed in v3

- **Real user accounts.** Everyone who uses this app signs in with their
  own username and password - not a shared SSH key everyone has equal
  access through.
- **Real Roles & Permissions**, enforced fresh on every single request
  (not just hidden/disabled buttons in the UI). Owner has full access;
  everyone else only has whatever their assigned Role explicitly grants.
- **A real backend service** (`api/ValheimControlApi`) deployed on the
  Ubuntu server, with its own systemd unit, its own SQLite database of
  users/roles, and salted password hashing.
- **The old shared "delete password"** for world deletion is gone -
  deletion is now genuinely Owner-gated by the API instead.
- **One thing didn't move to the API**: the launch-time enforced-update
  check (`AppUpdateRequiredWindow`) still uses the original SSH
  connection to read `/opt/valheim/app_version.txt`. Everything else -
  the dashboard, Runestones, Update, Settings - runs on the API.

## Requirements to build

- **WPF app**: [.NET 8 SDK](https://dotnet.microsoft.com/download)
  (Windows), Windows 10/11
- **API**: [.NET 10 SDK](https://dotnet.microsoft.com/download) (can be
  installed alongside .NET 8 with no conflict), and on the server side,
  Ubuntu 26.04's own package repos already carry the matching
  `aspnetcore-runtime-10.0`

## Project Layout

```
ValheimControl.NET/
├── ValheimControl.sln
├── TODO.md                        # roadmap, kept outside the codebase
├── RELEASE_GUIDE.txt              # step-by-step release checklist
├── src/
│   └── ValheimControl/            # the WPF desktop app
│       ├── App.xaml.cs            # startup flow: config -> version check -> login gate -> dashboard
│       ├── MainWindow.xaml.cs     # main dashboard - fully API-backed
│       ├── LoginWindow.xaml.cs    # sign-in + remember-me
│       ├── AccountWindow.xaml.cs  # your own profile, password change, logout
│       ├── UsersRolesWindow.xaml.cs  # Owner-only: manage accounts and roles
│       ├── ConfigWindow.xaml.cs   # Runestones - config editing + world management
│       ├── UpdateWindow.xaml.cs
│       ├── SettingsWindow.xaml.cs
│       ├── SetupWindow.xaml.cs    # first-run SSH wizard (still used - see below)
│       ├── Models/AppConfig.cs
│       └── Services/
│           ├── ApiClient.cs       # talks to api/ValheimControlApi over HTTP
│           ├── SshService.cs      # legacy - only the version-check still uses this
│           └── ConfigService.cs
└── api/
    └── ValheimControlApi/         # the backend service - deployed to the Ubuntu server
        ├── README.md              # full deployment instructions live here
        ├── Program.cs             # every endpoint
        ├── Data/                  # User, Role, AppDbContext (EF Core + SQLite)
        └── Services/              # PasswordHasher, ProcessRunner, Permissions
```

## Building and running the WPF app (development)

```powershell
cd ValheimControl.NET
dotnet restore
dotnet run --project src\ValheimControl
```

## Publishing a single-file .exe (for distribution to the other PCs)

```powershell
cd ValheimControl.NET
dotnet publish src\ValheimControl -c Release -r win-x64 --self-contained true
```

Output lands at:
```
src\ValheimControl\bin\Release\net8.0-windows\win-x64\publish\ValheimControl.exe
```

Self-contained - the target PC doesn't need the .NET runtime installed.

## The API backend

Full build/deploy instructions (installing the runtime on Ubuntu,
publishing, the systemd service, testing with curl) live in
[`api/ValheimControlApi/README.md`](api/ValheimControlApi/README.md) -
that file is kept up to date as the source of truth for deployment,
rather than duplicated here.

## First-time setup on a new PC

For a plain-language, step-by-step walkthrough (aimed at anyone joining
the server, not just developers), see the
[First-Time Setup Guide](FIRST_TIME_SETUP.md).

The short version: two separate things need to happen, not just one -
the SSH wizard (connects this PC to the server, using the
`valheim-control` account) and signing in with a real personal account
(provisioned by Owner beforehand, not self-registration - see `TODO.md`
for the reasoning behind that design choice).

## Roles & Permissions

A fixed set of real, gate-able actions (starting/stopping the server,
backups, world management, config edits, updates, backup settings) -
Owner always has all of them; anyone else only has what their assigned
Role explicitly grants. Enforced with a fresh database lookup on every
single request, not just hidden buttons in the UI - see
`api/ValheimControlApi/Services/Permissions.cs` for the exact list.

## Roadmap

See `TODO.md` for the actively maintained list. At a glance, still open:
- User self-service role-change requests + a notification/mailbox system
- Silent delta-based auto-updates (Velopack) - deferred until the app
  settles down further
- EF Core migrations to replace `EnsureCreated()` on the API, now that
  schema changes have stabilized

## License

Same license as the v1 project (MIT) - see the main
`valheim-server-control` repository.
