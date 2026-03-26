using CoreSyncServer.Components;
using CoreSyncServer.Components.Account;
using CoreSyncServer.Data;
using CoreSyncServer.Filters;
using CoreSyncServer.Server.Services;
using CoreSyncServer.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer;

public static class CoreSyncServerBuilderExtensions
{
    /// <summary>
    /// Registers all CoreSyncServer services using the default <see cref="ApplicationDbContext"/>
    /// with the connection string named "DefaultConnection".
    /// SaaS hosts can call individual pieces instead for more control.
    /// </summary>
    public static WebApplicationBuilder AddCoreSyncServer(this WebApplicationBuilder builder)
    {
        return builder.AddCoreSyncServer<ApplicationDbContext>();
    }

    /// <summary>
    /// Registers all CoreSyncServer services using a custom DbContext derived from
    /// <see cref="ApplicationDbContext"/>. The connection string "DefaultConnection" is used.
    /// </summary>
    public static WebApplicationBuilder AddCoreSyncServer<TContext>(this WebApplicationBuilder builder)
        where TContext : ApplicationDbContext
    {
        builder.Services.AddControllers(options =>
        {
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        });
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddInteractiveWebAssemblyComponents()
            .AddAuthenticationStateSerialization();

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        builder.Services.AddCoreSyncServerServices<TContext>(
            builder.Configuration,
            options => options.UseNpgsql(connectionString, b => b.MigrationsAssembly("CoreSyncServer")));

        builder.Services.AddMemoryCache();
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

        builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
        builder.Services.AddScoped<INotificationService, SmtpNotificationService>();

        builder.Services.AddScoped<SyncEndpointAuthFilter>();

        builder.Services.AddHostedService<MigrationHostedService>();

        builder.Services.AddScoped(sp =>
        {
            var navigationManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
            return new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) };
        });

        return builder;
    }

    /// <summary>
    /// Configures the CoreSyncServer HTTP request pipeline.
    /// Call this after any custom middleware (e.g. tenant resolution) has been added.
    /// </summary>
    public static WebApplication UseCoreSyncServer(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseWebAssemblyDebugging();
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapControllers();
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(CoreSyncServer.Client._Imports).Assembly);

        app.MapAdditionalIdentityEndpoints();

        return app;
    }
}
