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

This v2 app does not (yet) include its own installer/key-generation
wizard - for now, either:

**Option A - reuse the v1 installer for setup, then run v2 for daily use:**
1. Run the v1 `Install-ValheimControl.ps1` as before (generates the SSH
   key, copies it to the server, writes `config.json`, creates shortcuts).
2. Replace the shortcut's target with the published `ValheimControl.exe`
   from this v2 project instead of the PowerShell script.

**Option B - manual setup:**
1. Generate a key: `ssh-keygen -t ed25519 -f %USERPROFILE%\.ssh\valheim_control_ed25519`
2. Copy the public key into `~/.ssh/authorized_keys` for the
   `valheim-control` user on the server.
3. Create `%APPDATA%\ValheimControl\config.json` by hand (see
   `config.example.json` in the v1 project for the format).
4. Run `ValheimControl.exe`.

*(A proper v2 installer/setup wizard is on the roadmap - see below.)*

## Notes on the SSH implementation

- Uses `Renci.SshNet` (SSH.NET) for a native, in-process SSH connection -
  no `ssh.exe` subprocess, no PATH dependency.
- Authenticates using the ED25519 private key referenced in `config.json`
  (`SshKeyPath`), same key file the v1 installer generates.
- Each button click opens a short-lived SSH connection, runs one command,
  and disconnects - simple and stateless, matching the v1 tool's behavior.

## Roadmap

- [ ] Built-in setup wizard (generate key, copy to server, write config) -
      no more dependency on the v1 PowerShell installer
- [ ] System tray icon with live status polling
- [ ] Toast notification when a scheduled backup completes
- [ ] MSI or Inno Setup packaged installer
- [ ] Persistent SSH connection option (avoid reconnect overhead per click)

## License

Same license as the v1 project (MIT) - see the main
`valheim-server-control` repository.
