using System.Net.Http;
using System.Text.Json;

namespace ValheimControl.Services;

/// <summary>
/// Checks Steam directly (from this Windows PC, not via SSH) for the latest
/// publicly available build of the Valheim dedicated server, so it can be
/// compared against whatever build number GetInstalledBuildIdAsync() reports
/// from the server.
///
/// Uses the community-run api.steamcmd.net - not an official Valve API, but
/// widely used for exactly this purpose. If it's ever unreachable or changes
/// shape, this fails soft (returns null) rather than blocking anything -
/// "can't check right now" is fine, a wrong answer isn't.
/// </summary>
public class SteamUpdateService
{
    private const string ValheimDedicatedServerAppId = "896660";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<string?> GetLatestPublicBuildIdAsync()
    {
        try
        {
            var json = await Http.GetStringAsync($"https://api.steamcmd.net/v1/info/{ValheimDedicatedServerAppId}");
            using var doc = JsonDocument.Parse(json);

            return doc.RootElement
                .GetProperty("data")
                .GetProperty(ValheimDedicatedServerAppId)
                .GetProperty("depots")
                .GetProperty("branches")
                .GetProperty("public")
                .GetProperty("buildid")
                .GetString();
        }
        catch
        {
            // Network hiccup, API shape changed, offline, etc. - the caller
            // should show "Unable to check" rather than treat this as "up to date".
            return null;
        }
    }
}
