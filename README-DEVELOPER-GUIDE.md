# Rally IdP - Client Integration Guide

## Overview

The Rally Identity Provider (IdP) uses OpenID Connect (OIDC) to enable Single Sign-On (SSO) for applications across the Rally Group. By integrating with the IdP, your application can delegate user authentication, allowing users to log in with their central Rally account.

This guide primarily covers integrating **ASP.NET Core web applications** using the **Authorization Code Flow with PKCE**.

## Important IdP Endpoints

Your application will interact with the Rally IdP using the following base URL:

*   **Authority URL (Issuer):** `https://localhost:7223` (for local development against the Rally IdP).
    *   *(Note: This URL will change for Staging and Production environments. Obtain the correct URL from the IdP administration team.)*
*   **Discovery Document:** The OIDC discovery document can be found by appending `/.well-known/openid-configuration` to the Authority URL.
    *   Example (Dev): `https://localhost:7223/.well-known/openid-configuration`
    *   *This JSON document provides metadata about the IdP, including other endpoint URLs (authorization, token, userinfo, jwks_uri), supported scopes, and public keys. OIDC client libraries typically fetch this automatically.*

## Client Registration Process

Before your application can use the Rally IdP, it must be registered as a client. Please contact the **Rally IdP Administration Team** ([TODO: Provide Contact Email/Channel]) with the following information:

1.  **Application Name:**
    *   A user-friendly name (e.g., "Hetro Clothing Online Store").
    *   This will be shown to users on the consent screen.
2.  **Desired Client ID:**
    *   A unique string identifier for your application (e.g., `hetro-clothing-web`, `rally-motors-mobile`).
    *   Lowercase, uses hyphens, no spaces. Please suggest one.
3.  **Redirect URIs (Callback URLs):**
    *   The **exact HTTPS URL(s)** where the IdP should redirect the user back to your application after successful authentication.
    *   For ASP.NET Core applications, this is typically: `https://your-app-domain.com/signin-oidc`
    *   **Provide URIs for all environments** (Development, Staging, Production).
        *   Example Development: `https://localhost:7272/signin-oidc` (if your client app runs on port 7272 for HTTPS)
4.  **Post-Logout Redirect URIs:**
    *   The **exact HTTPS URL(s)** where the IdP can redirect the user after they log out from the central IdP session.
    *   This is often your application's home page or a specific logout confirmation page.
    *   Example: `https://your-app-domain.com/` or `https://your-app-domain.com/logged-out`
    *   **Provide URIs for all environments.**
5.  **Required Scopes:**
    *   List the OIDC scopes your application needs. For basic user login and profile information, request:
        *   `openid` (Essential for OIDC - provides the unique user subject ID)
        *   `profile` (Access to standard profile claims like name, family name, etc.)
        *   `email` (Access to email address and `email_verified` status)
    *   *(If your application will need to access Rally Group APIs protected by this IdP in the future, specify any required API scopes here.)*

Upon successful registration, you will receive:

*   Your unique **Client ID**.
*   A **Client Secret**.

**IMPORTANT:** Treat the **Client Secret** like a password. Store it securely (e.g., environment variables, Azure Key Vault, AWS Secrets Manager) and **NEVER** embed it directly in client-side code or commit it to public repositories.

## ASP.NET Core Client Integration Example

This example shows how to configure an ASP.NET Core (MVC or Razor Pages) web application.

### 1. Install NuGet Package

```bash
dotnet add package Microsoft.AspNetCore.Authentication.OpenIdConnect

Markdown
2. Configure Services (Program.cs)
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect; // For OpenIdConnectResponseType

// Inside your Program.cs or Startup.cs

var builder = WebApplication.CreateBuilder(args);

// ... other services (AddControllersWithViews, AddRazorPages, etc.)

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    // Optional: Configure cookie settings
    options.Cookie.Name = "YourApp.AuthCookie"; // Give it a distinct name
})
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.Authority = "https://localhost:7223"; // Rally IdP Development URL

    options.ClientId = "your-application-client-id";     // Provided by IdP Admin
    options.ClientSecret = builder.Configuration["OIDC:ClientSecret"]; // Load from secure config

    options.ResponseType = OpenIdConnectResponseType.Code; // Use Authorization Code flow
    options.ResponseMode = OpenIdConnectResponseMode.FormPost; // Standard for code flow
    options.UsePkce = true; // PKCE is enabled by default and recommended

    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    // Add other scopes if needed, e.g., options.Scope.Add("offline_access"); for refresh tokens

    options.SaveTokens = true; // Store tokens received from IdP

    // For local development against IdP's dev certificate ONLY
    if (builder.Environment.IsDevelopment())
    {
        options.RequireHttpsMetadata = false; // Allows HTTP for IdP metadata endpoint (discovery)
    }

    // Optional: Map claims for better User.Identity experience
    // options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    // {
    //     NameClaimType = "name", // Maps the 'name' claim to User.Identity.Name
    //     RoleClaimType = "role"  // Maps a 'role' claim to User.IsInRole (if roles are sent)
    // };

    options.Events = new OpenIdConnectEvents
    {
        OnRemoteFailure = context =>
        {
            // Handle errors from the IdP (e.g., user denies consent, invalid request)
            context.Response.Redirect($"/Home/Error?message=OIDC_Remote_Failure:{context.Failure?.Message.Split('.').FirstOrDefault()}");
            context.HandleResponse(); // Marks the event as handled
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ...

C#
Note on Client Secret Storage (Client Application):

Development: Use .NET User Secrets:
# In your client application's directory
dotnet user-secrets init
dotnet user-secrets set "OIDC:ClientSecret" "THE_SECRET_PROVIDED_BY_IDP_ADMIN"

Bash
Production: Use environment variables or a secure vault service.
3. Add Middleware (Program.cs)
Ensure authentication and authorization middleware are added in the correct order:

var app = builder.Build();

// ...
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication(); // MUST be before UseAuthorization
app.UseAuthorization();

app.MapRazorPages(); // Or app.MapControllerRoute(...)
// ...
app.Run();

C#
4. Triggering Login
Protect your application's pages or controller actions using the [Authorize] attribute. When an unauthenticated user tries to access a protected resource, the OIDC middleware will automatically challenge them and redirect to the Rally IdP.

To create an explicit "Login" link/button:

Point it to an action in your AccountController (or similar).
The action should return a challenge:
// Example in an AccountController.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

public class AccountController : Controller
{
    [HttpGet] // Can be GET for a simple login link
    public IActionResult Login(string returnUrl = "/")
    {
        if (User.Identity?.IsAuthenticated ?? false)
        {
            return LocalRedirect(returnUrl);
        }
        // Issue a challenge to the OpenIdConnect scheme
        return Challenge(new AuthenticationProperties { RedirectUri = returnUrl },
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}

C#
5. Implementing Logout
Logout requires signing out of both the local application cookie and the OIDC session with the IdP.

// Example in an AccountController.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

public class AccountController : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken] // Good practice for POST actions
    public async Task<IActionResult> Logout()
    {
        // Properties to pass to the OIDC middleware during sign-out
        var props = new AuthenticationProperties
        {
            // URL to redirect to in your client app AFTER IdP logout is complete
            // This MUST be one of the PostLogoutRedirectUris registered for your client in the IdP
            RedirectUri = Url.Action("Index", "Home", values: null, protocol: Request.Scheme)
        };

        // Sign out of the local cookie scheme
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Sign out of the OIDC scheme (this will redirect to the IdP's end_session_endpoint)
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, props);

        // Typically, SignOutAsync for OIDC handles the redirect. This return might not be hit if successful.
        // If it IS hit, it means only local logout happened, so explicitly redirect.
        return RedirectToAction("Index", "Home");
    }
}

C#
Accessing User Information (Claims)
After successful authentication, user claims provided by the Rally IdP are available via the User property (HttpContext.User) in your controllers and Razor Pages.

// Example:
var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier); // The 'sub' (subject ID)
var email = User.FindFirstValue(System.Security.Claims.ClaimTypes.Email);
var name = User.FindFirstValue(System.Security.Claims.ClaimTypes.Name);
var emailVerified = bool.TryParse(User.FindFirstValue("email_verified"), out var ev) && ev;

// To iterate through all claims:
// foreach (var claim in User.Claims)
// {
//     Console.WriteLine($"Claim Type: {claim.Type}, Claim Value: {claim.Value}");
// }

C#
If you configured options.SaveTokens = true; in your OIDC settings, you can also retrieve the raw tokens:

using Microsoft.AspNetCore.Authentication; // For GetTokenAsync

// ...
string idToken = await HttpContext.GetTokenAsync("id_token");
string accessToken = await HttpContext.GetTokenAsync("access_token");
// string refreshToken = await HttpContext.GetTokenAsync("refresh_token"); // If offline_access was requested & granted

C#
Support
For questions or assistance with integrating your application with the Rally IdP, please contact [TODO: Support Team Email/Channel/Documentation Link].

**How to Use this File:**

1.  Save this content as `README-DEVELOPER-GUIDE.md` in the root of your `Rally` IdP project.
2.  Commit it to your Git repository.
3.  When a new brand developer needs to integrate, you can point them to this file.
4.  Keep this guide updated as the IdP evolves (e.g., production URLs, new available scopes, changes to client registration process).

This gives AI agents (and human developers) a clear, structured guide to follow for client integration.