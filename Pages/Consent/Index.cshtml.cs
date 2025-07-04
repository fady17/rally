using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace Rally.Pages.Consent
{
    /// <summary>
    /// This Razor Page model handles the logic for the user consent screen in the v1 Identity Provider.
    /// It is responsible for building the view with the requested permissions and processing the user's
    /// consent decision (grant or deny).
    /// </summary>
    [Authorize] // Ensures that only an authenticated user can access the consent page.
    public class IndexModel : PageModel
    {
        private readonly IIdentityServerInteractionService _interaction;
        private readonly ILogger<IndexModel> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="IndexModel"/> class.
        /// </summary>
        /// <param name="interaction">The Duende IdentityServer interaction service, used to communicate with the core engine.</param>
        /// <param name="logger">The logger for recording consent page operations.</param>
        public IndexModel(
            IIdentityServerInteractionService interaction,
            ILogger<IndexModel> logger)
        {
            _interaction = interaction;
            _logger = logger;
        }

        /// <summary>
        /// Binds to the user's input from the consent form on POST.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; } = new();

        /// <summary>
        /// The view model containing data to be displayed on the consent page.
        /// </summary>
        public ViewModel View { get; set; } = new() { IdentityScopes = Enumerable.Empty<ScopeViewModel>(), ApiScopes = Enumerable.Empty<ScopeViewModel>() };

        /// <summary>
        /// Handles the GET request for the consent page. It fetches the authorization context
        /// and builds the view model for display.
        /// </summary>
        /// <param name="returnUrl">The URL that contains the context of the authorization request.</param>
        public async Task<IActionResult> OnGet(string returnUrl)
        {
            var builtViewModel = await BuildViewModelAsync(returnUrl);
            if (builtViewModel == null)
            {
                _logger.LogWarning("Invalid context/returnUrl in Consent OnGet: {ReturnUrl}", returnUrl);
                return RedirectToPage("/Error");
            }
            View = builtViewModel;
            Input = new InputModel { ReturnUrl = returnUrl };
            return Page();
        }

        /// <summary>
        /// Handles the POST request from the consent form submission.
        /// </summary>
        public async Task<IActionResult> OnPost()
        {
            var result = await ProcessConsent(Input);
            
            // If consent was processed successfully, a redirect URI will be available.
            if (result.RedirectUri != null)
            {
                // Duende IdentityServer performs its own validation on the return URL, so an explicit check is not always needed here.
                return Redirect(result.RedirectUri);
            }
            
            // If there was a validation error, add it to the model state.
            if (result.HasValidationError)
            {
                ModelState.AddModelError(string.Empty, result.ValidationError ?? "Consent validation error.");
            }

            // If the view needs to be re-displayed (e.g., due to an error), rebuild the view model.
            if (result.ShowView)
            {
#pragma warning disable CS8601 // Possible null reference assignment.
                View = result.ViewModel ?? await BuildViewModelAsync(Input?.ReturnUrl, Input);
#pragma warning restore CS8601 // Possible null reference assignment.
                if (View == null)
                {
                     _logger.LogWarning("Could not rebuild ViewModel in Consent OnPost for ReturnUrl: {ReturnUrl}", Input?.ReturnUrl);
                     return RedirectToPage("/Error");
                }
                return Page();
            }

            _logger.LogError("Unexpected state processing consent for ReturnUrl: {ReturnUrl}", Input?.ReturnUrl);
            return RedirectToPage("/Error");
        }

        // --- Helper Methods ---

        /// <summary>
        /// A helper method to build the main <see cref="ViewModel"/> for the consent page.
        /// </summary>
        private async Task<ViewModel?> BuildViewModelAsync(string? returnUrl, InputModel? model = null)
        {
            if (returnUrl == null) return null;

            // Use the interaction service to get the context of the current authorization request.
            var request = await _interaction.GetAuthorizationContextAsync(returnUrl);
            if (request == null)
            {
                 _logger.LogError("No consent request matching returnUrl: {ReturnUrl}", returnUrl);
                 return null;
            }

            return CreateConsentViewModel(model, returnUrl, request);
        }

        /// <summary>
        /// Creates the <see cref="ViewModel"/> instance from the authorization request data.
        /// </summary>
        private ViewModel CreateConsentViewModel(InputModel? model, string returnUrl, AuthorizationRequest request)
        {
            var client = request.Client ?? throw new InvalidOperationException($"Client not found for request associated with returnUrl: {returnUrl}");

            var vm = new ViewModel
            {
                ClientName = client.ClientName ?? client.ClientId,
                ClientUrl = client.ClientUri,
                ClientLogoUrl = client.LogoUri,
                AllowRememberConsent = client.AllowRememberConsent,
                
                // Map the validated identity and API resources to our ScopeViewModel for display.
                // The 'openid' scope is filtered out as it's a protocol requirement and not a user-choosable permission.
                IdentityScopes = request.ValidatedResources.Resources.IdentityResources
                            .Where(x => x.Name != Duende.IdentityServer.IdentityServerConstants.StandardScopes.OpenId)
                            .Select(x => CreateScopeViewModel(x, model?.ScopesConsented == null || (model.ScopesConsented?.Contains(x.Name) ?? false)))
                            .ToList(), 
                ApiScopes = request.ValidatedResources.Resources.ApiScopes
                                .Select(x => CreateScopeViewModel(x, model?.ScopesConsented == null || (model.ScopesConsented?.Contains(x.Name) ?? false)))
                                .ToList() 
            };
            return vm;
        }
        
        /// <summary>
        /// Creates a <see cref="ScopeViewModel"/> from an <see cref="IdentityResource"/>.
        /// </summary>
        private ScopeViewModel CreateScopeViewModel(IdentityResource identity, bool check)
        {
            return new ScopeViewModel { /* ... implementation ... */ };
        }
        
        /// <summary>
        /// Creates a <see cref="ScopeViewModel"/> from an <see cref="ApiScope"/>.
        /// </summary>
        private ScopeViewModel CreateScopeViewModel(ApiScope scope, bool check)
        {
            return new ScopeViewModel { /* ... implementation ... */ };
        }

        /// <summary>
        /// Processes the user's consent decision from the submitted <see cref="InputModel"/>.
        /// </summary>
        private async Task<ProcessConsentResult> ProcessConsent(InputModel model)
        {
            var result = new ProcessConsentResult();
            ConsentResponse? grantedConsent = null;
            AuthorizationRequest? request = null;

            if (model?.ReturnUrl != null)
            {
                 request = await _interaction.GetAuthorizationContextAsync(model.ReturnUrl);
                 if (request == null)
                 {
                    result.ValidationError = "Invalid consent request.";
                    result.ShowView = true;
                    return result;
                 }
            }
            else
            {
                 result.ValidationError = "Invalid submission.";
                 result.ShowView = true;
                 return result;
            }

            // --- Logic for Grant or Deny ---
            if (model.Button == "no") // User denied consent.
            {
                grantedConsent = new ConsentResponse { Error = AuthorizationError.AccessDenied };
            }
            else if (model.Button == "yes") // User granted consent.
            {
                if (model.ScopesConsented != null && model.ScopesConsented.Any())
                {
                    var scopes = model.ScopesConsented.ToList();
                    
                    // Always include any scopes that are marked as 'Required' by the client configuration.
                    var requiredScopes = request.ValidatedResources.Resources.IdentityResources.Where(x => x.Required).Select(x => x.Name)
                        .Union(request.ValidatedResources.Resources.ApiScopes.Where(x => x.Required).Select(x => x.Name));
                    scopes.AddRange(requiredScopes);

                    grantedConsent = new ConsentResponse
                    {
                        RememberConsent = model.RememberConsent,
                        ScopesValuesConsented = scopes.Distinct().ToList()
                    };
                }
                else
                {
                    // Handle the case where the user clicks "Yes" but has not selected any optional scopes.
                    // If there are required scopes, they are implicitly consented to.
                    var hasRequiredScopes = request.ValidatedResources.Resources.IdentityResources.Any(x => x.Required) ||
                                            request.ValidatedResources.Resources.ApiScopes.Any(x => x.Required);
                     if(!hasRequiredScopes)
                     {
                         result.ValidationError = "You must pick at least one permission to allow access.";
                     }
                     else
                     {
                          var requiredScopes = request.ValidatedResources.Resources.IdentityResources.Where(x => x.Required).Select(x => x.Name)
                            .Union(request.ValidatedResources.Resources.ApiScopes.Where(x => x.Required).Select(x => x.Name));
                          grantedConsent = new ConsentResponse
                          {
                                RememberConsent = model.RememberConsent,
                                ScopesValuesConsented = requiredScopes.ToArray()
                          };
                     }
                }
            }
            else
            {
                result.ValidationError = "Invalid submission.";
            }

            if (grantedConsent != null)
            {
                // Use the interaction service to communicate the user's consent decision back to Duende IdentityServer.
                await _interaction.GrantConsentAsync(request, grantedConsent);
                result.SetRedirect(model.ReturnUrl);
            }
            else if(result.ValidationError != null)
            {
                result.ShowView = true;
            }

            return result;
        }

        /// <summary>
        /// An inner class to structure the result of the consent processing logic.
        /// </summary>
        public class ProcessConsentResult
        {
            public bool IsRedirect { get; private set;}
            public string? RedirectUri { get; private set; }
            public bool ShowView { get; set; } = false;
            public ViewModel? ViewModel { get; set; }
            public bool HasValidationError => ValidationError != null;
            public string? ValidationError { get; set; }

            public void SetRedirect(string? url)
            {
                IsRedirect = true;
                RedirectUri = url;
            }
        }
    }
}