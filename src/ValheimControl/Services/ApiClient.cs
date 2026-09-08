using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ValheimControl.Services;

public record ApiResult(bool Success, string Output, int StatusCode);

/// <summary>
/// Talks to the new ValheimControlApi backend over HTTP instead of SSH.
/// Mirrors SshService's simple (Success, Output) result shape so windows
/// migrating from one to the other don't need to change much beyond the
/// call site itself. Holds the JWT for the current session in memory only -
/// never persisted to disk, so logging out (or just closing the app)
/// forgets it, matching how a real login should behave.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _baseUrl;

    public string? Token { get; private set; }
    public string? Username { get; private set; }
    public bool IsOwner { get; private set; }
    public string? RoleName { get; private set; }
    public string[] Permissions { get; private set; } = [];
    public bool IsLoggedIn => !string.IsNullOrEmpty(Token);

    public ApiClient(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');

        // If someone types just the bare IP (missing http:// - an easy
        // mistake, since the SSH wizard's server field right next to this
        // one really does just want a bare IP), assume http rather than
        // throwing a confusing "invalid absolute URI" exception later.
        // Doesn't fix a missing port, but turns a hard crash into a
        // request that at least gets as far as a clear connection error.
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = $"http://{trimmed}";
        }

        _baseUrl = trimmed;
    }

    public async Task<(bool Success, string? Error)> LoginAsync(string username, string password)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/auth/login", new { username, password });

            if (!response.IsSuccessStatusCode)
            {
                var error = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Incorrect username or password."
                    : $"Login failed: {(int)response.StatusCode} {response.StatusCode}";
                return (false, error);
            }

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (body is null) return (false, "Server returned an empty response.");

            Token = body.Token;
            Username = body.Username;
            IsOwner = body.IsOwner;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores a previously-saved token without re-entering credentials -
    /// but doesn't trust it as valid until ValidateSessionAsync confirms it
    /// against the server. A token alone proves nothing by itself here;
    /// it's just what gets sent on the next request to find out.
    /// </summary>
    public void RestoreToken(string token) => Token = token;

    /// <summary>
    /// Confirms a token is still genuinely valid by asking the server, and
    /// refreshes Username/IsOwner/RoleName/Permissions from that same
    /// authoritative response - never from anything cached locally. Clears
    /// the session on any failure (expired token, deleted account, etc.)
    /// so the caller can fall back to a real login prompt.
    /// </summary>
    public async Task<(bool Valid, string? Error)> ValidateSessionAsync()
    {
        if (string.IsNullOrEmpty(Token))
        {
            return (false, "No session to validate.");
        }

        var result = await GetAsync("/api/auth/whoami");
        if (!result.Success)
        {
            LogOut();
            var error = result.StatusCode == 401
                ? "Session expired or account no longer exists - please sign in again."
                : $"Could not validate session: {result.Output}";
            return (false, error);
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(result.Output);
            var root = doc.RootElement;

            Username = root.GetProperty("username").GetString();
            IsOwner = root.GetProperty("isOwner").GetBoolean();
            RoleName = root.TryGetProperty("roleName", out var roleEl) ? roleEl.GetString() : null;
            Permissions = root.TryGetProperty("permissions", out var permsEl)
                ? permsEl.EnumerateArray().Select(p => p.GetString() ?? "").ToArray()
                : [];

            return (true, null);
        }
        catch (Exception ex)
        {
            LogOut();
            return (false, $"Could not parse session info: {ex.Message}");
        }
    }

    public void LogOut()
    {
        Token = null;
        Username = null;
        IsOwner = false;
        RoleName = null;
        Permissions = [];
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var result = await PutAsync("/api/auth/change-password", new { currentPassword, newPassword });
        if (result.Success) return (true, null);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(result.Output);
            if (doc.RootElement.TryGetProperty("error", out var errEl))
            {
                return (false, errEl.GetString());
            }
        }
        catch
        {
            // fall through to generic error below
        }

        return (false, $"Failed ({result.StatusCode}): {result.Output}");
    }

    public Task<ApiResult> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null);
    public Task<ApiResult> PostAsync(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body);
    public Task<ApiResult> PutAsync(string path, object? body = null) => SendAsync(HttpMethod.Put, path, body);
    public Task<ApiResult> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null);

    private async Task<ApiResult> SendAsync(HttpMethod method, string path, object? body)
    {
        try
        {
            var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
            if (!string.IsNullOrEmpty(Token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            }
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            return new ApiResult(
                response.IsSuccessStatusCode,
                string.IsNullOrWhiteSpace(content) ? "(no output)" : content,
                (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            return new ApiResult(false, $"Request failed: {ex.Message}", -1);
        }
    }

    private record LoginResponse(string Token, string Username, bool IsOwner);
}
