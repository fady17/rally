// File: Rally/Pages/Index.cshtml.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rally.Models;
using Serilog; // Keep using Serilog

namespace Rally.Pages
{
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(UserManager<ApplicationUser> userManager, ILogger<IndexModel> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        public string? UserEmail { get; set; }

        public async Task OnGetAsync()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    UserEmail = user.Email;

                    // --- FIX: Change Log Level and AlertType ---
                    _logger.LogWarning( // Changed from LogInformation to LogWarning
                        "Authenticated user {UserId} accessed IdP Index page directly. AlertType: {AlertType}",
                        user.Id,
                        "SuspiciousBehavior"); // Changed from "InfoTrace"
                    // --- END FIX ---
                }
                else
                {
                     _logger.LogWarning("Authenticated user could not be found via UserManager on Index page.");
                     UserEmail = User.Identity.Name;
                }
            }
            else
            {
                 // Keep this as Info - unauthenticated access might be less critical
                 _logger.LogInformation("Unauthenticated user accessed IdP Index page directly.");
            }
        }
    }
}