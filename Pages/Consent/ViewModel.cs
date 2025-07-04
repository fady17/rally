using System.Collections.Generic;
// This using is for the ScopeViewModel defined in a separate file within the same namespace.
using Rally.Pages.Consent; 

namespace Rally.Pages.Consent
{
    /// <summary>
    /// Represents the data model used to render the consent page UI.
    /// It contains all the necessary information to display to the user, such as
    /// the client application's details and the permissions (scopes) being requested.
    /// </summary>
    public class ViewModel
    {
        /// <summary>
        /// The name of the client application requesting consent.
        /// </summary>
        public string? ClientName { get; set; }

        /// <summary>
        /// The URL of the client application's homepage.
        /// </summary>
        public string? ClientUrl { get; set; }

        /// <summary>
        /// The URL of the client application's logo, for display on the consent screen.
        /// </summary>
        public string? ClientLogoUrl { get; set; }

        /// <summary>
        /// A flag indicating whether the client application allows the user's consent decision to be remembered.
        /// </summary>
        public bool AllowRememberConsent { get; set; }

        /// <summary>
        /// A collection of identity-related scopes (e.g., profile, email) being requested.
        /// </summary>
        public IEnumerable<ScopeViewModel>? IdentityScopes { get; set; }

        /// <summary>
        /// A collection of API-related scopes (e.g., permissions to access specific resources) being requested.
        /// </summary>
        public IEnumerable<ScopeViewModel>? ApiScopes { get; set; }
    }
}