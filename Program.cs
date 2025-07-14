using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Rally.Data;
using Rally.Models;
using Rally.Services; // Contains EmailSender and DiscordWebhookSink
using Rally.Configuration;
using Duende.IdentityServer.EntityFramework.DbContexts; // For DbContexts
using Duende.IdentityServer.EntityFramework.Mappers; // For ToEntity()
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using NetEscapades.AspNetCore.SecurityHeaders;
// using System.Threading.RateLimiting; // Keep commented if Rate Limiter is commented out
// using Microsoft.AspNetCore.RateLimiting; // Keep commented if Rate Limiter is commented out
using Microsoft.AspNetCore.Builder;
// --- SERILOG Usings ---
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Microsoft.Extensions.DependencyInjection; // For IHttpClientFactory

/// <summary>
/// This is the main entry point and configuration file for the Rally Identity Provider (v1).
/// This version is built using Duende IdentityServer and serves as the central authentication hub
/// for the v1 Logistics and Asset Tracking platform.
/// </summary>

// --- 1. BOOTSTRAP LOGGING CONFIGURATION ---
// Serilog is configured here at the very start of the application's lifecycle. This "bootstrap logger"
// ensures that any issues during the host configuration process itself can be captured.
// --- Configure Serilog EARLY ---
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information) // Keep startup info
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information)
    .MinimumLevel.Override("Duende.IdentityServer", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithProcessId().Enrich.WithThreadId().Enrich.WithMachineName()
    .WriteTo.Console() // Initial console logging
    .ReadFrom.Configuration(WebApplication.CreateBuilder(args).Configuration) // Read initial config
    .CreateBootstrapLogger(); // Use bootstrap logger

try
{
    Log.Information("Configuring web host..."); // Log startup

    var builder = WebApplication.CreateBuilder(args);

    // --- 2. SERVICE REGISTRATION & CONFIGURATION ---

    // --- ADD HttpClientFactory Registration ---
    builder.Services.AddHttpClient("DiscordWebhookClient"); // Named client for Discord sink

    // --- Tell ASP.NET Core to use Serilog (using final configuration) ---
    builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProcessId().Enrich.WithThreadId().Enrich.WithMachineName() // Enrichers can be added here too
        // Define final sink configurations
        .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
        .WriteTo.File(
            formatter: new RenderedCompactJsonFormatter(),
            path: Path.Combine(AppContext.BaseDirectory, "logs", "rally-idp-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            fileSizeLimitBytes: 20 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true,
            flushToDiskInterval: TimeSpan.FromSeconds(5),
            restrictedToMinimumLevel: LogEventLevel.Debug) // Log Debug+ to file
        // --- Discord Sink via Map ---
        .WriteTo.Map(
            logEvent => logEvent.Properties.TryGetValue("AlertType", out var alertTypeValue)
                            ? alertTypeValue.ToString().Trim('"')
                            : "__Default__",
            (alertTypeKey, wt) =>
            {
                var alertTypesToSend = new[] { "LoginFailure", "AccountLocked", "AdminAction", "FatalError", "SuspiciousBehavior" };
                if (alertTypesToSend.Contains(alertTypeKey))
                {
                    var webhookUrl = context.Configuration["Discord:WebhookUrl"]; // Read from final config
                    if (!string.IsNullOrWhiteSpace(webhookUrl))
                    {
                         wt.DiscordWebhook(
                             webhookUrl,
                             services.GetRequiredService<IHttpClientFactory>(), // Get factory from services
                             LogEventLevel.Information); // Send Info+ matching AlertType
                    }
                    else
                    {
                         // Log locally if Discord isn't configured
                         Console.WriteLine($"[Serilog Config] Discord Webhook URL not configured. Alert for {alertTypeKey} not sent.");
                    }
                }
            },
            sinkMapCountLimit: 10) // Limit unique map keys
        );


    // --- Service Configurations ---

    // --- 1. Database Configuration ---
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    var migrationsAssembly = typeof(Program).Assembly.GetName().Name;
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString,
            npgsqlOptions => npgsqlOptions.MigrationsAssembly(migrationsAssembly)));

    // --- 1.5 Data Protection Configuration ---
    builder.Services.AddDataProtection()
        .PersistKeysToDbContext<ApplicationDbContext>()
        .SetApplicationName("RallyIdP");

    // --- 2. ASP.NET Core Identity Configuration ---
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.SignIn.RequireConfirmedAccount = true; // Keep true
            // options.Password.RequireDigit = false;
            // options.Password.RequiredLength = 6;
            // options.Password.RequireNonAlphanumeric = false;
            // options.Password.RequireUppercase = false;
            // options.Password.RequireLowercase = false;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultPhoneProvider;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

    // --- 2.5 Explicit Authentication Configuration (REMAINS COMMENTED OUT / REMOVED) ---
    /*
    builder.Services.AddAuthentication()
        .AddCookie(IdentityConstants.ApplicationScheme, options => {...})
        .AddCookie(IdentityConstants.ExternalScheme, options => {...});
    */

    // --- Rate Limiter Configuration (COMMENTED OUT) ---
    /*
    using System.Threading.RateLimiting;
    using Microsoft.AspNetCore.RateLimiting;
    builder.Services.AddRateLimiter(options =>
    {
        options.AddFixedWindowLimiter(policyName: "fixedLogin", fixedWindowOptions => { ... });
        options.AddFixedWindowLimiter(policyName: "fixedToken", fixedWindowOptions => { ... });
        options.AddFixedWindowLimiter(policyName: "fixedPar", fixedWindowOptions => { ... });
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, cancellationToken) => { ... };
    });
    */

    // --- 3. Duende IdentityServer Configuration ---
    builder.Services.AddIdentityServer(options =>
        {
            options.Events.RaiseErrorEvents = true;
            options.Events.RaiseInformationEvents = true;
            options.Events.RaiseFailureEvents = true;
            options.Events.RaiseSuccessEvents = true;
            options.EmitStaticAudienceClaim = true;
            options.UserInteraction.LoginUrl = "/Identity/Account/Login";
            options.UserInteraction.LogoutUrl = "/Identity/Account/Logout";
            options.UserInteraction.ConsentUrl = "/Consent";
            // options.PushedAuthorization.Required = false; // Keep commented/default for now
        })
        .AddConfigurationStore(options =>
        {
            options.ConfigureDbContext = db => db.UseNpgsql(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));
        })
        .AddOperationalStore(options =>
        {
            options.ConfigureDbContext = db => db.UseNpgsql(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));
            options.EnableTokenCleanup = true;
            options.TokenCleanupInterval = 3600;
        })
        .AddAspNetIdentity<ApplicationUser>()
        .AddSigningCredential(LoadSigningCertificate(builder.Configuration, builder.Environment));

    // --- 4. Application Services ---
    builder.Services.AddRazorPages(); // Simple AddRazorPages call

    // --- 4.5 Email Sender Registration ---
    builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
    builder.Services.AddTransient<IEmailSender, EmailSender>();

    // --- 5. Authorization ---
    builder.Services.AddAuthorization();

    // ========================================================================
    var app = builder.Build();
    // ========================================================================

  
    // --- Seed Initial OIDC Configuration Data (Development Only) ---
    if (app.Environment.IsDevelopment())
    {
        try
        {
            Log.Information("Attempting database seeding for Duende IdentityServer configuration...");
            using (var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
            {
                var provider = serviceScope.ServiceProvider;
                var configContext = provider.GetRequiredService<ConfigurationDbContext>();
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupSeeding");

                await configContext.Database.MigrateAsync(); // Ensure migrations are applied

                // --- Seed Identity Resources ---
                var identityResourcesInConfig = Config.GetIdentityResources().ToList();
                var existingIdentityResourceNames = await configContext.IdentityResources.Select(ir => ir.Name).ToListAsync();
                
                foreach (var resource in identityResourcesInConfig)
                {
                    if (!existingIdentityResourceNames.Contains(resource.Name))
                    {
                        logger.LogInformation("--> Seeding NEW IdentityResource: {IdentityResourceName}", resource.Name);
                        configContext.IdentityResources.Add(resource.ToEntity());
                    }
                    // else { logger.LogInformation("--> IdentityResource '{IdentityResourceName}' already exists.", resource.Name); }
                }

                // --- Seed API Scopes ---
                var apiScopesInConfig = Config.GetApiScopes().ToList();
                var existingApiScopeNames = await configContext.ApiScopes.Select(s => s.Name).ToListAsync();

                foreach (var scope in apiScopesInConfig)
                {
                    if (!existingApiScopeNames.Contains(scope.Name))
                    {
                        logger.LogInformation("--> Seeding NEW ApiScope: {ApiScopeName}", scope.Name);
                        configContext.ApiScopes.Add(scope.ToEntity());
                    }
                    // else { logger.LogInformation("--> ApiScope '{ApiScopeName}' already exists.", scope.Name); }
                }
                
                // --- Seed API Resources ---
                var apiResourcesInConfig = Config.GetApiResources().ToList();
                var existingApiResourceNames = await configContext.ApiResources.Select(ar => ar.Name).ToListAsync();

                foreach (var resource in apiResourcesInConfig)
                {
                    if (!existingApiResourceNames.Contains(resource.Name))
                    {
                        logger.LogInformation("--> Seeding NEW ApiResource: {ApiResourceName}", resource.Name);
                        configContext.ApiResources.Add(resource.ToEntity());
                    }
                    // else { logger.LogInformation("--> ApiResource '{ApiResourceName}' already exists.", resource.Name); }
                }

                // --- Seed Clients ---
                var clientsInConfig = Config.GetClients().ToList();
                var existingClientIds = await configContext.Clients.Select(c => c.ClientId).ToListAsync();

                foreach (var client in clientsInConfig)
                {
                    if (!existingClientIds.Contains(client.ClientId))
                    {
                        logger.LogInformation("--> Seeding NEW Client: {ClientId}", client.ClientId);
                        // Ensure all navigation properties (like AllowedScopes) are correctly mapped by ToEntity()
                        // If client.AllowedScopes from Config.cs is just a list of strings,
                        // and ToEntity() needs the actual Scope entities, this part might need adjustment
                        // or ensure that ClientMappers.ToEntity correctly handles scope strings.
                        // For Duende IS, client.AllowedScopes is typically List<string> in the model.
                        configContext.Clients.Add(client.ToEntity());
                    }
                    else
                    {
                        // OPTIONAL: Update existing client if needed (e.g., if scopes changed)
                        // This is more complex as it involves fetching the existing client,
                        // comparing properties, and updating. For MVP, adding if missing is simpler.
                        logger.LogInformation("--> Client '{ClientId}' already exists. Consider manual update or more complex seeding if properties changed.", client.ClientId);
                        
                        // Example of updating scopes for an existing client (simplified):
                        var existingClient = await configContext.Clients
                            .Include(c => c.AllowedScopes) // Important: Include existing scopes
                            .FirstOrDefaultAsync(c => c.ClientId == client.ClientId);
                        
                        if (existingClient != null)
                        {
                            bool clientUpdated = false;
                            // Check and update AllowedScopes
                            var newScopes = client.AllowedScopes.ToList();
                            var currentScopes = existingClient.AllowedScopes.Select(s => s.Scope).ToList();

                            // Add scopes not yet present
                            foreach (var newScope in newScopes)
                            {
                                if (!currentScopes.Contains(newScope))
                                {
                                    existingClient.AllowedScopes.Add(new Duende.IdentityServer.EntityFramework.Entities.ClientScope { Scope = newScope });
                                    clientUpdated = true;
                                    logger.LogInformation("----> Added scope '{Scope}' to client '{ClientId}'", newScope, client.ClientId);
                                }
                            }
                            // Remove scopes no longer needed (more complex, be careful with relationships)
                            // For simplicity, we'll just add missing ones for now.
                            // To remove, you'd find scopes in currentScopes not in newScopes and remove them from existingClient.AllowedScopes.

                            // Compare other properties and update if necessary (ClientName, RedirectUris, etc.)
                            if (existingClient.ClientName != client.ClientName)
                            {
                                existingClient.ClientName = client.ClientName;
                                clientUpdated = true;
                            }
                            // Add similar checks for RedirectUris, PostLogoutRedirectUris etc.
                            // Be careful with collections - you often need to clear and re-add or manage individual items.

                            if(clientUpdated)
                            {
                                logger.LogInformation("--> Updating existing Client: {ClientId}", client.ClientId);
                            }
                        }
                    }
                }

                // Save all changes made during seeding
                var changes = await configContext.SaveChangesAsync();
                if (changes > 0)
                {
                    logger.LogInformation("--> Database seeding applied {ChangeCount} changes.", changes);
                }
                else
                {
                    logger.LogInformation("--> No new configuration entities needed to be seeded, or no changes detected in existing clients.");
                }
            }
            Log.Information("Database seeding attempt finished.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "XXXXXXXXXX Error during Duende IdentityServer DB Seeding. Check configuration and database state.");
            // Consider re-throwing if seeding is essential for startup, especially for first run.
            // throw;
        }
    }
    
    // Configure Forwarded Headers VERY EARLY if behind proxy
    // app.UseForwardedHeaders();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();

    // --- Security Headers Middleware ---
    app.UseSecurityHeaders(policies =>
        policies
            .AddFrameOptionsDeny()
            .AddContentTypeOptionsNoSniff()
            .AddReferrerPolicyStrictOriginWhenCrossOrigin()
            .RemoveServerHeader()
            .AddContentSecurityPolicy(csp =>
               {
                   csp.AddObjectSrc().None();
                   csp.AddFormAction()
                    .Self()
                    .From("*");  // Accept form submissions from any origin
                //    csp.AddFormAction().From("https://localhost:7223");
                //    csp.AddFormAction().Self();   //.From("*"); when debug .Self();
                    // .From("https://localhost:7223")                    
                    // .From("https://localhost:7272")
                    // .From("https://localhost:7268");
                   csp.AddFrameAncestors().None();
                   csp.AddScriptSrc().Self().UnsafeInline(); // Keep unsafe-inline for now
                   csp.AddStyleSrc().Self().UnsafeInline(); // Add if needed for inline styles
                   csp.AddImgSrc().Self().Data();
                   csp.AddFontSrc().Self();
                   csp.AddConnectSrc().Self();
               })
            .AddPermissionsPolicy(perm => perm.AddDefaultSecureDirectives())
        );

    // --- Serilog Request Logging ---
     app.UseSerilogRequestLogging();

    app.UseRouting();

    // --- Rate Limiter Middleware (COMMENTED OUT) ---
    // app.UseRateLimiter();

    // --- Auth Middleware ---
    app.UseIdentityServer();
    app.UseAuthorization();

    // --- Map endpoints ---
    app.MapStaticAssets();
    app.MapRazorPages().WithStaticAssets();

    app.Run();

}
catch (Exception ex) // Catch bootstrap errors
{
    Log.Fatal(ex, "Rally IdP Host terminated unexpectedly during startup");
}
finally
{
    Log.Information("Shutting down Rally IdP host.");
    Log.CloseAndFlush(); // Ensure logs are written
}


// --- Helper Function for Cert Loading ---
X509Certificate2 LoadSigningCertificate(ConfigurationManager config, IWebHostEnvironment env)
{
    var certPassword = config["IdentityServer:SigningCertPassword"];
    if (string.IsNullOrEmpty(certPassword)) throw new InvalidOperationException("IdentityServer:SigningCertPassword is not configured.");
    var certPath = config["IdentityServer:SigningCertPath"];
    if (string.IsNullOrEmpty(certPath)) throw new InvalidOperationException("IdentityServer:SigningCertPath is not configured.");
    if (!File.Exists(certPath)) throw new FileNotFoundException($"Signing certificate not found at path: {certPath}");
    try
    {
        #pragma warning disable SYSLIB0057
        return new X509Certificate2(certPath, certPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        #pragma warning restore SYSLIB0057
    }
    catch (Exception ex)
    {
         throw new InvalidOperationException($"Error loading signing certificate from path: {certPath}", ex);
    }
}
