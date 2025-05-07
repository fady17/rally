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
            Log.Information("Attempting database seeding..."); // Log seeding start
            using (var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
            {
                var provider = serviceScope.ServiceProvider;
                var configContext = provider.GetRequiredService<ConfigurationDbContext>();
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupSeeding");

                // Identity Resources
                bool anyIdentity = await configContext.IdentityResources.AnyAsync();
                logger.LogInformation("Seeding Check: IdentityResources.AnyAsync() = {AnyResult}", anyIdentity);
                if (!anyIdentity)
                {
                    logger.LogInformation("--> Seeding Identity Resources...");
                    foreach (var resource in Config.GetIdentityResources()) { configContext.IdentityResources.Add(resource.ToEntity()); }
                    await configContext.SaveChangesAsync();
                    logger.LogInformation("--> Done Seeding Identity Resources.");
                }

                // API Scopes
                bool anyApiScopes = await configContext.ApiScopes.AnyAsync();
                logger.LogInformation("Seeding Check: ApiScopes.AnyAsync() = {AnyResult}", anyApiScopes);
                if (!anyApiScopes)
                {
                     logger.LogInformation("--> Seeding Api Scopes...");
                    foreach (var scope in Config.GetApiScopes()) { configContext.ApiScopes.Add(scope.ToEntity()); }
                    await configContext.SaveChangesAsync();
                     logger.LogInformation("--> Done Seeding Api Scopes.");
                }

                // Clients
                bool anyClients = await configContext.Clients.AnyAsync();
                logger.LogInformation("Seeding Check: Clients.AnyAsync() = {AnyResult}", anyClients); // Log the check result
                if (!anyClients)
                {
                     logger.LogInformation("--> Seeding Clients...");
                    foreach (var client in Config.GetClients()) { configContext.Clients.Add(client.ToEntity()); }
                    await configContext.SaveChangesAsync();
                    logger.LogInformation("--> Done Seeding Clients.");
                }
                 else
                 {
                     logger.LogWarning("--> SKIPPING Client Seeding because Clients.AnyAsync() returned true."); // Log skip reason
                 }
            }
             Log.Information("Database seeding attempt finished.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "XXXXXXXXXX Error during DB Seeding."); // Log Error, not just Console
            // throw; // Consider re-throwing if seeding is essential for startup
        }
    }

    // --- Configure the HTTP request pipeline ---
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
                   csp.AddFormAction().Self().From("https://localhost:7272"); // Adjust port if client changes
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


// using Microsoft.AspNetCore.Identity;
// using Microsoft.AspNetCore.Identity.UI.Services;
// using Microsoft.EntityFrameworkCore;
// using Rally.Data;
// using Rally.Models;
// using Rally.Services; // Contains EmailSender and DiscordWebhookSink
// using Rally.Configuration;
// using Duende.IdentityServer.EntityFramework.DbContexts;
// using Duende.IdentityServer.EntityFramework.Mappers;
// using System.Security.Cryptography.X509Certificates;
// using Microsoft.AspNetCore.DataProtection;
// using NetEscapades.AspNetCore.SecurityHeaders;
// // using System.Threading.RateLimiting; // Rate Limiter commented out
// // using Microsoft.AspNetCore.RateLimiting; // Rate Limiter commented out
// using Microsoft.AspNetCore.Builder;
// // --- SERILOG Usings ---
// using Serilog;
// using Serilog.Core; // For LoggerSinkConfiguration
// using Serilog.Events;
// using Serilog.Formatting.Compact; // Or Json
// using Microsoft.Extensions.DependencyInjection; // For IHttpClientFactory

// // --- Configure Serilog EARLY ---
// Log.Logger = new LoggerConfiguration()
//     .MinimumLevel.Debug() // Capture Debug+ by default
//     .MinimumLevel.Override("Microsoft", LogEventLevel.Warning) // Default override for MS logs
//     .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information) // Show startup/shutdown info
//     .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning) // Default override for EF
//     .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information) // See SQL commands
//     .MinimumLevel.Override("Duende.IdentityServer", LogEventLevel.Information) // See IS info logs
//     .Enrich.FromLogContext() // Required for context properties like RequestId, AlertType
//     .Enrich.WithProcessId()
//     .Enrich.WithThreadId()
//     .Enrich.WithMachineName()
//     .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information) // Console for Info+
//     .WriteTo.File( // File sink for Debug+
//         formatter: new RenderedCompactJsonFormatter(), // Structured JSON
//         path: Path.Combine(AppContext.BaseDirectory, "logs", "rally-idp-.log"), // Relative path to logs folder
//         rollingInterval: RollingInterval.Day,
//         retainedFileCountLimit: 7, // Keep 7 days of logs
//         fileSizeLimitBytes: 20 * 1024 * 1024, // 20 MB limit
//         rollOnFileSizeLimit: true,
//         shared: true,
//         flushToDiskInterval: TimeSpan.FromSeconds(5),
//         restrictedToMinimumLevel: LogEventLevel.Debug // Log Debug+ to file
//         )
//     // --- Add Discord Sink via Map below after builder services are available ---
//     .ReadFrom.Configuration(WebApplication.CreateBuilder(args).Configuration) // Read appsettings overrides early (for initial levels)
//     .CreateBootstrapLogger(); // Use bootstrap logger initially

// try
// {
//     var builder = WebApplication.CreateBuilder(args);

//     // --- Tell ASP.NET Core to use Serilog ---
//     builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
//         .ReadFrom.Configuration(context.Configuration) // Read final config (incl appsettings)
//         .ReadFrom.Services(services) // Read enrichers etc. registered as services
//         .Enrich.FromLogContext()
//         .MinimumLevel.Override("Microsoft", LogEventLevel.Warning) // Re-apply overrides if needed
//         .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information)
//         .MinimumLevel.Override("Duende.IdentityServer", LogEventLevel.Information)
//         // Console Sink Configuration (optional if defaults from bootstrap are fine)
//         .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
//         // File Sink Configuration (optional if defaults from bootstrap are fine)
//          .WriteTo.File(
//             formatter: new RenderedCompactJsonFormatter(),
//             path: Path.Combine(AppContext.BaseDirectory, "logs", "rally-idp-.log"),
//             rollingInterval: RollingInterval.Day,
//             retainedFileCountLimit: 7,
//             fileSizeLimitBytes: 20 * 1024 * 1024,
//             rollOnFileSizeLimit: true,
//             shared: true,
//             flushToDiskInterval: TimeSpan.FromSeconds(5),
//             restrictedToMinimumLevel: LogEventLevel.Debug)
//         // --- Discord Sink via Map (using IServiceProvider) ---
//         .WriteTo.Map(
//             logEvent => logEvent.Properties.TryGetValue("AlertType", out var alertTypeValue)
//                             ? alertTypeValue.ToString().Trim('"')
//                             : "__Default__", // Key extractor
//             (alertTypeKey, wt) => // Sink configuration action
//             {
//                 var alertTypesToSend = new[] { "LoginFailure", "AccountLocked", "AdminAction", "FatalError", "SuspiciousBehavior" };
//                 if (alertTypesToSend.Contains(alertTypeKey))
//                 {
//                     var webhookUrl = context.Configuration["Discord:WebhookUrl"]; // Read from final config
//                     if (!string.IsNullOrWhiteSpace(webhookUrl))
//                     {
//                          // Use the extension method, passing required services
//                          wt.DiscordWebhook(
//                              webhookUrl,
//                              services.GetRequiredService<IHttpClientFactory>(), // Get factory from services passed by UseSerilog
//                              LogEventLevel.Information); // Send Info+ logs matching the AlertType key
//                     }
//                     else
//                     {
//                          // Log locally if Discord isn't configured
//                          Console.WriteLine($"[Serilog Config] Discord Webhook URL not configured. Alert for {alertTypeKey} not sent.");
//                          // Use a Null sink to prevent errors if you want to be very robust
//                          // wt.Sink(new Serilog.Sinks.Null.NullSink(), LogEventLevel.Verbose);
//                     }
//                 }
//             },
//             sinkMapCountLimit: 10)); // Limit unique keys


//     // --- 1. Database Configuration ---
//     var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
//     var migrationsAssembly = typeof(Program).Assembly.GetName().Name;
//     builder.Services.AddDbContext<ApplicationDbContext>(options =>
//         options.UseNpgsql(connectionString,
//             npgsqlOptions => npgsqlOptions.MigrationsAssembly(migrationsAssembly)));

//     // --- 1.5 Data Protection Configuration ---
//     builder.Services.AddDataProtection()
//         .PersistKeysToDbContext<ApplicationDbContext>()
//         .SetApplicationName("RallyIdP");

//     // --- ADD HttpClientFactory ---
//     builder.Services.AddHttpClient("DiscordWebhookClient"); // Named client for Discord sink

//     // --- 2. ASP.NET Core Identity Configuration ---
//     builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
//         {
//             options.SignIn.RequireConfirmedAccount = true; // Keep true since email sending works
//             options.Password.RequireDigit = false;
//             options.Password.RequiredLength = 6;
//             options.Password.RequireNonAlphanumeric = false;
//             options.Password.RequireUppercase = false;
//             options.Password.RequireLowercase = false;
//             options.Lockout.AllowedForNewUsers = true;
//             options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
//             options.Lockout.MaxFailedAccessAttempts = 5;
//             options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultPhoneProvider; // Use short code provider
//         })
//         .AddEntityFrameworkStores<ApplicationDbContext>()
//         .AddDefaultTokenProviders();

//     // --- 2.5 Explicit Authentication Configuration (REMAINS COMMENTED OUT) ---
//     /* ... */

//     // --- Rate Limiter Configuration (COMMENTED OUT) ---
//     /* builder.Services.AddRateLimiter(...) */

//     // --- 3. Duende IdentityServer Configuration ---
//     builder.Services.AddIdentityServer(options =>
//         {
//             // ... IdentityServer options ...
//             options.UserInteraction.LoginUrl = "/Identity/Account/Login";
//             options.UserInteraction.LogoutUrl = "/Identity/Account/Logout";
//             options.UserInteraction.ConsentUrl = "/Consent";
//         })
//         .AddConfigurationStore(options =>
//         {
//             options.ConfigureDbContext = dbContextBuilder =>
//                 dbContextBuilder.UseNpgsql(connectionString,
//                     npgsqlOptions => npgsqlOptions.MigrationsAssembly(migrationsAssembly));
//         })
//         .AddOperationalStore(options =>
//         {
//             options.ConfigureDbContext = dbContextBuilder =>
//                 dbContextBuilder.UseNpgsql(connectionString,
//                     npgsqlOptions => npgsqlOptions.MigrationsAssembly(migrationsAssembly));
//             options.EnableTokenCleanup = true;
//             options.TokenCleanupInterval = 3600;
//         })
//         .AddAspNetIdentity<ApplicationUser>()
//         .AddSigningCredential(LoadSigningCertificate(builder.Configuration, builder.Environment));

//     // --- 4. Application Services ---
//     builder.Services.AddRazorPages(); // Keep simple AddRazorPages

//     // --- 4.5 Email Sender Registration ---
//     builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
//     builder.Services.AddTransient<IEmailSender, EmailSender>();

//     // --- 5. Authorization ---
//     builder.Services.AddAuthorization();

//     // ========================================================================
//     var app = builder.Build();
//     // ========================================================================

//     // --- Seed Initial OIDC Configuration Data (Development Only) ---
//     // ... (LoadSigningCertificate function remains the same) ...
//     // ... (Seeding logic remains the same) ...

//     // --- Configure the HTTP request pipeline ---
//     // Configure Forwarded Headers VERY EARLY if behind proxy
//     // app.UseForwardedHeaders();

//     if (app.Environment.IsDevelopment())
//     {
//         app.UseDeveloperExceptionPage();
//     }
//     else
//     {
//         app.UseExceptionHandler("/Error");
//         app.UseHsts();
//     }

//     app.UseHttpsRedirection();
//     app.UseStaticFiles();

//     // --- Security Headers Middleware ---
//     app.UseSecurityHeaders(policies =>
//         policies
//             // ... (Keep your security header policy configuration) ...
//             .AddContentSecurityPolicy(csp =>
//                {
//                    // ... CSP directives ...
//                    csp.AddScriptSrc().Self().UnsafeInline(); // Keep unsafe-inline for now
//                })
//             .AddPermissionsPolicy(perm => perm.AddDefaultSecureDirectives())
//         );

//     // --- Serilog Request Logging ---
//     // Place AFTER static files/security headers, but BEFORE routing/authz/endpoints
//     app.UseSerilogRequestLogging();

//     app.UseRouting();

//     app.UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}");

//     // --- Rate Limiter Middleware (COMMENTED OUT) ---
//     // app.UseRateLimiter();

//     // --- Auth Middleware ---
//     app.UseIdentityServer(); // Handles OIDC requests and its auth interactions
//     app.UseAuthorization(); // Applies authorization policies

//     // --- Map endpoints ---
//     app.MapStaticAssets();
//     app.MapRazorPages().WithStaticAssets();

//     Log.Information("Starting host run...");
//     app.Run();

// }
// catch (Exception ex)
// {
//     Log.Fatal(ex, "Rally IdP Host terminated unexpectedly");
// }
// finally
// {
//     Log.Information("Shutting down Rally IdP host.");
//     Log.CloseAndFlush(); // Ensure logs are written on shutdown
// }

// // --- Helper Function for Cert Loading ---
// // Needs to be defined outside the try block if called before builder.Build()
// // or keep it inside if only called by services.AddIdentityServer
// X509Certificate2 LoadSigningCertificate(ConfigurationManager config, IWebHostEnvironment env)
// {
//     var certPassword = config["IdentityServer:SigningCertPassword"];
//     if (string.IsNullOrEmpty(certPassword)) throw new InvalidOperationException("IdentityServer:SigningCertPassword is not configured.");
//     var certPath = config["IdentityServer:SigningCertPath"];
//     if (string.IsNullOrEmpty(certPath)) throw new InvalidOperationException("IdentityServer:SigningCertPath is not configured.");
//     if (!File.Exists(certPath)) throw new FileNotFoundException($"Signing certificate not found at path: {certPath}");
//     try
//     {
//         #pragma warning disable SYSLIB0057
//         return new X509Certificate2(certPath, certPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
//         #pragma warning restore SYSLIB0057
//     }
//     catch (Exception ex)
//     {
//          throw new InvalidOperationException($"Error loading signing certificate from path: {certPath}", ex);
//     }
// }