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
                // Future: Could add custom IdentityResource for loyalty info if needed
                // new IdentityResource("loyalty", "Loyalty Program Information", new[] { "loyalty_id", "loyalty_tier" })
            };

        // Defines API scopes (permissions for APIs). Leave empty for now if just doing SSO.
        public static IEnumerable<ApiScope> GetApiScopes() =>
            new List<ApiScope>
            {
                // Example if you had an API later:
                // new ApiScope("rally.api.read", "Read access to Rally API"),
                // new ApiScope("rally.api.write", "Write access to Rally API"),
            };

        // Defines API Resources (grouping API scopes). Leave empty for now.
        public static IEnumerable<ApiResource> GetApiResources() =>
            new List<ApiResource>
            {
                // Example if you had an API later:
                // new ApiResource("urn:rallyapi", "Rally API")
                // {
                //     Scopes = { "rally.api.read", "rally.api.write" }
                // }
            };

        // Defines the client applications (Relying Parties)
        public static IEnumerable<Client> GetClients() =>
            new List<Client>
            {
                // Client for Rally Motors Website (Example)
                new Client
                {
                    ClientId = "rally-motors-web",
                    ClientName = "Rally Motors Website",
                    ClientSecrets = { new Secret("secret_motors".Sha256()) }, // CHANGE IN PRODUCTION! Store securely. Hash the secret.

                    AllowedGrantTypes = GrantTypes.Code, // Use Authorization Code Flow
                    RequirePkce = true, // Always use PKCE for Code flow

                    // Where the user is redirected to after login at the IdP
                    // Use distinct ports for local testing
                    RedirectUris = { "https://localhost:7001/signin-oidc" },

                    // Where the user is redirected to after logout at the IdP
                    PostLogoutRedirectUris = { "https://localhost:7001/signout-callback-oidc" },

                    AllowedScopes = { "openid", "profile", "email" }, // Scopes this client can request

                    AllowOfflineAccess = false, // Set to true if refresh tokens are needed
                    RequireConsent = true, // Show consent screen first time (good practice)
                    // AccessTokenLifetime = 3600, // Default is 1 hour
                },

                   // Client for Hetro Clothing Online Store (Example)
                new Client
                {
                    ClientId = "hetro-clothing-store",
                    ClientName = "Hetro Clothing Store",
                    ClientSecrets = { new Secret("secret_hetro".Sha256()) }, // CHANGE IN PRODUCTION!

                    AllowedGrantTypes = GrantTypes.Code,
                    RequirePkce = true,

                    // --- UPDATE THIS PORT ---
                    RedirectUris = { "https://localhost:7153/signin-oidc" },
                    // --- UPDATE THIS PORT ---
                    PostLogoutRedirectUris = { "https://localhost:7153/signout-callback-oidc" },

                    AllowedScopes = { "openid", "profile", "email" },

                    AllowOfflineAccess = false,
                    RequireConsent = true,
                },

                // Client for Dental Clinic Portal (Example)
                new Client
                {
                    ClientId = "dental-clinic-portal",
                    ClientName = "Dental Clinic Patient Portal",
                    ClientSecrets = { new Secret("secret_dental".Sha256()) }, // CHANGE IN PRODUCTION!

                    AllowedGrantTypes = GrantTypes.Code,
                    RequirePkce = true,

                    RedirectUris = { "https://localhost:7003/signin-oidc" }, // Different port for local testing
                    PostLogoutRedirectUris = { "https://localhost:7003/signout-callback-oidc" },

                    AllowedScopes = { "openid", "profile", "email" },

                    AllowOfflineAccess = false,
                    RequireConsent = true,
                }
                
            };
    }
}