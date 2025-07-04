using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace Rally.Configuration
{
    /// <summary>
    /// This static class contains the in-code configuration for the Duende IdentityServer instance.
    /// It defines all the clients, API resources, and identity resources that the v1 platform supports.
    /// This configuration is typically seeded into the database on application startup in a development environment.
    /// In our v1 narrative, this configures the security for the "Logistics & Asset Tracking" platform.
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// Defines the standard identity-related claims that clients can request.
        /// These scopes control which user information is included in the ID Token and UserInfo endpoint response.
        /// </summary>
        public static IEnumerable<IdentityResource> GetIdentityResources() =>
            new List<IdentityResource>
            {
                // The 'openid' scope is mandatory for OpenID Connect compliance. It provides the 'sub' (subject ID)
                // claim, which is the unique, stable identifier for a user.
                new IdentityResources.OpenId(),
                
                // The 'profile' scope grants access to standard user profile claims like name, family_name, picture, etc.
                new IdentityResources.Profile(),
                
                // The 'email' scope grants access to the 'email' and 'email_verified' claims.
                new IdentityResources.Email(),
            };

        /// <summary>
        /// Defines the custom API scopes. These represent specific permissions for accessing protected resources (APIs).
        /// In our v1 narrative, these are permissions for the "Logistics API".
        /// </summary>
        public static IEnumerable<ApiScope> GetApiScopes() =>
            new List<ApiScope>
            {
                // A scope for legacy or different API modules.
                new ApiScope(
                    name: "carservice.api.access",
                    displayName: "Car Service API Access",
                    userClaims: new List<string> { "name", "email" }
                ),

                // Defines a permission to read public, non-sensitive data from the Logistics API.
                new ApiScope(
                    name: "automotiveservices.api.read_public",
                    displayName: "Read public logistics and fleet data"
                ),
                
                // Defines a higher-privilege permission for authenticated users (e.g., Dispatchers, Drivers)
                // to interact with the Logistics API (e.g., update delivery status, view assigned routes).
                new ApiScope(
                    name: "automotiveservices.api.user.interact",
                    displayName: "Interact with the Logistics Platform (e.g., update routes, view manifests)"
                    )
            };

        /// <summary>
        /// Defines the API Resources. An API Resource is a logical representation of a protected API.
        /// It groups related API Scopes and defines the 'audience' (aud) claim for JWTs.
        /// </summary>
        public static IEnumerable<ApiResource> GetApiResources() =>
            new List<ApiResource>
            {
                // This represents our main "Logistics API". Any access token intended for this API
                // must contain 'urn:automotiveservicesapi' in its 'aud' claim.
                new ApiResource(
                    name: "urn:automotiveservicesapi",
                    displayName: "Logistics API (Main)",
                    // These claims will be automatically included in the access token when a client requests
                    // a scope associated with this resource.
                    userClaims: new List<string> { "role", "name", "email" }
                )
                {
                    // These are the scopes that "belong" to this API. A client must request one or more
                    // of these scopes to be granted an access token for this resource.
                    Scopes = { "automotiveservices.api.user.interact", "automotiveservices.api.read_public" }
                }
            };

        /// <summary>
        /// Defines the client applications that are allowed to request tokens from this Identity Provider.
        /// </summary>
        public static IEnumerable<Client> GetClients() =>
            new List<Client>
            {
                // This client represents the v1 server-side web application (e.g., a Blazor or MVC app).
                // In our narrative, this is the "Logistics Management Portal" for internal use.
                new Client
                {
                    ClientId = "car-service-client",
                    ClientName = "Logistics Management Portal (v1)",
                    ClientSecrets = { new Secret("secret_motors".Sha256()) }, // This client is "confidential" as it can keep a secret.

                    // This client uses the Authorization Code Flow, the standard for interactive web applications.
                    AllowedGrantTypes = GrantTypes.Code,
                    RequirePkce = true, // Enforces Proof Key for Code Exchange for added security.

                    // The URLs where the IdP is allowed to redirect the user after a successful login.
                    RedirectUris = { "https://localhost:7268/signin-oidc" },
                    // The URLs where the IdP is allowed to redirect the user after a successful logout.
                    PostLogoutRedirectUris = { "https://localhost:7268/signout-callback-oidc", "https://localhost:7268/" },

                    // Defines the complete list of scopes this client is allowed to request.
                    AllowedScopes = {
                        IdentityServerConstants.StandardScopes.OpenId,
                        IdentityServerConstants.StandardScopes.Profile,
                        IdentityServerConstants.StandardScopes.Email,
                        "automotiveservices.api.read_public",
                        "automotiveservices.api.user.interact"
                    },

                    // 'false' because this client does not need long-lived sessions via refresh tokens.
                    AllowOfflineAccess = false,
                    // 'true' means the user will be prompted for consent the first time they use the application.
                    RequireConsent = true,
                    AccessTokenLifetime = 3600, // Access tokens are valid for 1 hour.
                    // Ensures user claims are included in the ID Token for easier access by the client application.
                    AlwaysIncludeUserClaimsInIdToken = true,
                },
                
                // This client represents a more modern Single-Page Application (SPA) client, like our Next.js app.
                // In our narrative, this could be a "Mobile Driver App" or a "v2" portal frontend.
                new Client
                {
                    ClientId = "RALLY_MOTORS_GROUP",
                    ClientName = "Logistics Mobile/SPA Client",
                    // Although this client has a secret defined, for a true public client using PKCE,
                    // the secret is not used during the token exchange. This configuration supports both scenarios.
                    ClientSecrets = { new Secret("RALLY_IDP_CLIENT_SECRET_FOR_NEXTJS_APP".Sha256()) },

                    AllowedGrantTypes = GrantTypes.Code, // Also uses the Authorization Code Flow.
                    RequirePkce = true, // PKCE is mandatory for public clients like SPAs.

                    // The specific callback URL used by the next-auth library.
                    RedirectUris = { "http://localhost:3000/api/auth/callback/rallyidp" }, 
                    PostLogoutRedirectUris = { "http://localhost:3000/" },

                    // Allows the JavaScript client to make direct requests to the IdP's discovery endpoint from the browser.
                    AllowedCorsOrigins = { "http://localhost:3000" },

                    // The list of scopes this modern client is allowed to request.
                    AllowedScopes = {
                        IdentityServerConstants.StandardScopes.OpenId,
                        IdentityServerConstants.StandardScopes.Profile,
                        IdentityServerConstants.StandardScopes.Email,
                        // This client is allowed to request refresh tokens for long-lived sessions.
                        IdentityServerConstants.StandardScopes.OfflineAccess, 
                        "automotiveservices.api.read_public",
                        "automotiveservices.api.user.interact"
                    },

                    // 'true' because this client needs refresh tokens to maintain a seamless user session without frequent logins.
                    AllowOfflineAccess = true, 
                    // 'false' could be used for a highly trusted, first-party application where the consent step is skipped.
                    RequireConsent = false, 
                    AccessTokenLifetime = 3600,
                    AlwaysIncludeUserClaimsInIdToken = true, 
                }
            };
    }
}