# Valheim Server Control (v2 - C#/.NET)

A WPF rewrite of the original PowerShell tool - same functionality
(Start/Stop/Restart/Backup/Status/Logs over SSH), but as a real compiled
`.exe`: a taskbar icon, no console window, and no dependency on the
Windows OpenSSH client being installed (SSH is handled natively via
[SSH.NET](https://github.com/sshnet/SSH.NET)).

This version **reuses the same `config.json`** the v1 installer writes to
`%APPDATA%\ValheimControl\config.json` - if you already ran the v1
installer on a PC, this app will just work with no reconfiguration.

## Requirements to build

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (Windows)
- Windows 10/11 (WPF is Windows-only)

## Project Layout

```
ValheimControl.NET/
├── ValheimControl.sln
├── src/
│   └── ValheimControl/
│       ├── ValheimControl.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml / MainWindow.xaml.cs
│       ├── Models/
│       │   └── AppConfig.cs
│       ├── Services/
│       │   ├── ConfigService.cs
│       │   └── SshService.cs
│       └── Assets/
│           └── valheim.ico      # (optional) add your own icon here
├── .gitignore
└── README.md
```

## Building and running (development)

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

The output `.exe` will be at:

```
src\ValheimControl\bin\Release\net8.0-windows\win-x64\publish\ValheimControl.exe
```

This is a **self-contained** build, meaning the target PC does **not**
need the .NET runtime installed - just copy `ValheimControl.exe` over
(it's a larger file, ~60-80 MB, because the runtime is bundled in).

## First-time setup on a new PC

The app now includes a built-in setup wizard - no dependency on the v1
PowerShell installer.

1. Run `ValheimControl.exe` (or `dotnet run --project src\ValheimControl`
   during development) on a PC with no existing config.
2. Since no `config.json` exists yet, the **Setup** window opens automatically.
3. Enter:
   - **Server IP or hostname** - your Ubuntu server's LAN IP
   - **SSH username** - typically `valheim-control`
   - **SSH port** - `22` unless you've changed it
   - **Account password** - entered once, never stored, used only to copy
     this PC's new public key into the server's `authorized_keys`
4. Click **Run Setup**. The wizard will:
   - Generate a new ED25519 key pair for this PC (or reuse one if it
     already exists at `%USERPROFILE%\.ssh\valheim_control_ed25519`)
   - Copy the public key to the server over a password-authenticated SSH
     connection
   - Write `%APPDATA%\ValheimControl\config.json`
   - Test a passwordless connection using the new key
   - Create Desktop and Start Menu shortcuts
5. Once you see "Setup complete", click **Continue to App** to open the
   main control window.

**Note:** key generation still shells out to `ssh-keygen.exe` (the
Windows OpenSSH Client optional feature), since .NET/SSH.NET doesn't
provide a key-generation utility. This is only needed once, during
setup - normal day-to-day use (Start/Stop/Restart/Backup/Logs) never
touches `ssh.exe` and works even if the OpenSSH client isn't installed.

If you're running via `dotnet run` during development, shortcuts will
point at a temporary build path. Re-run the wizard after
`dotnet publish` (see below) to point shortcuts at the permanent `.exe`.

*(If you already ran the old v1 PowerShell installer on this PC, its
`config.json` is fully compatible - v2 will detect it and skip the
wizard entirely.)*

## Notes on the SSH implementation

- Uses `Renci.SshNet` (SSH.NET) for a native, in-process SSH connection -
  no `ssh.exe` subprocess, no PATH dependency.
- Authenticates using the ED25519 private key referenced in `config.json`
  (`SshKeyPath`), same key file the v1 installer generates.
- Each button click opens a short-lived SSH connection, runs one command,
  and disconnects - simple and stateless, matching the v1 tool's behavior.

## Server-side permission required for "Backup + Reboot Server"

That button additionally needs a `reboot` rule in
`/etc/sudoers.d/valheim-control` (not required for the other buttons):

```
valheim-control ALL=(root) NOPASSWD: /usr/sbin/reboot
```

Run `which reboot` on the server first to confirm the exact path before
adding this line - sudoers matches the path exactly, and it's occasionally
`/sbin/reboot` instead depending on the distro.

## Roadmap

- [x] Built-in setup wizard (generate key, copy to server, write config)
- [ ] System tray icon with live status polling
- [ ] Toast notification when a scheduled backup completes
- [ ] MSI or Inno Setup packaged installer
- [ ] Persistent SSH connection option (avoid reconnect overhead per click)

## License

Same license as the v1 project (MIT) - see the main
`valheim-server-control` repository.
