using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace VezaSkills.Api.Auth;

public record SupaUser(Guid Id, string? Email);

// Validates the Supabase access token by asking Supabase Auth who it belongs to.
// Works whatever signing method the Supabase project uses. Results are cached for 5 minutes.
public class SupabaseAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Supabase";
    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;

    public SupabaseAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IHttpClientFactory http,
        IMemoryCache cache) : base(options, logger, encoder)
    {
        _http = http;
        _cache = cache;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header.Substring(7).Trim();
        if (token.Length == 0 || token.Length > 4000)
            return AuthenticateResult.Fail("Invalid token");

        var cacheKey = "tok:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        if (!_cache.TryGetValue(cacheKey, out SupaUser? user) || user == null)
        {
            var url = Environment.GetEnvironmentVariable("SUPABASE_URL")?.TrimEnd('/');
            var anon = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(anon))
            {
                Logger.LogError("SUPABASE_URL or SUPABASE_ANON_KEY is not set");
                return AuthenticateResult.Fail("Auth not configured");
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{url}/auth/v1/user");
            req.Headers.Add("apikey", anon);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.CreateClient().SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return AuthenticateResult.Fail("Invalid token");

            var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            if (!json.TryGetProperty("id", out var idProp) || !Guid.TryParse(idProp.GetString(), out var id))
                return AuthenticateResult.Fail("Invalid token");

            string? email = json.TryGetProperty("email", out var e) ? e.GetString() : null;
            user = new SupaUser(id, email);
            _cache.Set(cacheKey, user, TimeSpan.FromMinutes(5));
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()) };
        if (!string.IsNullOrEmpty(user.Email)) claims.Add(new Claim(ClaimTypes.Email, user.Email));
        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
