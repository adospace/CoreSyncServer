using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CoreSyncServer.Tests.Infrastructure;

/// <summary>
/// Provides a way to create authenticated HttpClients for integration tests
/// by bypassing the real authentication pipeline with a test auth handler.
/// </summary>
public static class AuthenticatedHttpClientExtensions
{
    public const string TestScheme = "TestScheme";
    public const string DefaultUserId = "test-user-001";
    public const string DefaultUserName = "testuser";

    public static HttpClient CreateAuthenticatedClient(
        this CustomWebApplicationFactory factory,
        string? userId = null,
        string? userName = null)
        => factory.CreateAuthenticatedFactory(userId, userName).CreateAuthenticatedClient();

    /// <summary>
    /// The host an authenticated client talks to. It is a separate host from
    /// <paramref name="factory"/>, with its own singletons, so a test that needs to see server
    /// state (e.g. the sync provider cache) must resolve it from here rather than from the
    /// fixture's <c>Services</c>.
    /// </summary>
    public static WebApplicationFactory<Program> CreateAuthenticatedFactory(
        this CustomWebApplicationFactory factory,
        string? userId = null,
        string? userName = null)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestScheme;
                    options.DefaultChallengeScheme = TestScheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestScheme, _ => { });

                // Store claims in a singleton so TestAuthHandler can pick them up
                services.AddSingleton(new TestClaimsProvider(
                    userId ?? DefaultUserId,
                    userName ?? DefaultUserName));
            });
        });
    }

    public static HttpClient CreateAuthenticatedClient(this WebApplicationFactory<Program> authenticatedFactory)
    {
        var client = authenticatedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(TestScheme);

        return client;
    }
}

public record TestClaimsProvider(string UserId, string UserName);
