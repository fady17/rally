using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Validation; // For AuthorizationError
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
//using Rally.Pages.Shared; // Use if ScopeViewModel is in Shared
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace Rally.Pages.Consent
{
    [Authorize] // Only authenticated users should see consent
    // [SecurityHeaders] // REMOVED - Implement via middleware later
    public class IndexModel : PageModel
    {
        private readonly IIdentityServerInteractionService _interaction;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            IIdentityServerInteractionService interaction,
            ILogger<IndexModel> logger)
        {
            _interaction = interaction;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new(); // Initialize InputModel

        // Initialize ViewModel to prevent CS8601 on assignment later
        public ViewModel View { get; set; } = new() { IdentityScopes = Enumerable.Empty<ScopeViewModel>(), ApiScopes = Enumerable.Empty<ScopeViewModel>() };

        public async Task<IActionResult> OnGet(string returnUrl)
        {
            var builtViewModel = await BuildViewModelAsync(returnUrl); // Assign to temp variable
            if (builtViewModel == null)
            {
                _logger.LogWarning("Invalid context/returnUrl in Consent OnGet: {ReturnUrl}", returnUrl);
                return RedirectToPage("/Error"); // Or appropriate error handling
            }
            View = builtViewModel; // Assign if valid
            Input = new InputModel { ReturnUrl = returnUrl };
            return Page();
        }

        public async Task<IActionResult> OnPost()
        {
            var result = await ProcessConsent(Input);

            // Use null check on RedirectUri directly
            if (result.RedirectUri != null)
            {
                // Removed IsValidReturnUrlAsync check for simplicity, rely on IS internal checks
                return Redirect(result.RedirectUri); // CS8604 handled by check above
            }

            if (result.HasValidationError)
            {
                ModelState.AddModelError(string.Empty, result.ValidationError ?? "Consent validation error."); // Provide default msg
            }

            if (result.ShowView)
            {
                // If ViewModel is null on error, rebuild it
#pragma warning disable CS8601 // Possible null reference assignment.
                View = result.ViewModel ?? await BuildViewModelAsync(Input?.ReturnUrl, Input); // CS8601 handled by ??
#pragma warning restore CS8601 // Possible null reference assignment.
                if (View == null) // Handle case where returnUrl might be invalid on POST
                {
                     _logger.LogWarning("Could not rebuild ViewModel in Consent OnPost for ReturnUrl: {ReturnUrl}", Input?.ReturnUrl);
                     return RedirectToPage("/Error");
                }
                return Page();
            }

            // Fallback, should not happen often
            _logger.LogError("Unexpected state processing consent for ReturnUrl: {ReturnUrl}", Input?.ReturnUrl);
            return RedirectToPage("/Error");
        }

        // --- Helper Methods ---

        private async Task<ViewModel?> BuildViewModelAsync(string? returnUrl, InputModel? model = null)
        {
            if (returnUrl == null) return null; // Guard clause

            var request = await _interaction.GetAuthorizationContextAsync(returnUrl);
            if (request == null)
            {
                 _logger.LogError("No consent request matching returnUrl: {ReturnUrl}", returnUrl);
                 return null;
            }

            return CreateConsentViewModel(model, returnUrl, request);
        }

        private ViewModel CreateConsentViewModel(InputModel? model, string returnUrl, AuthorizationRequest request)
        {
            // Ensure client is not null - GetAuthorizationContextAsync should guarantee this if request is not null
            var client = request.Client ?? throw new InvalidOperationException($"Client not found for request associated with returnUrl: {returnUrl}");

            var vm = new ViewModel
            {
                ClientName = client.ClientName ?? client.ClientId,
                ClientUrl = client.ClientUri,
                ClientLogoUrl = client.LogoUri,
                AllowRememberConsent = client.AllowRememberConsent,

                IdentityScopes = request.ValidatedResources.Resources.IdentityResources
                                     .Select(x => CreateScopeViewModel(x, model?.ScopesConsented == null || (model.ScopesConsented?.Contains(x.Name) ?? false)))
                                     .ToList(), // Use ToList for concrete type
                ApiScopes = request.ValidatedResources.Resources.ApiScopes
                                .Select(x => CreateScopeViewModel(x, model?.ScopesConsented == null || (model.ScopesConsented?.Contains(x.Name) ?? false)))
                                .ToList() // Use ToList for concrete type
            };
            return vm;
        }

        private ScopeViewModel CreateScopeViewModel(IdentityResource identity, bool check)
        {
            return new ScopeViewModel
            {
                Value = identity.Name,
                DisplayName = identity.DisplayName ?? identity.Name,
                Description = identity.Description,
                Emphasize = identity.Emphasize,
                Required = identity.Required,
                Checked = check || identity.Required,
            };
        }

         // Changed visibility to private as it's only used internally here
         private ScopeViewModel CreateScopeViewModel(ApiScope scope, bool check)
         {
            return new ScopeViewModel
            {
                Value = scope.Name,
                DisplayName = scope.DisplayName ?? scope.Name,
                Description = scope.Description,
                Emphasize = scope.Emphasize,
                Required = scope.Required,
                Checked = check || scope.Required,
            };
         }


        private async Task<ProcessConsentResult> ProcessConsent(InputModel model)
        {
            var result = new ProcessConsentResult();
            ConsentResponse? grantedConsent = null;
            AuthorizationRequest? request = null; // Hold the request context

            // Need the request context for GrantConsentAsync
            if (model?.ReturnUrl != null)
            {
                 request = await _interaction.GetAuthorizationContextAsync(model.ReturnUrl);
                 if (request == null)
                 {
                    result.ValidationError = "Invalid consent request.";
                    result.ShowView = true; // Need to show view with error
                    return result;
                 }
            }
            else
            {
                 result.ValidationError = "Invalid submission.";
                 result.ShowView = true; // Need to show view with error
                 return result;
            }


            if (model.Button == "no") // Denied
            {
                grantedConsent = new ConsentResponse { Error = AuthorizationError.AccessDenied };
            }
            else if (model.Button == "yes") // Granted
            {
                if (model.ScopesConsented != null && model.ScopesConsented.Any())
                {
                    var scopes = model.ScopesConsented.ToList(); // Work with a list

                    // Ensure required scopes are always granted if user says yes overall
                    var requiredScopes = request.ValidatedResources.Resources.IdentityResources.Where(x => x.Required).Select(x => x.Name)
                        .Union(request.ValidatedResources.Resources.ApiScopes.Where(x => x.Required).Select(x => x.Name));
                    scopes.AddRange(requiredScopes); // Add required scopes
                    scopes = scopes.Distinct().ToList(); // Ensure uniqueness

                    grantedConsent = new ConsentResponse
                    {
                        RememberConsent = model.RememberConsent,
                        ScopesValuesConsented = scopes
                    };
                }
                else
                {
                    // If required scopes exist, user *must* consent to them implicitly by clicking Yes.
                    // If no required scopes exist, and user unchecks everything, treat as denial? Or show error?
                    // Current logic requires at least one scope if saying Yes. Let's keep that for now.
                     var hasRequiredScopes = request.ValidatedResources.Resources.IdentityResources.Any(x => x.Required) ||
                                            request.ValidatedResources.Resources.ApiScopes.Any(x => x.Required);
                     if(!hasRequiredScopes)
                     {
                         result.ValidationError = "You must pick at least one permission to allow access.";
                     }
                     else
                     {
                         // If only required scopes existed and user clicked Yes, build consent for required scopes
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
                // GrantConsentAsync expects non-null request
                await _interaction.GrantConsentAsync(request, grantedConsent); // CS8604 handled by earlier check

                result.SetRedirect(model.ReturnUrl); // Use helper method
            }
            else if(result.ValidationError != null) // Check if validation error occurred
            {
                // We need to redisplay the consent UI with the validation error
                result.ShowView = true;
            }
            // If no grant and no error, something is wrong, but the final return handles it.

            return result;
        }

         // Inner class to structure consent processing result
        public class ProcessConsentResult
        {
            // Make properties settable
            public bool IsRedirect { get; private set;}
            public string? RedirectUri { get; private set; }
            public bool ShowView { get; set; } = false;
            public ViewModel? ViewModel { get; set; }
            public bool HasValidationError => ValidationError != null;
            public string? ValidationError { get; set; }

            public void SetRedirect(string? url)
            {
                IsRedirect = true; // Use the setter
                RedirectUri = url;
            }
        }
    }
}