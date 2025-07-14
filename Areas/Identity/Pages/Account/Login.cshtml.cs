#nullable disable
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rally.Models;
using Microsoft.Extensions.Logging;

namespace Rally.Areas.Identity.Pages.Account
{
    /// <summary>
    /// This Razor Page model handles the v1 Identity Provider's **passwordless login flow**.
    /// Instead of collecting a password, it collects an email address and initiates a one-time
    /// code verification process. This page serves as the entry point for both signing in
    /// an existing user and registering a new user.
    /// </summary>
    public class LoginModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<LoginModel> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoginModel"/> class.
        /// </summary>
        public LoginModel(
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            ILogger<LoginModel> logger)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        /// <summary>
        /// The model that binds to the login form's input field.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        /// The URL to redirect to after a successful login. This is passed from the
        /// Duende IdentityServer middleware when authentication is required.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        /// Defines the data structure for the passwordless login form input.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            /// The user's email address, used as their primary identifier.
            /// </summary>
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        /// <summary>
        /// Handles the GET request for the login page.
        /// </summary>
        /// <param name="returnUrl">The optional return URL.</param>
        public void OnGet(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
        }

        /// <summary>
        /// Handles the POST request when a user submits their email address to start the passwordless flow.
        /// </summary>
        /// <param name="returnUrl">The optional return URL.</param>
        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(Input.Email);
                string codePurpose = "PasswordlessLogin"; // A purpose string to isolate these tokens from others (e.g., password reset).

                // --- Case 1: The user already exists in the system. ---
                if (user != null)
                {
                    _logger.LogInformation("Passwordless login initiated for existing email: {Email}", Input.Email);
                    // Generate a time-sensitive, single-use token for this user.
                    var code = await _userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, codePurpose);
                    try
                    {
                        // Send the generated code to the user's email address.
                        await _emailSender.SendEmailAsync(Input.Email,
                            "Your Rally Login Code",
                            $"Enter this code to log in to Rally: <h2>{code}</h2>");
                        _logger.LogInformation("Login code sent to existing user {Email}", Input.Email);
                    }
                    catch(Exception ex)
                    {
                        // Log the failure but do not reveal to the potential attacker that the email sending failed.
                        // The user will be redirected to the verification page regardless.
                        _logger.LogError(ex, "Failed to send login code to existing user {Email}", Input.Email);
                    }
                }
                // --- Case 2: The email address does not exist. This is a new user registration attempt. ---
                else
                {
                    _logger.LogInformation("Passwordless registration initiated for new email: {Email}", Input.Email);
                    // For a new user, we cannot generate a user-specific token yet because the user record
                    // doesn't exist. Instead of sending an email here, we will redirect them to the
                    // verification page. The logic on that page will handle creating the new user account
                    // and then proceeding with a confirmation step.
                }

                // Regardless of whether the user exists or not, we always redirect to the verification page.
                // This prevents account enumeration attacks, as the UI flow is identical in both cases.
                // The `VerifyLoginCode` page is now responsible for handling the next step.
                return RedirectToPage("./VerifyLoginCode", new { email = Input.Email, returnUrl = ReturnUrl });
            }

            // If model state is invalid, re-display the form.
            return Page();
        }
    }
}