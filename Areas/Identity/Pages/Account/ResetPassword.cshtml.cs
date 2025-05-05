// File: Rally/Areas/Identity/Pages/Account/ResetPassword.cshtml.cs
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq; // Added for Select
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Rally.Models;
using Serilog; // Add this for structured logging (though not strictly required for LogWarning)

namespace Rally.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ResetPasswordModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ResetPasswordModel> _logger;

        public ResetPasswordModel(UserManager<ApplicationUser> userManager, ILogger<ResetPasswordModel> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }

            [Required]
            public string Code { get; set; } // Passed hidden from VerifyCode
        }

        public IActionResult OnGet(string code = null, string email = null)
        {
            if (code == null || email == null)
            {
                _logger.LogWarning("ResetPassword OnGet called without code or email.");
                TempData["ErrorMessage"] = "Invalid password reset request. Please start over."; // Friendlier message
                return RedirectToPage("./ForgotPassword");
            }
            else
            {
                Input = new InputModel
                {
                    Code = code, // Assign directly
                    Email = email
                };
                return Page();
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                // Log with Email if available
                _logger.LogWarning("ResetPassword POST failed model validation for email: {Email}", Input?.Email ?? "Unknown");
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null)
            {
                _logger.LogWarning("Password reset POST attempted for non-existent email: {Email}", Input.Email);
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            // --- Reset the password using the Code ---
            var result = await _userManager.ResetPasswordAsync(user, Input.Code, Input.Password);

            if (result.Succeeded)
            {
                _logger.LogInformation("User {UserId} successfully reset password.", user.Id);
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            // --- Add AlertType context for logging failures ---
            _logger.LogWarning("ResetPasswordAsync failed for user {UserId}. Errors: {Errors}. AlertType: {AlertType}",
                user.Id,
                string.Join(",", result.Errors.Select(e => e.Code)),
                "SuspiciousBehavior"); // Add AlertType property

            foreach (var error in result.Errors)
            {
                if (error.Code == "InvalidToken") {
                     ModelState.AddModelError(string.Empty, "Password reset request may have expired or code was invalid. Please start the process again.");
                } else {
                     ModelState.AddModelError(string.Empty, error.Description);
                }
            }
            return Page();
        }
    }
}