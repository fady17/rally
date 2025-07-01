using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace Rally.Configuration
{
    public static class Config
    {
        // Defines standard identity claims (scopes) like 'openid', 'profile', 'email'
        public static IEnumerable<IdentityResource> GetIdentityResources() =>
            new List<IdentityResource>
            {
                new IdentityResources.OpenId(),    // Essential: Provides the 'sub' (subject ID) claim - THE unique user identifier
                new IdentityResources.Profile(),   // Standard profile claims (name, family_name, website, etc.)
                new IdentityResources.Email(),     // Standard 'email' and 'email_verified' claims
                // new IdentityResources.Roles(), 
                // Future: Could add custom IdentityResource for loyalty info 
                // new IdentityResource("loyalty", "Loyalty Program Information", new[] { "loyalty_id", "loyalty_tier" })
            };

        // Defines API scopes (permissions for APIs). Leave empty for now .
        public static IEnumerable<ApiScope> GetApiScopes() =>
            new List<ApiScope>
            {
                
                // --- NEW API SCOPE for Car Service API ---
                new ApiScope(
                    name: "carservice.api.access",
                    displayName: "Car Service API Access",
                   
                    // Optionally, define user claims to be included in access token when this scope is granted
                    userClaims: new List<string> { "name", "email" } // Example claims
                ),
                new ApiScope(
                    name: "automotiveservices.api.read_public",
                    displayName: "Read public automotive service data"
                ),
                new ApiScope(
                    name: "automotiveservices.api.user.interact",
                    displayName: "Interact with automotive services as a user (e.g., bookings, profile)",
                    userClaims: new List<string> { "user_id_custom_claim_if_needed" }
                    )

                };

            

        // Defines API Resources (grouping API scopes). Leave empty for now.
        public static IEnumerable<ApiResource> GetApiResources() =>
            new List<ApiResource>
            {
                
                // --- NEW API RESOURCE for Car Service API ---
                // new ApiResource(
                //     name: "urn:carserviceapi", // This will be the AUDIENCE in the JWT
                //     displayName: "Car Service API"

                // )
                // {
                //     // Define which scopes this API resource allows/protects
                //     Scopes = { "carservice.api.access" }, // Client must request this scope to get a token for this API
                    
                //     // If your API needs to introspect tokens or has its own secrets for other flows (not typical for simple resource server)
                //     // ApiSecrets = { new Secret("car_service_api_secret".Sha256()) }, 
                    
                //     // Define user claims that should be included in the access token for this resource
                //     // These are in addition to claims defined in the ApiScope itself
                //     UserClaims = new List<string> { "role" } // Example: if API needs user's role
                // },
                new ApiResource(
                        name: "urn:automotiveservicesapi", // This will be the AUDIENCE for tokens
                        displayName: "Automotive Services API (Main)",
                        userClaims: new List<string> { "role", "name", "email" } // Claims to include in access token
                    )
                    {
                        Scopes = { "automotiveservices.api.interact", "automotiveservices.api.read_public" } // Link to new scopes
                    }
                    };

        // Defines the client applications (Relying Parties)
              public static IEnumerable<Client> GetClients() =>
            new List<Client>
            {
                // Client for CarService.Client (which is becoming CarService.Api and will also be consumed by Next.js)
                // This client definition is primarily for its MVC parts and OIDC login flow.
                // A *new* client definition might be needed for the Next.js app if its flow is different (e.g., pure SPA, different redirect URIs)
                new Client
                {
                    ClientId = "car-service-client", // Current client ID
                    ClientName = "Rally Car Service Web App", // Updated name
                    ClientSecrets = { new Secret("secret_motors".Sha256()) },

                    AllowedGrantTypes = GrantTypes.Code, // For interactive login
                    RequirePkce = true,

                    RedirectUris = { "https://localhost:7268/signin-oidc" }, // For MVC login
                    PostLogoutRedirectUris = {
                        "https://localhost:7268/signout-callback-oidc",
                        "https://localhost:7268/"
                    },

                    AllowedScopes = {
                        IdentityServerConstants.StandardScopes.OpenId,
                        IdentityServerConstants.StandardScopes.Profile,
                        IdentityServerConstants.StandardScopes.Email,
                        "automotiveservices.api.read_public", // New scope
                        "automotiveservices.api.user.interact"
                        
                        // IdentityServerConstants.StandardScopes.Roles, // If requesting roles
                        //"carservice.api.access" // <<< ADDED: This client can now request access to the API
                    },

                    AllowOfflineAccess = false,
                    RequireConsent = true, // Or false if you trust this first-party client
                    AccessTokenLifetime = 3600, // 1 hour
                    AlwaysIncludeUserClaimsInIdToken = true, 
                    // If this client will be making direct API calls after user login using its access token:
                    // AlwaysIncludeUserClaimsInIdToken = false, // Default, user claims usually in UserInfo or Access Token
                    // UpdateAccessTokenClaimsOnRefresh = true, // If using refresh tokens
                },

new Client
{
    ClientId = "RALLY_MOTORS_GROUP", // Must match RALLY_IDP_CLIENT_ID in .env.local
    ClientName = "Car Service Next.js Frontend",
    ClientSecrets = { new Secret("RALLY_IDP_CLIENT_SECRET_FOR_NEXTJS_APP".Sha256()) }, // Must match RALLY_IDP_CLIENT_SECRET

    AllowedGrantTypes = GrantTypes.Code, // Authorization Code Flow
    RequirePkce = true, // PKCE is essential

    // IMPORTANT: Callback URL used by next-auth
    RedirectUris = { "http://localhost:3000/api/auth/callback/rallyidp" }, 
    // For production, add your production callback URL: "https://your-app.com/api/auth/callback/rallyidp"

    PostLogoutRedirectUris = { "http://localhost:3000/" }, // Where to redirect after IdP logout
    // For production: "https://your-app.com/"

    AllowedCorsOrigins = { "http://localhost:3000" }, // Important if JS makes direct calls to token/userinfo endpoints (next-auth usually handles this server-side)

    AllowedScopes = {
        IdentityServerConstants.StandardScopes.OpenId,
        IdentityServerConstants.StandardScopes.Profile,
        IdentityServerConstants.StandardScopes.Email,
        IdentityServerConstants.StandardScopes.OfflineAccess,
        "automotiveservices.api.read_public", // New scope
        "automotiveservices.api.user.interact"
    },
    

    AllowOfflineAccess = true, 
    RequireConsent = false, 
    AccessTokenLifetime = 3600, // 1 hour
    AlwaysIncludeUserClaimsInIdToken = true, 
}
                
            };
    }
}