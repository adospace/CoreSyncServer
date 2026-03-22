using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using CoreSyncServer.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CoreSyncServer.Services.Implementation;

public class SyncEndpointAuthService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    ILogger<SyncEndpointAuthService> logger) : ISyncEndpointAuthService
{
    private static readonly TimeSpan JwksCacheDuration = TimeSpan.FromHours(1);

    public async Task<SyncAuthResult> AuthenticateAsync(
        Data.Endpoint endpoint,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (endpoint.Authentication is null)
            return SyncAuthResult.NoAuthRequired();

        return endpoint.Authentication switch
        {
            BasicAuthentication basic => AuthenticateBasic(basic, httpContext),
            ApiKeyAuthentication apiKey => AuthenticateApiKey(apiKey, httpContext),
            JwtAuthentication jwt => await AuthenticateJwtAsync(jwt, httpContext, cancellationToken),
            _ => SyncAuthResult.Fail("Unsupported authentication type.")
        };
    }

    private static SyncAuthResult AuthenticateBasic(BasicAuthentication config, HttpContext httpContext)
    {
        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) ||
            !AuthenticationHeaderValue.TryParse(authHeader, out var parsed) ||
            !string.Equals(parsed.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(parsed.Parameter))
        {
            return SyncAuthResult.Fail("Missing or invalid Basic authorization header.");
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parsed.Parameter));
        }
        catch (FormatException)
        {
            return SyncAuthResult.Fail("Invalid Base64 in Basic authorization header.");
        }

        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex < 0)
            return SyncAuthResult.Fail("Invalid Basic authorization header format.");

        var username = decoded[..separatorIndex];
        var password = decoded[(separatorIndex + 1)..];

        if (!string.Equals(username, config.Username, StringComparison.Ordinal) ||
            !string.Equals(password, config.Password, StringComparison.Ordinal))
        {
            return SyncAuthResult.Fail("Invalid credentials.");
        }

        return SyncAuthResult.Success(
            username,
            username,
            [new Claim(ClaimTypes.Name, username)]);
    }

    private static SyncAuthResult AuthenticateApiKey(ApiKeyAuthentication config, HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyValues) ||
            string.IsNullOrEmpty(apiKeyValues.ToString()))
        {
            return SyncAuthResult.Fail("Missing X-Api-Key header.");
        }

        var providedKey = apiKeyValues.ToString();

        if (!string.Equals(providedKey, config.ApiKey, StringComparison.Ordinal))
            return SyncAuthResult.Fail("Invalid API key.");

        return SyncAuthResult.Success(null, null, []);
    }

    private async Task<SyncAuthResult> AuthenticateJwtAsync(
        JwtAuthentication config,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) ||
            !AuthenticationHeaderValue.TryParse(authHeader, out var parsed) ||
            !string.Equals(parsed.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(parsed.Parameter))
        {
            return SyncAuthResult.Fail("Missing or invalid Bearer authorization header.");
        }

        var signingKeys = await GetSigningKeysAsync(config.JWKSEndpoint, cancellationToken);
        if (signingKeys is null)
            return SyncAuthResult.Fail("Failed to retrieve JWKS from endpoint.");

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config.Issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(parsed.Parameter, validationParameters);

        if (!result.IsValid)
        {
            logger.LogWarning("JWT validation failed for issuer {Issuer}: {Error}",
                config.Issuer, result.Exception?.Message);
            return SyncAuthResult.Fail("Invalid or expired token.");
        }

        var claims = result.ClaimsIdentity.Claims.ToList();

        var userId = claims
            .FirstOrDefault(c => c.Type == config.UserIdClaim)?.Value;

        string? userName = null;
        if (!string.IsNullOrEmpty(config.UserNameClaim))
            userName = claims.FirstOrDefault(c => c.Type == config.UserNameClaim)?.Value;

        return SyncAuthResult.Success(userId, userName, claims);
    }

    private async Task<IList<SecurityKey>?> GetSigningKeysAsync(
        string jwksEndpoint,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"jwks:{jwksEndpoint}";

        if (memoryCache.TryGetValue(cacheKey, out IList<SecurityKey>? cachedKeys))
            return cachedKeys;

        try
        {
            var httpClient = httpClientFactory.CreateClient("jwks");
            var response = await httpClient.GetStringAsync(jwksEndpoint, cancellationToken);
            var jwks = new JsonWebKeySet(response);
            var keys = jwks.GetSigningKeys();

            memoryCache.Set(cacheKey, keys, JwksCacheDuration);

            return keys;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch JWKS from {JwksEndpoint}", jwksEndpoint);
            return null;
        }
    }
}
