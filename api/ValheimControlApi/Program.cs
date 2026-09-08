using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ValheimControlApi.Data;
using ValheimControlApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=valheimcontrol.db"));

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key is not configured. Set it in appsettings.json before running - see README.md.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "ValheimControlApi";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();

var app = builder.Build();

// Auto-create the SQLite file and schema on first run - fine for this scale
// (a handful of users), no need for a full migrations pipeline yet.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseAuthentication();
app.UseAuthorization();

// ------------------------------------------------------------------
// Health check - no auth required, just confirms the service is up.
// ------------------------------------------------------------------
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timeUtc = DateTime.UtcNow }));

// ------------------------------------------------------------------
// One-time bootstrap: creates the first Owner account. Permanently
// refuses once any user already exists - there is no window where this
// could be used to create a second, unauthorized account later.
// ------------------------------------------------------------------
app.MapPost("/api/auth/bootstrap-owner", async (AppDbContext db, BootstrapRequest req) =>
{
    if (await db.Users.AnyAsync())
    {
        return Results.Conflict(new { error = "An account already exists - bootstrap can only be used once." });
    }

    if (string.IsNullOrWhiteSpace(req.Username))
    {
        return Results.BadRequest(new { error = "Username is required." });
    }
    if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
    {
        return Results.BadRequest(new { error = "Password must be at least 6 characters." });
    }

    var (hash, salt) = PasswordHasher.Hash(req.Password);
    db.Users.Add(new User
    {
        Username = req.Username.Trim(),
        PasswordHash = hash,
        PasswordSalt = salt,
        IsOwner = true
    });
    await db.SaveChangesAsync();

    return Results.Ok(new { message = $"Owner account '{req.Username}' created." });
});

// ------------------------------------------------------------------
// Login - verifies credentials, issues a JWT valid for 7 days.
// ------------------------------------------------------------------
app.MapPost("/api/auth/login", async (AppDbContext db, LoginRequest req) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
    if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash, user.PasswordSalt))
    {
        // Deliberately identical error for "no such user" and "wrong password" -
        // doesn't reveal which one, matching normal login-security practice.
        return Results.Unauthorized();
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim("isOwner", user.IsOwner.ToString())
    };

    var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        claims: claims,
        expires: DateTime.UtcNow.AddDays(7),
        signingCredentials: credentials);

    return Results.Ok(new
    {
        token = new JwtSecurityTokenHandler().WriteToken(token),
        username = user.Username,
        isOwner = user.IsOwner
    });
});

// ------------------------------------------------------------------
// Whoami - validates a saved JWT and returns fresh, authoritative
// session info via a real DB lookup (not just decoding the token's
// cached claims). Used by the WPF app both to check whether a
// remembered session is still valid on startup, and to populate the
// Account page with always-current username/role/permissions.
// ------------------------------------------------------------------
app.MapGet("/api/auth/whoami", async (AppDbContext db, ClaimsPrincipal principal) =>
{
    var user = await GetCurrentUserAsync(db, principal);
    if (user is null) return Results.Unauthorized(); // account deleted since the token was issued

    return Results.Ok(new
    {
        username = user.Username,
        isOwner = user.IsOwner,
        roleName = user.Role?.Name,
        permissions = user.IsOwner ? Permissions.All : (user.Role?.Permissions ?? [])
    });
}).RequireAuthorization();

// ------------------------------------------------------------------
// Change own password - real self-service, no Owner involvement
// needed. Requires the CURRENT password to be verified first, same as
// any normal account settings page.
// ------------------------------------------------------------------
app.MapPut("/api/auth/change-password", async (AppDbContext db, ClaimsPrincipal principal, ChangePasswordRequest req) =>
{
    var user = await GetCurrentUserAsync(db, principal);
    if (user is null) return Results.Unauthorized();

    if (!PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash, user.PasswordSalt))
    {
        return Results.BadRequest(new { error = "Current password is incorrect." });
    }

    if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
    {
        return Results.BadRequest(new { error = "New password must be at least 6 characters." });
    }

    var (hash, salt) = PasswordHasher.Hash(req.NewPassword);
    user.PasswordHash = hash;
    user.PasswordSalt = salt;
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true });
}).RequireAuthorization();

// ------------------------------------------------------------------
// Status - read-only, no elevated permission needed to query it.
// ------------------------------------------------------------------
app.MapGet("/api/status", async (ClaimsPrincipal user) =>
{
    var requestedBy = user.Identity?.Name ?? "unknown";
    var result = await ProcessRunner.RunAsync(
        "/usr/bin/systemctl", ["show", "valheim.service", "-p", "ActiveState", "-p", "SubState", "-p", "MainPID"]);

    return Results.Ok(new { requestedBy, raw = result.Output });
}).RequireAuthorization();

// ------------------------------------------------------------------
// Core server actions - the SERVER and BACKUP tiers from the main
// dashboard. Each checks the caller's actual current permission (a
// fresh DB lookup, not a stale JWT claim) - Owner always passes, a
// non-Owner needs their assigned Role to include the specific key.
// ------------------------------------------------------------------

app.MapPost("/api/server/start", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await HasPermissionAsync(db, user, Permissions.ServerStart)) return Results.Forbid();

    var result = await ProcessRunner.RunAsync("/usr/bin/systemctl", ["start", "valheim.service"]);
    return Results.Ok(new { success = result.Success, output = result.Output });
}).RequireAuthorization();

app.MapPost("/api/server/restart", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await HasPermissionAsync(db, user, Permissions.ServerRestart)) return Results.Forbid();

    var result = await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "valheim.service"]);
    return Results.Ok(new { success = result.Success, output = result.Output });
}).RequireAuthorization();

app.MapPost("/api/server/stop", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await HasPermissionAsync(db, user, Permissions.ServerStop)) return Results.Forbid();

    var result = await ProcessRunner.RunAsync("/usr/bin/systemctl", ["stop", "valheim.service"]);
    return Results.Ok(new { success = result.Success, output = result.Output });
}).RequireAuthorization();

app.MapPost("/api/backup/run", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await HasPermissionAsync(db, user, Permissions.BackupRun)) return Results.Forbid();

    var result = await ProcessRunner.RunAsync("/opt/valheim/backup.sh", timeoutSeconds: 60);
    return Results.Ok(new { success = result.Success, output = result.Output });
}).RequireAuthorization();

app.MapPost("/api/server/backup-and-restart", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await HasPermissionAsync(db, user, Permissions.BackupAndRestart)) return Results.Forbid();

    var backupResult = await ProcessRunner.RunAsync("/opt/valheim/backup.sh", timeoutSeconds: 60);
    if (!backupResult.Success)
    {
        return Results.Ok(new { success = false, stage = "backup", output = backupResult.Output });
    }

    var restartResult = await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "valheim.service"]);
    return Results.Ok(new { success = restartResult.Success, stage = "restart", output = restartResult.Output });
}).RequireAuthorization();

app.MapPost("/api/server/backup-and-reboot", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await HasPermissionAsync(db, user, Permissions.BackupAndReboot)) return Results.Forbid();

    var stopResult = await ProcessRunner.RunAsync("/usr/bin/systemctl", ["stop", "valheim.service"]);
    if (!stopResult.Success)
    {
        return Results.Ok(new { success = false, stage = "stop", output = stopResult.Output });
    }

    var backupResult = await ProcessRunner.RunAsync("/opt/valheim/backup.sh", timeoutSeconds: 60);
    if (!backupResult.Success)
    {
        return Results.Ok(new { success = false, stage = "backup", output = backupResult.Output });
    }

    // Fire-and-forget: the reboot kills this very process mid-flight, so we
    // don't await it - just let the response above go out first, then let
    // the reboot happen. Matches how the SSH-based version already treats
    // "connection drops here" as expected, not an error.
    _ = ProcessRunner.RunAsync("/usr/bin/systemctl", ["reboot"], timeoutSeconds: 5);

    return Results.Ok(new { success = true, stage = "rebooting" });
}).RequireAuthorization();

// ------------------------------------------------------------------
// Dashboard stats - Logs, World/Uptime, recent player joins. All
// read-only. Where the old SSH version had to shell out to tr/grep/tail
// to process text, this native version mostly does that filtering in C#
// instead - simpler and fewer moving parts than piping between commands.
// ------------------------------------------------------------------

app.MapGet("/api/logs", async (int? lines) =>
{
    var count = (lines is > 0 and <= 500) ? lines.Value : 50;
    var result = await ProcessRunner.RunAsync(
        "/usr/bin/journalctl", ["-u", "valheim.service", "-n", count.ToString(), "--no-pager"]);

    return Results.Ok(new { success = result.Success, output = result.Output });
}).RequireAuthorization();

app.MapGet("/api/world-info", async () =>
{
    var uptimeSeconds = await GetUptimeSecondsAsync();
    var world = await GetWorldNameAsync();
    return Results.Ok(new { world, uptimeSeconds });
}).RequireAuthorization();

app.MapGet("/api/players/recent", async (int? lines) =>
{
    var count = (lines is > 0 and <= 100) ? lines.Value : 20;

    var sinceResult = await ProcessRunner.RunAsync(
        "/usr/bin/systemctl", ["show", "valheim.service", "-p", "ActiveEnterTimestamp", "--value"]);
    var since = sinceResult.Output.Trim();

    if (string.IsNullOrWhiteSpace(since) || since is "n/a" or "(no output)")
    {
        return Results.Ok(new { players = Array.Empty<object>() });
    }

    var logResult = await ProcessRunner.RunAsync(
        "/usr/bin/journalctl", ["-u", "valheim.service", "-o", "short-iso", "--since", since, "--no-pager"]);

    var players = ParsePlayerJoins(logResult.Output).TakeLast(count).ToArray();
    return Results.Ok(new { players });
}).RequireAuthorization();

// ------------------------------------------------------------------
// World management - list, swap/create (mechanically identical - both
// just point -world at a name; whether that name already has save files
// determines whether Valheim treats it as a swap or a fresh creation),
// and delete.
//
// Delete is Owner-only, using real per-user identity now that we have
// it - a cleaner replacement for the old SSH-based app's shared "delete
// password", which only existed because that architecture had no way to
// tell which person was acting. World modifiers (-preset/-modifier/
// -setkey) from the WPF app's Create World popup aren't ported here yet -
// keeping this pass to the core swap/create/delete mechanics first.
// ------------------------------------------------------------------

app.MapGet("/api/worlds", () => Results.Ok(new { worlds = GetWorldList() })).RequireAuthorization();

app.MapPost("/api/worlds/swap", async (AppDbContext db, ClaimsPrincipal user, SwapWorldRequest req) =>
{
    if (!await HasPermissionAsync(db, user, Permissions.WorldsSwap)) return Results.Forbid();

    var newWorld = req.World?.Trim();
    if (string.IsNullOrWhiteSpace(newWorld))
    {
        return Results.BadRequest(new { error = "World name is required." });
    }

    var isNewWorld = !GetWorldList().Contains(newWorld, StringComparer.OrdinalIgnoreCase);
    var (success, error, previousWorld) = await SetConfiguredWorldAsync(newWorld);

    return success
        ? Results.Ok(new { success = true, previousWorld, newWorld, isNewWorld })
        : Results.BadRequest(new { error });
}).RequireAuthorization();

app.MapDelete("/api/worlds/{name}", async (string name, AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await HasPermissionAsync(db, user, Permissions.WorldsDelete)) return Results.Forbid();

    if (name.Contains("_backup_auto-", StringComparison.Ordinal))
    {
        return Results.BadRequest(new { error = "Refusing to delete a backup snapshot, not a world." });
    }

    var currentWorld = await GetConfiguredWorldAsync();
    if (string.Equals(name, currentWorld, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            error = $"Refusing to delete the currently configured world ({currentWorld}). " +
                     "Swap to a different world first, save, and restart before deleting this one."
        });
    }

    const string saveDir = "/var/lib/valheim/worlds_local";
    var fwlPath = Path.Combine(saveDir, $"{name}.fwl");
    if (!File.Exists(fwlPath))
    {
        return Results.NotFound(new { error = $"World not found: {name}" });
    }

    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    var archiveDir = $"/opt/valheim/deleted_worlds/{name}-{timestamp}";
    Directory.CreateDirectory(archiveDir);

    var matchingFiles = Directory.GetFiles(saveDir)
        .Where(f => Path.GetFileName(f).StartsWith($"{name}.", StringComparison.Ordinal));

    foreach (var file in matchingFiles)
    {
        File.Move(file, Path.Combine(archiveDir, Path.GetFileName(file)));
    }

    return Results.Ok(new { success = true, archivedTo = archiveDir });
}).RequireAuthorization();

// ------------------------------------------------------------------
// Config file editing (Runestones) - start_server.sh and the 3
// player-list files. Simpler than the old SSH version: direct file I/O
// instead of base64-encoding content to survive a remote shell command.
// Same whitelist as before - only these 4 exact filenames are ever
// readable/writable through this endpoint, nothing else on the box.
// ------------------------------------------------------------------

var configFilePaths = new Dictionary<string, string>
{
    ["start_server.sh"] = "/opt/valheim/start_server.sh",
    ["adminlist.txt"] = "/var/lib/valheim/adminlist.txt",
    ["permittedlist.txt"] = "/var/lib/valheim/permittedlist.txt",
    ["bannedlist.txt"] = "/var/lib/valheim/bannedlist.txt",
};

app.MapGet("/api/config", () => Results.Ok(new { files = configFilePaths.Keys.ToArray() }))
    .RequireAuthorization();

app.MapGet("/api/config/{filename}", async (string filename) =>
{
    if (!configFilePaths.TryGetValue(filename, out var path))
    {
        return Results.BadRequest(new { error = $"'{filename}' is not an allowed config file." });
    }

    var content = File.Exists(path) ? await File.ReadAllTextAsync(path) : "";
    return Results.Ok(new { filename, content });
}).RequireAuthorization();

app.MapPut("/api/config/{filename}", async (string filename, AppDbContext db, ClaimsPrincipal user, ConfigWriteRequest req) =>
{
    if (!await HasPermissionAsync(db, user, Permissions.ConfigWrite)) return Results.Forbid();

    if (!configFilePaths.TryGetValue(filename, out var path))
    {
        return Results.BadRequest(new { error = $"'{filename}' is not an allowed config file." });
    }

    // Always back up the previous version first - same safety net as
    // every other write operation in this project.
    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    var backupDir = "/opt/valheim/config_backups";
    Directory.CreateDirectory(backupDir);
    if (File.Exists(path))
    {
        File.Copy(path, Path.Combine(backupDir, $"{filename}.{timestamp}.bak"));
    }

    var tempPath = path + ".tmp";
    await File.WriteAllTextAsync(tempPath, req.Content ?? "");
    File.Move(tempPath, path, overwrite: true); // atomic - never leaves a half-written file

    if (filename == "start_server.sh")
    {
        await ProcessRunner.RunAsync("/usr/bin/chmod", ["+x", path]);
    }

    // The actual Valheim process runs as the 'valheim' user - keep these
    // files owned by it, matching how the original setup already worked.
    await ProcessRunner.RunAsync("/usr/bin/chown", ["valheim:valheim", path]);

    return Results.Ok(new { success = true });
}).RequireAuthorization();

// ------------------------------------------------------------------
// Updates - checks installed build against Steam's latest public build,
// and runs the update. Unlike the old SSH-based app (where the Windows
// client called Steam's API directly, separate from the server-side
// installed-build check), this whole check now lives server-side in one
// place - the server already has internet access anyway (it's how
// SteamCMD downloads updates in the first place).
// ------------------------------------------------------------------

app.MapGet("/api/update/check", async (IHttpClientFactory httpClientFactory) =>
{
    const string manifestPath = "/opt/valheim/server/steamapps/appmanifest_896660.acf";
    string? installedBuild = null;

    if (File.Exists(manifestPath))
    {
        var manifestContent = await File.ReadAllTextAsync(manifestPath);
        var match = System.Text.RegularExpressions.Regex.Match(manifestContent, @"""buildid""\s+""(\d+)""");
        if (match.Success) installedBuild = match.Groups[1].Value;
    }

    string? latestBuild = null;
    try
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var json = await client.GetStringAsync("https://api.steamcmd.net/v1/info/896660");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        latestBuild = doc.RootElement
            .GetProperty("data").GetProperty("896660")
            .GetProperty("depots").GetProperty("branches").GetProperty("public")
            .GetProperty("buildid").GetString();
    }
    catch
    {
        // Fails soft - "unable to check" is fine here, a wrong answer isn't.
        // Not an official Valve API, so treating it as best-effort is correct.
    }

    var updateAvailable = installedBuild is not null && latestBuild is not null && installedBuild != latestBuild;
    return Results.Ok(new { installedBuild, latestBuild, updateAvailable });
}).RequireAuthorization();

// Runs the existing, more complete update.sh (which already handles
// backup -> stop -> SteamCMD -> start as one atomic script) rather than
// reimplementing that sequence here.
app.MapPost("/api/update/run", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await HasPermissionAsync(db, user, Permissions.UpdateRun)) return Results.Forbid();

    var result = await ProcessRunner.RunAsync("/opt/valheim/update.sh", timeoutSeconds: 300);
    return Results.Ok(new { success = result.Success, output = result.Output });
}).RequireAuthorization();

// ------------------------------------------------------------------
// Backup settings - retention windows (how long recent/daily backups
// are kept) and how often the recent tier runs. Completes the same set
// of controls the WPF app's Settings window already has.
// ------------------------------------------------------------------

app.MapGet("/api/backup/settings", async () =>
{
    const string backupScriptPath = "/opt/valheim/backup.sh";
    const string timerPath = "/etc/systemd/system/valheim-backup.timer";

    int? recentMinutes = null;
    int? dailyDays = null;
    string? interval = null;

    if (File.Exists(backupScriptPath))
    {
        var content = await File.ReadAllTextAsync(backupScriptPath);

        var mminMatch = System.Text.RegularExpressions.Regex.Match(content, @"-mmin\s+\+(\d+)");
        if (mminMatch.Success) recentMinutes = int.Parse(mminMatch.Groups[1].Value);

        var mtimeMatch = System.Text.RegularExpressions.Regex.Match(content, @"-mtime\s+\+(\d+)");
        if (mtimeMatch.Success) dailyDays = int.Parse(mtimeMatch.Groups[1].Value);
    }

    if (File.Exists(timerPath))
    {
        var content = await File.ReadAllTextAsync(timerPath);
        var intervalMatch = System.Text.RegularExpressions.Regex.Match(content, @"OnUnitActiveSec=(\S+)");
        if (intervalMatch.Success) interval = intervalMatch.Groups[1].Value;
    }

    return Results.Ok(new { recentRetentionMinutes = recentMinutes, dailyRetentionDays = dailyDays, interval });
}).RequireAuthorization();

// ------------------------------------------------------------------
// Last backup timestamp - reads real file modification times directly
// (no shell-out needed, since this runs natively on the box).
// ------------------------------------------------------------------
app.MapGet("/api/backup/last", () =>
{
    const string backupDir = "/opt/valheim/backups/recent";
    if (!Directory.Exists(backupDir))
    {
        return Results.Ok(new { lastBackupUtc = (DateTime?)null });
    }

    var newest = Directory.GetFiles(backupDir, "valheim_*.tar.gz")
        .Select(f => new FileInfo(f))
        .OrderByDescending(f => f.LastWriteTimeUtc)
        .FirstOrDefault();

    return Results.Ok(new { lastBackupUtc = newest?.LastWriteTimeUtc });
}).RequireAuthorization();

app.MapPut("/api/backup/settings", async (AppDbContext db, ClaimsPrincipal user, BackupSettingsRequest req) =>
{
    if (!await HasPermissionAsync(db, user, Permissions.BackupSettingsWrite)) return Results.Forbid();

    if (req.RecentRetentionMinutes <= 0 || req.DailyRetentionDays <= 0)
    {
        return Results.BadRequest(new { error = "Retention values must be positive whole numbers." });
    }

    // Same fixed whitelist the old set_backup_interval.sh enforced - never
    // accepts an arbitrary string into a systemd unit file.
    string[] allowedIntervals = ["5min", "10min", "15min", "30min", "1h", "2h", "6h"];
    if (!allowedIntervals.Contains(req.Interval))
    {
        return Results.BadRequest(new { error = $"Interval must be one of: {string.Join(", ", allowedIntervals)}" });
    }

    const string backupScriptPath = "/opt/valheim/backup.sh";
    const string timerPath = "/etc/systemd/system/valheim-backup.timer";
    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    var backupDir = "/opt/valheim/config_backups";
    Directory.CreateDirectory(backupDir);

    if (File.Exists(backupScriptPath))
    {
        File.Copy(backupScriptPath, Path.Combine(backupDir, $"backup.sh.{timestamp}.bak"));

        var content = await File.ReadAllTextAsync(backupScriptPath);
        content = System.Text.RegularExpressions.Regex.Replace(
            content, @"-mmin\s+\+\d+", $"-mmin +{req.RecentRetentionMinutes}");
        content = System.Text.RegularExpressions.Regex.Replace(
            content, @"-mtime\s+\+\d+", $"-mtime +{req.DailyRetentionDays}");

        var tempPath = backupScriptPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, content);
        File.Move(tempPath, backupScriptPath, overwrite: true);
    }

    if (File.Exists(timerPath))
    {
        File.Copy(timerPath, Path.Combine(backupDir, $"valheim-backup.timer.{timestamp}.bak"));

        var content = await File.ReadAllTextAsync(timerPath);
        content = System.Text.RegularExpressions.Regex.Replace(
            content, @"OnUnitActiveSec=\S+", $"OnUnitActiveSec={req.Interval}");
        content = System.Text.RegularExpressions.Regex.Replace(
            content, @"Description=.*", $"Description=Run Valheim backup every {req.Interval}");

        var tempPath = timerPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, content);
        File.Move(tempPath, timerPath, overwrite: true);

        // Interval changes need systemd to actually pick up the new timer
        // file - same two commands the old set_backup_interval.sh ran.
        await ProcessRunner.RunAsync("/usr/bin/systemctl", ["daemon-reload"]);
        await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "valheim-backup.timer"]);
    }

    return Results.Ok(new { success = true });
}).RequireAuthorization();

// ------------------------------------------------------------------
// Phase 3a: Roles & Users management - all Owner-only. This is the
// administrative foundation (create roles, create users, assign roles).
// The existing action endpoints above still only check IsOwner for their
// few gated actions - wiring real per-permission checks into all of them
// is Phase 3b, a deliberate follow-up rather than bundled in here.
// ------------------------------------------------------------------

app.MapGet("/api/permissions", () => Results.Ok(new { permissions = Permissions.All }))
    .RequireAuthorization();

app.MapGet("/api/roles", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await IsOwnerAsync(db, user)) return Results.Forbid();

    var roles = await db.Roles.ToListAsync();
    return Results.Ok(new { roles = roles.Select(r => new { r.Id, r.Name, r.Permissions }) });
}).RequireAuthorization();

app.MapPost("/api/roles", async (AppDbContext db, ClaimsPrincipal user, RoleRequest req) =>
{
    if (!await IsOwnerAsync(db, user)) return Results.Forbid();

    if (string.IsNullOrWhiteSpace(req.Name))
    {
        return Results.BadRequest(new { error = "Role name is required." });
    }

    var requestedPermissions = req.Permissions ?? [];
    var invalid = requestedPermissions.Where(p => !Permissions.All.Contains(p)).ToArray();
    if (invalid.Length > 0)
    {
        return Results.BadRequest(new { error = $"Unknown permission(s): {string.Join(", ", invalid)}" });
    }

    var role = new Role { Name = req.Name.Trim() };
    role.Permissions = requestedPermissions;
    db.Roles.Add(role);
    await db.SaveChangesAsync();

    return Results.Ok(new { role.Id, role.Name, role.Permissions });
}).RequireAuthorization();

app.MapPut("/api/roles/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user, RoleRequest req) =>
{
    if (!await IsOwnerAsync(db, user)) return Results.Forbid();

    var role = await db.Roles.FindAsync(id);
    if (role is null) return Results.NotFound();

    var requestedPermissions = req.Permissions ?? role.Permissions;
    var invalid = requestedPermissions.Where(p => !Permissions.All.Contains(p)).ToArray();
    if (invalid.Length > 0)
    {
        return Results.BadRequest(new { error = $"Unknown permission(s): {string.Join(", ", invalid)}" });
    }

    if (!string.IsNullOrWhiteSpace(req.Name)) role.Name = req.Name.Trim();
    role.Permissions = requestedPermissions;
    await db.SaveChangesAsync();

    return Results.Ok(new { role.Id, role.Name, role.Permissions });
}).RequireAuthorization();

app.MapDelete("/api/roles/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await IsOwnerAsync(db, user)) return Results.Forbid();

    var role = await db.Roles.FindAsync(id);
    if (role is null) return Results.NotFound();

    // Anyone assigned to this role loses it - they keep only whatever's
    // universally allowed until an Owner assigns them a new one.
    var affectedUsers = await db.Users.Where(u => u.RoleId == id).ToListAsync();
    foreach (var affectedUser in affectedUsers) affectedUser.RoleId = null;

    db.Roles.Remove(role);
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true, usersUnassigned = affectedUsers.Count });
}).RequireAuthorization();

app.MapGet("/api/users", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await IsOwnerAsync(db, user)) return Results.Forbid();

    var users = await db.Users
        .Select(u => new { u.Id, u.Username, u.IsOwner, u.RoleId, RoleName = u.Role != null ? u.Role.Name : null })
        .ToListAsync();

    return Results.Ok(new { users });
}).RequireAuthorization();

app.MapPost("/api/users", async (AppDbContext db, ClaimsPrincipal user, CreateUserRequest req) =>
{
    if (!await IsOwnerAsync(db, user)) return Results.Forbid();

    if (string.IsNullOrWhiteSpace(req.Username))
    {
        return Results.BadRequest(new { error = "Username is required." });
    }
    if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
    {
        return Results.BadRequest(new { error = "Password must be at least 6 characters." });
    }

    if (await db.Users.AnyAsync(u => u.Username == req.Username))
    {
        return Results.Conflict(new { error = "A user with that username already exists." });
    }

    if (req.RoleId is not null && !await db.Roles.AnyAsync(r => r.Id == req.RoleId))
    {
        return Results.BadRequest(new { error = "That role doesn't exist." });
    }

    var (hash, salt) = PasswordHasher.Hash(req.Password);
    var newUser = new User
    {
        Username = req.Username.Trim(),
        PasswordHash = hash,
        PasswordSalt = salt,
        IsOwner = false, // only /api/auth/bootstrap-owner can ever create an Owner
        RoleId = req.RoleId
    };
    db.Users.Add(newUser);
    await db.SaveChangesAsync();

    return Results.Ok(new { newUser.Id, newUser.Username, newUser.RoleId });
}).RequireAuthorization();

app.MapPut("/api/users/{id:int}/role", async (int id, AppDbContext db, ClaimsPrincipal user, UpdateUserRoleRequest req) =>
{
    if (!await IsOwnerAsync(db, user)) return Results.Forbid();

    var targetUser = await db.Users.FindAsync(id);
    if (targetUser is null) return Results.NotFound();
    if (targetUser.IsOwner)
    {
        return Results.BadRequest(new { error = "Can't change the Owner's role - Owner always has full access." });
    }

    if (req.RoleId is not null && !await db.Roles.AnyAsync(r => r.Id == req.RoleId))
    {
        return Results.BadRequest(new { error = "That role doesn't exist." });
    }

    targetUser.RoleId = req.RoleId;
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true });
}).RequireAuthorization();

app.MapDelete("/api/users/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
{
    if (!await IsOwnerAsync(db, user)) return Results.Forbid();

    var targetUser = await db.Users.FindAsync(id);
    if (targetUser is null) return Results.NotFound();
    if (targetUser.IsOwner)
    {
        return Results.BadRequest(new { error = "Can't delete the Owner account." });
    }

    db.Users.Remove(targetUser);
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true });
}).RequireAuthorization();

app.Run();

// ------------------------------------------------------------------
// Local helper functions for the dashboard stats endpoints above.
// ------------------------------------------------------------------

async Task<long?> GetUptimeSecondsAsync()
{
    var tsResult = await ProcessRunner.RunAsync(
        "/usr/bin/systemctl", ["show", "valheim.service", "-p", "ActiveEnterTimestamp", "--value"]);
    var ts = tsResult.Output.Trim();
    if (string.IsNullOrWhiteSpace(ts) || ts is "n/a" or "(no output)") return null;

    var epochResult = await ProcessRunner.RunAsync("/usr/bin/date", ["-d", ts, "+%s"]);
    if (!epochResult.Success || !long.TryParse(epochResult.Output.Trim(), out var startEpoch)) return null;

    return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - startEpoch;
}

async Task<string?> GetWorldNameAsync()
{
    var pidResult = await ProcessRunner.RunAsync(
        "/usr/bin/systemctl", ["show", "valheim.service", "-p", "MainPID", "--value"]);
    var pid = pidResult.Output.Trim();
    if (string.IsNullOrWhiteSpace(pid) || pid is "0" or "(no output)") return null;

    var argv = TryReadCmdline(pid);

    // MainPID might be a wrapper shell script rather than the game binary
    // itself (if the launch script doesn't use `exec`) - check its direct
    // children, then fall back to a system-wide search by process name.
    if (argv is null || !argv.Contains("valheim_server"))
    {
        var childPidResult = await ProcessRunner.RunAsync("/usr/bin/pgrep", ["-P", pid, "-f", "valheim_server"]);
        var childPid = childPidResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (childPid is null)
        {
            var globalPidResult = await ProcessRunner.RunAsync("/usr/bin/pgrep", ["-f", "valheim_server.x86_64"]);
            childPid = globalPidResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        if (childPid is not null) argv = TryReadCmdline(childPid);
    }

    if (argv is null) return null;

    var worldIndex = Array.IndexOf(argv, "-world");
    return (worldIndex >= 0 && worldIndex + 1 < argv.Length) ? argv[worldIndex + 1] : null;
}

string[]? TryReadCmdline(string pid)
{
    try
    {
        var raw = File.ReadAllText($"/proc/{pid}/cmdline");
        return raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
    catch
    {
        return null; // process may have exited between checks, or isn't readable
    }
}

List<PlayerJoin> ParsePlayerJoins(string journalOutput)
{
    var results = new List<PlayerJoin>();
    if (string.IsNullOrWhiteSpace(journalOutput) || journalOutput == "(no output)") return results;

    foreach (var line in journalOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        var match = System.Text.RegularExpressions.Regex.Match(line, @"Got character ZDOID from (.+?)\s*:");
        if (!match.Success) continue;

        var name = match.Groups[1].Value.Trim();
        var tsToken = line.Split(' ', 2)[0];

        var joinedAtUtc = DateTimeOffset.TryParse(tsToken, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed.UtcDateTime
            : DateTime.UtcNow;

        results.Add(new PlayerJoin(name, joinedAtUtc));
    }

    return results;
}

/// <summary>
/// Real world save names on disk. Valheim's own automatic backup
/// snapshots (named "&lt;world&gt;_backup_auto-&lt;timestamp&gt;") live in the
/// same folder as real worlds - filtered out here since they're backups,
/// not selectable worlds.
/// </summary>
string[] GetWorldList()
{
    const string saveDir = "/var/lib/valheim/worlds_local";
    if (!Directory.Exists(saveDir)) return [];

    return Directory.GetFiles(saveDir, "*.fwl")
        .Select(Path.GetFileNameWithoutExtension)
        .Where(name => name is not null && !name.Contains("_backup_auto-", StringComparison.Ordinal))
        .Select(name => name!)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

/// <summary>
/// The world currently configured in start_server.sh's -world argument -
/// distinct from GetWorldNameAsync() above, which inspects the actually
/// RUNNING process. These can differ if a swap was saved but the server
/// hasn't been restarted yet - this one is what delete-protection and the
/// swap endpoint's "previousWorld" need: the configured value, not the
/// live one.
/// </summary>
async Task<string?> GetConfiguredWorldAsync()
{
    const string scriptPath = "/opt/valheim/start_server.sh";
    if (!File.Exists(scriptPath)) return null;

    var content = await File.ReadAllTextAsync(scriptPath);
    var match = System.Text.RegularExpressions.Regex.Match(content, @"-world\s+""([^""]*)""");
    return match.Success ? match.Groups[1].Value : null;
}

/// <summary>
/// Updates start_server.sh's -world argument in place, backing up the
/// previous version first (same safety pattern used throughout this
/// project). Only touches that one argument - everything else in the
/// script is left byte-for-byte untouched.
/// </summary>
async Task<(bool Success, string? Error, string? PreviousWorld)> SetConfiguredWorldAsync(string newWorld)
{
    const string scriptPath = "/opt/valheim/start_server.sh";
    if (!File.Exists(scriptPath))
    {
        return (false, "start_server.sh not found.", null);
    }

    var content = await File.ReadAllTextAsync(scriptPath);
    var pattern = @"-world\s+""([^""]*)""";
    var match = System.Text.RegularExpressions.Regex.Match(content, pattern);

    if (!match.Success)
    {
        return (false, "Could not find a -world argument in start_server.sh.", null);
    }

    var previousWorld = match.Groups[1].Value;

    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    var backupDir = "/opt/valheim/config_backups";
    Directory.CreateDirectory(backupDir);
    File.Copy(scriptPath, Path.Combine(backupDir, $"start_server.sh.{timestamp}.bak"));

    var safeWorld = newWorld.Replace("\"", ""); // strip quotes - would break the shell string otherwise
    var updatedContent = System.Text.RegularExpressions.Regex.Replace(content, pattern, $"-world \"{safeWorld}\"");

    var tempPath = scriptPath + ".tmp";
    await File.WriteAllTextAsync(tempPath, updatedContent);
    File.Move(tempPath, scriptPath, overwrite: true); // atomic swap - never leaves a half-written script

    return (true, null, previousWorld);
}

/// <summary>
/// Fresh per-request DB lookup - deliberately NOT based on claims baked
/// into the JWT at login time. A JWT is valid for 7 days; if Owner
/// revokes or changes someone's role, that needs to take effect on their
/// very next request, not whenever their token happens to expire.
/// </summary>
async Task<bool> IsOwnerAsync(AppDbContext db, ClaimsPrincipal principal)
{
    var user = await GetCurrentUserAsync(db, principal);
    return user?.IsOwner == true;
}

async Task<bool> HasPermissionAsync(AppDbContext db, ClaimsPrincipal principal, string permission)
{
    var user = await GetCurrentUserAsync(db, principal);
    if (user is null) return false; // account was deleted since the token was issued

    if (user.IsOwner) return true; // Owner always has everything, regardless of role
    return user.Role?.Permissions.Contains(permission) == true;
}

async Task<User?> GetCurrentUserAsync(AppDbContext db, ClaimsPrincipal principal)
{
    var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (idClaim is null || !int.TryParse(idClaim, out var userId)) return null;

    return await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId);
}

record BootstrapRequest(string Username, string Password);
record LoginRequest(string Username, string Password);
record PlayerJoin(string Name, DateTime JoinedAtUtc);
record SwapWorldRequest(string World);
record ConfigWriteRequest(string? Content);
record BackupSettingsRequest(int RecentRetentionMinutes, int DailyRetentionDays, string Interval);
record ChangePasswordRequest(string CurrentPassword, string NewPassword);
record RoleRequest(string? Name, string[]? Permissions);
record CreateUserRequest(string Username, string Password, int? RoleId);
record UpdateUserRoleRequest(int? RoleId);
