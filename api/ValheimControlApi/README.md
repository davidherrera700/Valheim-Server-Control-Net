# Valheim Control API - Phase 1 (Foundation)

This is the first phase of the real backend service replacing the shared-
SSH-key model. Right now this proves the whole chain works end to end -
login, JWT tokens, one real authenticated action - before the other ~15
actions (start/stop/backup/world management/etc.) get ported over in
later phases.

## What's here

- Real user accounts with salted PBKDF2 password hashing (not the simple
  SHA-256 "courtesy gate" the delete password uses elsewhere - this is
  genuine security)
- JWT-based login (`/api/auth/login`)
- One real authenticated endpoint (`/api/status`) that runs `systemctl`
  natively on the server - no SSH involved, since this code already runs
  on the box
- Runs as its own systemd service, bound only to the Tailscale interface

## What's deliberately NOT here yet (future phases)

- Every other action (start/stop/restart/backup/world swap-create-delete/
  config editing/updates) - still only in the SSH-based WPF app for now
- A full granular permission system - this phase only has `IsOwner`
  (true/false), not per-permission toggles yet
- Any changes to the Windows app itself - it still talks over SSH; this
  API isn't wired into it yet

## First-time setup on the server

1. Install the ASP.NET Core runtime (Ubuntu 26.04's own package repos
   carry .NET 10, confirmed via `apt search aspnetcore-runtime` - not
   .NET 8, since Canonical packages whichever LTS was current when 26.04
   shipped):

   ```bash
   sudo apt update
   sudo apt install -y aspnetcore-runtime-10.0
   ```

2. On your Windows PC, publish the API for Linux:

   ```powershell
   dotnet publish api\ValheimControlApi -c Release -r linux-x64 --self-contained false
   ```

   Output lands at:
   ```
   api\ValheimControlApi\bin\Release\net10.0\linux-x64\publish\
   ```

3. Copy that whole `publish` folder to the server:

   ```powershell
   scp -r api\ValheimControlApi\bin\Release\net8.0\linux-x64\publish\* davidherrera700@100.71.61.39:~/valheim-control-api/
   ```

4. SSH in and move it into place:

   ```bash
   sudo mkdir -p /opt/valheim-control-api
   sudo mv ~/valheim-control-api/* /opt/valheim-control-api/
   sudo chown -R root:root /opt/valheim-control-api
   ```

5. Copy the systemd unit file (also in this folder,
   `valheim-control-api.service`) to the server the same way, then:

   ```bash
   sudo mv ~/valheim-control-api.service /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now valheim-control-api
   ```

6. Check it's running:

   ```bash
   systemctl status valheim-control-api
   ```

## Testing it (from your Windows PC, over Tailscale)

**1. Health check** (no auth needed):
```powershell
curl http://100.71.61.39:5080/api/health
```
Should return `{"status":"ok","timeUtc":"..."}`.

**2. Create the first Owner account** (only works once, ever):
```powershell
curl -X POST http://100.71.61.39:5080/api/auth/bootstrap-owner -H "Content-Type: application/json" -d '{\"username\":\"david\",\"password\":\"choose-a-real-password-here\"}'
```

**3. Log in:**
```powershell
curl -X POST http://100.71.61.39:5080/api/auth/login -H "Content-Type: application/json" -d '{\"username\":\"david\",\"password\":\"choose-a-real-password-here\"}'
```
This returns a JSON object with a `token` field - copy that whole token
string for the next step.

**4. Call the authenticated status endpoint** (replace `YOUR_TOKEN_HERE`):
```powershell
curl http://100.71.61.39:5080/api/status -H "Authorization: Bearer YOUR_TOKEN_HERE"
```
Should return the real `systemctl` status for `valheim.service`, along
with which username the request came from - proving the whole chain
(password hash verify -> JWT issue -> JWT verify -> real local action)
works end to end.

## A note on the JWT signing key

`appsettings.json` already has a randomly-generated key filled in, so
this works out of the box - but since that key is committed to the repo
for convenience during this build-out phase, treat it as not-actually-
secret. Before this handles anything more sensitive than a status check,
generate a fresh key and keep it out of version control (an environment
variable or a file outside the repo, not `appsettings.json`).
