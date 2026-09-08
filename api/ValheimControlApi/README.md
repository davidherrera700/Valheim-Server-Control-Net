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

## Rotating the JWT key

The signing key used to be committed directly in `appsettings.json` for
convenience during early development - that's no longer the case, and
should never be again, especially now that this repo may be public. The
key now lives **only** on the server, in a file that's never tracked by
git, referenced by the systemd service via `EnvironmentFile=`.

**One-time setup on the server:**

```bash
sudo nano /etc/valheim-control-api.env
```

Add a single line (generate your own random value - don't reuse any key
that has ever appeared in git history, since that one should be treated
as permanently compromised):

```
Jwt__Key=<a long random string - 48+ bytes, base64-encoded is fine>
```

Lock down the file so only root can read it:

```bash
sudo chmod 600 /etc/valheim-control-api.env
sudo chown root:root /etc/valheim-control-api.env
```

Then restart the service to pick it up:

```bash
sudo systemctl daemon-reload
sudo systemctl restart valheim-control-api
```

**Important:** rotating the key immediately invalidates every JWT issued
under the old one - everyone currently signed in (including yourself)
will need to log in again on their next request. This is expected and
correct; it's exactly what should happen after a key rotation.

If the service fails to start after this change, check that
`/etc/valheim-control-api.env` exists and is readable - `Program.cs`
deliberately refuses to start (`InvalidOperationException`) rather than
run with no signing key at all, instead of silently falling back to
something insecure.
