using System.Collections.Generic;

namespace Rally.Pages.Consent
{
    public class InputModel
    {
        public string? Button { get; set; } // "yes" or "no"
        public IEnumerable<string>? ScopesConsented { get; set; }
        public bool RememberConsent { get; set; } = true; // Option to remember decision
        public string? ReturnUrl { get; set; }
        public string? Description { get; set; } // For device flow
    }
}