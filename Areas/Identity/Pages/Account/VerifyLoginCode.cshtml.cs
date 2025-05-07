// File: Rally/Areas/Identity/Pages/Account/VerifyLoginCode.cshtml.cs
#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Linq; // Added for Select
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization; // Keep if any parts of the page might need it (unlikely here)
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services; // Still needed if sending other emails
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Rally.Models;

namespace Rally.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class VerifyLoginCodeModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender; // Keep for potential future use (e.g., 2FA codes)
        private readonly ILogger<VerifyLoginCodeModel> _logger;

         private const string LoginPurpose = "PasswordlessLogin";
        private const string NewUserConfirmationPurpose = "NewUserEmailConfirmation";


        public VerifyLoginCodeModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailSender emailSender, // Keep injected for now
            ILogger<VerifyLoginCodeModel> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }
        public string EmailForDisplay { get; set; }
        public bool IsNewUserScenario { get; set; } 


        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            [Display(Name = "Login Code")]
            [StringLength(10, MinimumLength = 6)] // For 6-digit codes
            public string Code { get; set; }
        }

         public async Task<IActionResult> OnGetAsync(string email, string returnUrl = null)
        {
            if (string.IsNullOrEmpty(email)) { return RedirectToPage("./Login"); }

            Input = new InputModel { Email = email };
            EmailForDisplay = email;
            ReturnUrl = returnUrl ?? Url.Content("~/");

            var user = await _userManager.FindByEmailAsync(email);
            string code;
            string emailSubject;
            string emailBodyIntro;

            if (user != null) // Existing User
            {
                IsNewUserScenario = false;
                code = await _userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, LoginPurpose);
                emailSubject = "Your Rally Login Code";
                emailBodyIntro = $"Enter this code to log in to your Rally account ({email}):";
                _logger.LogInformation("Generated login code for existing user {Email}", email);
            }
            else // New User
            {
                IsNewUserScenario = true;
                // For a new user, we generate a code they will use to confirm their email AND register.
                // We need a temporary user object to generate a token against, as GenerateUserTokenAsync needs it.
                // We won't save this temporary user. The actual user is created on POST after code validation.
                var tempUser = new ApplicationUser { UserName = email, Email = email }; // Create a transient instance
                code = await _userManager.GenerateUserTokenAsync(tempUser, TokenOptions.DefaultPhoneProvider, NewUserConfirmationPurpose);
                emailSubject = "Confirm Your Rally Account Creation";
                emailBodyIntro = $"Enter this code to create and confirm your Rally account for {email}:";
                _logger.LogInformation("Generated confirmation/registration code for new user {Email}", email);
            }

            try
            {
                await _emailSender.SendEmailAsync(Input.Email, emailSubject, $"{emailBodyIntro}<h2>{code}</h2>");
                _logger.LogInformation("Code sent to {Email}", Input.Email);
                TempData["StatusMessage"] = "A code has been sent to your email. Please enter it below.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send code to {Email}", Input.Email);
                TempData["ErrorMessage"] = "Failed to send verification code. Please try again.";
                return RedirectToPage("./Login", new { returnUrl = ReturnUrl }); // Send back to start
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl ?? Url.Content("~/");
            EmailForDisplay = Input.Email;
            // Re-check if user exists to set IsNewUserScenario for potential redisplay on error
            var existingUser = await _userManager.FindByEmailAsync(Input.Email);
            IsNewUserScenario = (existingUser == null);


            if (!ModelState.IsValid) { return Page(); }

            var user = await _userManager.FindByEmailAsync(Input.Email);

            if (user != null) // User Exists - Verify Login Code
            {
                var isValidCode = await _userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, LoginPurpose, Input.Code);
                if (isValidCode)
                {
                    _logger.LogInformation("Login code verified for existing user {UserId}. Logging in.", user.Id);
                    if (!user.EmailConfirmed && _userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        user.EmailConfirmed = true;
                        await _userManager.UpdateAsync(user);
                        _logger.LogInformation("Email auto-confirmed for user {UserId} during passwordless login.", user.Id);
                    }
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    return LocalRedirect(ReturnUrl);
                }
                else
                {
                    _logger.LogWarning("Invalid login code for existing user {Email}. AlertType: {AlertType}", Input.Email, "LoginFailure");
                    ModelState.AddModelError(string.Empty, "Invalid login code.");
                    return Page();
                }
            }
            else // User Does Not Exist - This is a Registration Attempt with the confirmation code
            {
                _logger.LogInformation("Attempting to register new user with email {Email} using provided code.", Input.Email);
                var newUser = new ApplicationUser { UserName = Input.Email, Email = Input.Email };

                // Verify the code against the *new user object* and the *NewUserConfirmationPurpose*
                // This requires the token provider to be able to validate a code against a user
                // that didn't exist when the code was *conceptually* generated (for the email address).
                // The User-Manager's GenerateUserTokenAsync and VerifyUserTokenAsync methods when used with
                // the DefaultPhoneProvider (TOTP) are based on the user's SecurityStamp.
                // When a new user is created, they get a new SecurityStamp.
                // Therefore, a code generated "for an email" cannot be verified against a "new user"
                // unless the generation itself didn't rely on a specific user's stamp.
                // This is a hard part of the passwordless "login or register" flow.

                // SAFER APPROACH: Create user, THEN generate and verify a new code for them.
                var createUserResult = await _userManager.CreateAsync(newUser);
                if (createUserResult.Succeeded)
                {
                    _logger.LogInformation("New user {UserId} created for email {Email}. Now verifying code.", newUser.Id, Input.Email);

                    // Now, verify the code they entered against the *newly created user*
                    // using the NewUserConfirmationPurpose.
                    var isValidCodeForNewUser = await _userManager.VerifyUserTokenAsync(newUser, TokenOptions.DefaultPhoneProvider, NewUserConfirmationPurpose, Input.Code);

                    if (isValidCodeForNewUser)
                    {
                        _logger.LogInformation("Code verified for newly created user {UserId}. Email is confirmed.", newUser.Id);
                        newUser.EmailConfirmed = true; // Mark as confirmed
                        var updateResult = await _userManager.UpdateAsync(newUser);

                        if (updateResult.Succeeded)
                        {
                            await _signInManager.SignInAsync(newUser, isPersistent: false);
                            return LocalRedirect(ReturnUrl);
                        }
                        else
                        {
                             _logger.LogWarning("Failed to update new user {UserId} after email confirmation. Errors: {Errors}", newUser.Id, string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                             ModelState.AddModelError(string.Empty, "Account created, but a final error occurred. Please try logging in.");
                             return Page();
                        }
                    }
                    else
                    {
                        _logger.LogWarning("User {UserId} created, but provided code {Code} was NOT valid for NewUserConfirmationPurpose. AlertType: {AlertType}", newUser.Id, Input.Code, "SuspiciousBehavior");
                        ModelState.AddModelError(string.Empty, "Account created, but the verification code was invalid. A new confirmation might be needed or contact support.");
                        // At this point, the user *exists* but is unconfirmed.
                        // You might want to offer a "resend confirmation email" option here.
                        return Page();
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to create new user {Email}: {Errors}", Input.Email, string.Join(", ", createUserResult.Errors.Select(e => e.Description)));
                    foreach (var error in createUserResult.Errors) { ModelState.AddModelError(string.Empty, error.Description); }
                    return Page();
                }
            }
        }
    }
}