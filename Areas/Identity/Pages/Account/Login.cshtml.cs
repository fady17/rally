// File: Rally/Areas/Identity/Pages/Account/Login.cshtml.cs
#nullable disable
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rally.Models;
using Microsoft.Extensions.Logging; // Ensure logger is used

namespace Rally.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            ILogger<LoginModel> logger)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public void OnGet(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(Input.Email);
                string codePurpose = "PasswordlessLogin"; // Purpose for this flow

                if (user != null) // User exists
                {
                    _logger.LogInformation("Passwordless login initiated for existing email: {Email}", Input.Email);
                    var code = await _userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, codePurpose);
                    try
                    {
                        await _emailSender.SendEmailAsync(Input.Email,
                            "Your Rally Login Code",
                            $"Enter this code to log in to Rally: <h2>{code}</h2>");
                        _logger.LogInformation("Login code sent to existing user {Email}", Input.Email);
                    }
                    catch(Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send login code to existing user {Email}", Input.Email);
                        // Don't reveal failure, still redirect
                    }
                }
                else // User does NOT exist - this is a new registration attempt
                {
                    _logger.LogInformation("Passwordless registration initiated for new email: {Email}", Input.Email);
                    // We will send a generic "enter this code" email.
                    // The VerifyLoginCode page will create the user if this code is then "verified"
                    // (which is a conceptual verification for a new user, as the code wasn't tied to a specific user yet).
                    // To make this truly work, we need a code that can be verified without a pre-existing user,
                    // or a way for VerifyLoginCodeModel to know this code was for a new user.

                    // For a simple combined flow:
                    // Generate a placeholder/generic code for the email step for new users if the token provider allows.
                    // However, GenerateUserTokenAsync REQUIRES a user.
                    // So, if user is null, we can't generate a user-specific code here.
                    // The email sent will invite them to enter a code, and VerifyLoginCodeModel will handle creation.

                    // A simpler approach is to send a DIFFERENT email for new users,
                    // asking them to proceed to create an account where a code will be presented/sent.
                    // Or, for passwordless, the VerifyLoginCode will create the user.
                    // Let's assume VerifyLoginCode will create if needed, and the code sent is for *that email address*.

                    // We will simulate sending a code, but VerifyLoginCodeModel does the heavy lifting for new users.
                    // For the email, we can still try to send *a* code concept, but it won't be verifiable in the same way
                    // until the user is created in VerifyLoginCode.
                    // Let's send an email just saying to go to the verify page.
                    try
                    {
                        // In a more advanced scenario, you might generate a temporary, non-user-specific code
                        // or a different type of registration token.
                        // For now, we are relying on VerifyLoginCode to do the "does this look like a new user registration attempt?" logic.
                        // A better approach is to generate a code for a NEW user in VerifyLoginCode AFTER creation.

                        // Let's modify VerifyLoginCode to generate a code if user is new.
                        // This page (LoginModel) will just redirect to VerifyLoginCode with the email.
                        _logger.LogInformation("New email {Email} entered. Redirecting to VerifyLoginCode for potential registration.", Input.Email);

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process initiation for new user {Email}", Input.Email);
                    }
                }

                // ALWAYS redirect to VerifyLoginCode, pass the email.
                // VerifyLoginCode will handle:
                // 1. If user exists: Send/Verify code for login.
                // 2. If user doesn't exist: Create user, send/verify *their first confirmation code*.
                return RedirectToPage("./VerifyLoginCode", new { email = Input.Email, returnUrl = ReturnUrl });
            }
            return Page();
        }
    }
}