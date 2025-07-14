using System.Collections.Generic;

namespace Rally.Pages.Consent
{
    /// <summary>
    /// Represents the data model that binds to the user's input from the consent form.
    /// This captures the user's decision (accept/deny), the specific scopes they consented to,
    /// and their choice about remembering the decision.
    /// </summary>
    public class InputModel
    {
        /// <summary>
        /// The value of the button clicked by the user (e.g., "yes" for accept, "no" for deny).
        /// </summary>
        public string? Button { get; set; }

        /// <summary>
        /// A collection of the names of the scopes that the user explicitly checked and consented to.
        /// </summary>
        public IEnumerable<string>? ScopesConsented { get; set; }
        
        /// <summary>
        /// A flag indicating whether the user wants their consent decision to be stored for future requests.
        /// </summary>
        public bool RememberConsent { get; set; } = true;
        
        /// <summary>
        /// The return URL that holds the context of the original authorization request.
        /// </summary>
        public string? ReturnUrl { get; set; }
        
        /// <summary>
        /// An optional description, primarily used in the Device Authorization Flow.
        /// </summary>
        public string? Description { get; set; }
    }
}