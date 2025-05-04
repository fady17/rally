using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services; 
using Microsoft.EntityFrameworkCore;
using Rally.Data;  
using Rally.Models; 
using Rally.Services;
using Rally.Configuration;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;

var builder = WebApplication.CreateBuilder(args);

// --- 1. Database Configuration ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
var migrationsAssembly = typeof(Program).Assembly.GetName().Name;

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString,
        npgsqlOptions => npgsqlOptions.MigrationsAssembly(migrationsAssembly)));

// --- 2. ASP.NET Core Identity Configuration ---
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // We set RequireConfirmedAccount to false for now,
        // as we are using a dummy email sender. Change if implementing real email confirmation.
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders(); // For password reset, 2FA tokens etc.

// --- 2.5 Explicit Authentication Configuration (REMAINS COMMENTED OUT / REMOVED) ---
/*
// This block caused scheme conflicts in the .NET 9 Preview environment and was removed.
// We rely on AddIdentity() to configure its necessary cookie schemes internally.
builder.Services.AddAuthentication()
    .AddCookie(IdentityConstants.ApplicationScheme, options => {...})
    .AddCookie(IdentityConstants.ExternalScheme, options => {...});
*/

// --- 3. Duende IdentityServer Configuration (RE-ENABLED) ---
builder.Services.AddIdentityServer(options =>
    {
        options.Events.RaiseErrorEvents = true;
        options.Events.RaiseInformationEvents = true;
        options.Events.RaiseFailureEvents = true;
        options.Events.RaiseSuccessEvents = true;
        options.EmitStaticAudienceClaim = true;
        // Optional: Set the public facing IssuerUri if needed, especially if behind a reverse proxy
        // options.IssuerUri = "https://your-idp-domain.com"
        options.UserInteraction.LoginUrl = "/Identity/Account/Login"; 
        options.UserInteraction.LogoutUrl = "/Identity/Account/Logout";
        options.UserInteraction.ConsentUrl = "/Consent";
    })
    .AddConfigurationStore(options =>
    {
        options.ConfigureDbContext = dbContextBuilder =>
            dbContextBuilder.UseNpgsql(connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(migrationsAssembly));
    })
    .AddOperationalStore(options =>
    {
        options.ConfigureDbContext = dbContextBuilder =>
            dbContextBuilder.UseNpgsql(connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(migrationsAssembly));
        options.EnableTokenCleanup = true;
        options.TokenCleanupInterval = 3600; // 1 hour
    })
    .AddAspNetIdentity<ApplicationUser>() // Integrates with the Identity setup above
    .AddDeveloperSigningCredential(); // DEVELOPMENT ONLY! Replace for Production.

// --- 4. Application Services ---
builder.Services.AddRazorPages(); // Add services for Razor Pages

// --- 4.5 Email Sender Registration ---
// Register the dummy email sender (or your real one later)
builder.Services.AddSingleton<IEmailSender, DummyEmailSender>();

// --- 5. Authorization ---
builder.Services.AddAuthorization(); // Basic authorization services

// ========================================================================
var app = builder.Build();
// ========================================================================
// --- Seed Initial OIDC Configuration Data (Development Only) ---
if (app.Environment.IsDevelopment())
{
    try
    {
        // Use GetRequiredService to ensure the factory exists and satisfy null analysis
        using (var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            var provider = serviceScope.ServiceProvider;

            // Optional: Ensure databases are migrated (can remove if using CLI exclusively)
            // await provider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
            // await provider.GetRequiredService<ConfigurationDbContext>().Database.MigrateAsync();
            // await provider.GetRequiredService<PersistedGrantDbContext>().Database.MigrateAsync();

            var configContext = provider.GetRequiredService<ConfigurationDbContext>();

            // Seed Identity Resources
            if (!await configContext.IdentityResources.AnyAsync())
            {
                Console.WriteLine("--> Seeding Identity Resources...");
                foreach (var resource in Config.GetIdentityResources())
                {
                    configContext.IdentityResources.Add(resource.ToEntity());
                }
                await configContext.SaveChangesAsync();
                Console.WriteLine("--> Done Seeding Identity Resources.");
            }

            // Seed API Scopes (if any)
            if (!await configContext.ApiScopes.AnyAsync())
            {
                 Console.WriteLine("--> Seeding Api Scopes...");
                foreach (var scope in Config.GetApiScopes()) // Will be empty now
                {
                    configContext.ApiScopes.Add(scope.ToEntity());
                }
                await configContext.SaveChangesAsync();
                 Console.WriteLine("--> Done Seeding Api Scopes.");
            }

            // Seed Clients
            if (!await configContext.Clients.AnyAsync())
            {
                 Console.WriteLine("--> Seeding Clients...");
                foreach (var client in Config.GetClients())
                {
                    configContext.Clients.Add(client.ToEntity());
                }
                await configContext.SaveChangesAsync();
                Console.WriteLine("--> Done Seeding Clients.");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"XXXXXXXXXX Error during DB Seeding: {ex.Message}");
        // throw; // Consider re-throwing in production if seeding is critical
    }
}

// Configure the HTTP request pipeline.
// --- CORRECTED Environment Check ---
if (app.Environment.IsDevelopment()) // Use Developer options IN Development
{
    app.UseDeveloperExceptionPage();
    // app.UseMigrationsEndPoint(); // Optional DB migration helper endpoint
}
else // Use Production options OUTSIDE Development
{
    app.UseExceptionHandler("/Error"); // Ensure Pages/Error.cshtml exists
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection(); // Redirect HTTP to HTTPS
app.UseStaticFiles();      // Serve files from wwwroot

app.UseRouting();          // Enable endpoint routing

// --- Middleware Order ---
// 1. UseIdentityServer handles OIDC/OAuth protocol requests.
//    It internally uses the authentication schemes set up by AddIdentity.
app.UseIdentityServer(); // <<-- RE-ENABLED

// 2. UseAuthorization applies authorization policies after authentication/identity is established.
app.UseAuthorization();

// Map endpoints
app.MapStaticAssets(); // Optional: For new .NET 9 static assets feature
app.MapRazorPages()
   .WithStaticAssets(); // Ensure Razor pages work with static assets

app.Run(); // Start the application