using System.Collections.Generic;
using Rally.Pages.Consent; 

namespace Rally.Pages.Consent
{
    public class ViewModel
    {
        public string? ClientName { get; set; }
        public string? ClientUrl { get; set; }
        public string? ClientLogoUrl { get; set; }
        public bool AllowRememberConsent { get; set; }

        public IEnumerable<ScopeViewModel>? IdentityScopes { get; set; } // Use ScopeViewModel defined below/shared
        public IEnumerable<ScopeViewModel>? ApiScopes { get; set; } // Use ScopeViewModel defined below/shared
    }

    
}