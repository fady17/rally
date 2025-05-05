// File: Rally/Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Rally.Models;

namespace Rally.Areas.Identity.Pages.Account
{
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ForgotPasswordModel> _logger;

        public ForgotPasswordModel(UserManager<ApplicationUser> userManager, IEmailSender emailSender, ILogger<ForgotPasswordModel> logger)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(Input.Email);

                // Still generate/send code even if user doesn't exist to prevent enumeration,
                // but only log generation if user was found.
                if (user != null)
                {
                    var code = await _userManager.GenerateUserTokenAsync(
                        user,
                        TokenOptions.DefaultPhoneProvider, // Or your configured provider for short codes
                        UserManager<ApplicationUser>.ResetPasswordTokenPurpose); // Use standard purpose

                    _logger.LogInformation("Generated password reset code for user {UserId}.", user.Id);

                    try
                    {
                        await _emailSender.SendEmailAsync(
                            Input.Email,
                            "Reset Your Rally Password Code", // Subject line clarity
                            $"Use the following 6-digit code to proceed with resetting your Rally account password.<br/><br/>" +
                            $"Your reset code is: <h2>{code}</h2>");

                        _logger.LogInformation("Password reset code email queued for sending to {Email}", Input.Email);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending password reset code email to {Email}", Input.Email);
                        // Proceed to redirect anyway to obscure failure
                    }
                }
                else
                {
                     _logger.LogWarning("Password reset requested for non-existent email: {Email}", Input.Email);
                }

                // --- MODIFIED REDIRECT ---
                // Redirect to the *new* VerifyCode page, passing the email
                // We don't redirect to ForgotPasswordConfirmation anymore in this flow.
                return RedirectToPage("./VerifyCode", new { email = Input.Email });
                // --- END MODIFIED REDIRECT ---
            }

            return Page();
        }
    }
}