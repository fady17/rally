// File: Rally/Areas/Identity/Pages/Account/VerifyCode.cshtml.cs
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Rally.Models; 
using Serilog; 

namespace Rally.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class VerifyCodeModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<VerifyCodeModel> _logger;

        public VerifyCodeModel(UserManager<ApplicationUser> userManager, ILogger<VerifyCodeModel> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string Email { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            [Display(Name = "Verification Code")]
            [StringLength(10, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
            public string Code { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Email address is required to verify a code.";
                return RedirectToPage("./ForgotPassword");
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                 _logger.LogWarning("VerifyCode GET attempted for non-existent email: {Email}", email);
                 TempData["ErrorMessage"] = "Unable to find user associated with that email.";
                 return RedirectToPage("./ForgotPassword");
            }

            Email = email;
            Input = new InputModel { Email = email };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                Email = Input.Email;
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null)
            {
                _logger.LogWarning("VerifyCode POST attempted for non-existent email: {Email}", Input.Email);
                ModelState.AddModelError(string.Empty, "Invalid request.");
                Email = Input.Email;
                return Page();
            }

            var isValidCode = await _userManager.VerifyUserTokenAsync(
                user,
                TokenOptions.DefaultPhoneProvider,
                UserManager<ApplicationUser>.ResetPasswordTokenPurpose,
                Input.Code);

            if (isValidCode)
            {
                _logger.LogInformation("Password reset code verified successfully for user {UserId}", user.Id);
                return RedirectToPage("./ResetPassword", new { email = Input.Email, code = Input.Code });
            }
            else
            {
                // --- ADD AlertType context here ---
                _logger.LogWarning("Invalid password reset code provided for user {UserId}, email {Email}. AlertType: {AlertType}",
                    user.Id,
                    Input.Email,
                    "SuspiciousBehavior"); // Log as SuspiciousBehavior

                ModelState.AddModelError(string.Empty, "Invalid verification code.");
                Email = Input.Email;
                return Page();
            }
        }
    }
}