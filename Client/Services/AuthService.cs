using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Client.Services;

public sealed record AuthSession(string Email, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Logs the user in against the chat API and keeps the tokens for this browser tab.
/// Scoped service: in Blazor Server one instance exists per user circuit (browser tab).
/// </summary>
public sealed class AuthService(
    IHttpClientFactory httpClientFactory,
    ProtectedSessionStorage sessionStorage,
    TimeProvider timeProvider,
    ILogger<AuthService> logger)
{
    public const string HttpClientName = "ChatApi";
    private const string StorageKey = "chat-auth";

    // Refresh a bit before the token expires, so a request never goes out with an expired token
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private AuthSession? _session;
    private bool _loaded;

    public string? Email => _session?.Email;
    public bool IsLoggedIn => _session is not null;

    public event Action? Changed;

    /// <summary>
    /// Restores the session after a page reload. Browser storage is encrypted with ASP.NET Core
    /// Data Protection, so the tokens can't be read or modified by scripts in the page.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            var stored = await sessionStorage.GetAsync<AuthSession>(StorageKey);
            _session = stored.Success ? stored.Value : null;
        }
        catch (CryptographicException)
        {
            // Stored with an old key (e.g. after a server restart without persisted keys)
            await sessionStorage.DeleteAsync(StorageKey);
        }
    }

    /// <returns>An error message, or null on success.</returns>
    public async Task<string?> LoginAsync(string email, string password)
    {
        var response = await Http.PostAsJsonAsync("/api/auth/login", new { email, password });
        if (!response.IsSuccessStatusCode)
        {
            return "Invalid email or password.";
        }

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        await SaveSessionAsync(email, tokens!);
        return null;
    }

    /// <returns>An error message, or null on success.</returns>
    public async Task<string?> RegisterAsync(string email, string password)
    {
        var response = await Http.PostAsJsonAsync("/api/auth/register", new { email, password });
        if (!response.IsSuccessStatusCode)
        {
            return await ReadErrorsAsync(response);
        }

        return await LoginAsync(email, password);
    }

    /// <summary>
    /// Returns a valid access token, refreshing it first if it is about to expire.
    /// Used by the SignalR connection, which asks for a token on every (re)connect.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        var session = _session;
        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAt - RefreshMargin > timeProvider.GetUtcNow())
        {
            return session.AccessToken;
        }

        // Several callers may notice the expiry at the same time; only one should refresh
        await _refreshLock.WaitAsync();
        try
        {
            if (_session is null)
            {
                return null;
            }

            if (_session.ExpiresAt - RefreshMargin > timeProvider.GetUtcNow())
            {
                return _session.AccessToken;
            }

            var response = await Http.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = _session.RefreshToken });
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Token refresh failed with {StatusCode}, logging out", response.StatusCode);
                await LogoutAsync();
                return null;
            }

            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
            await SaveSessionAsync(_session.Email, tokens!);
            return _session.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task LogoutAsync()
    {
        _session = null;
        await sessionStorage.DeleteAsync(StorageKey);
        Changed?.Invoke();
    }

    private HttpClient Http => httpClientFactory.CreateClient(HttpClientName);

    private async Task SaveSessionAsync(string email, TokenResponse tokens)
    {
        _session = new AuthSession(
            email,
            tokens.AccessToken,
            tokens.RefreshToken,
            timeProvider.GetUtcNow().AddSeconds(tokens.ExpiresIn));

        await sessionStorage.SetAsync(StorageKey, _session);
        Changed?.Invoke();
    }

    private static async Task<string> ReadErrorsAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
            var errors = problem?.Errors?.Values.SelectMany(e => e).ToList();
            if (errors is { Count: > 0 })
            {
                return string.Join(" ", errors);
            }
        }
        catch (JsonException)
        {
            // Not a ValidationProblem response
        }

        return "Registration failed. Please try again.";
    }

    private sealed record TokenResponse(string AccessToken, string RefreshToken, long ExpiresIn);

    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);
}
