using System.Security.Claims;
using CoreSyncServer.Data;
using Microsoft.AspNetCore.Http;

namespace CoreSyncServer.Services;

public class SyncAuthResult
{
    public bool IsAuthenticated { get; init; }
    public string? UserId { get; init; }
    public string? UserName { get; init; }
    public IReadOnlyList<Claim> Claims { get; init; } = [];
    public string? ErrorMessage { get; init; }

    public static SyncAuthResult Success(string? userId, string? userName, IReadOnlyList<Claim> claims) =>
        new() { IsAuthenticated = true, UserId = userId, UserName = userName, Claims = claims };

    public static SyncAuthResult NoAuthRequired() =>
        new() { IsAuthenticated = true };

    public static SyncAuthResult Fail(string error) =>
        new() { IsAuthenticated = false, ErrorMessage = error };
}

public interface ISyncEndpointAuthService
{
    Task<SyncAuthResult> AuthenticateAsync(Data.Endpoint endpoint, HttpContext httpContext, CancellationToken cancellationToken = default);
}
