using Duende.IdentityServer.Services; // For IIdentityServerInteractionService
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Rally.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoggedOutModel : PageModel
    {
        private readonly IIdentityServerInteractionService _interactionService;
        private readonly ILogger<LoggedOutModel> _logger;


        public LoggedOutViewModel View { get; set; }

        public LoggedOutModel(IIdentityServerInteractionService interactionService, ILogger<LoggedOutModel> logger)
        {
            _interactionService = interactionService;
            _logger = logger;
            View = new LoggedOutViewModel();
        }

        public async Task<IActionResult> OnGetAsync(string? logoutId)
        {
            View = new LoggedOutViewModel();

            if (User.Identity?.IsAuthenticated == true)
            {
                // If the user is somehow still authenticated, log them out.
                // This shouldn't happen if Logout.cshtml.cs did its job.
                // HttpContext.SignOutAsync()... but _signInManager.SignOutAsync() is better from Logout.cshtml.cs
            }
            
            var logoutRequest = await _interactionService.GetLogoutContextAsync(logoutId);
            _logger.LogInformation("LogoutId received on LoggedOut page: {LogoutId}", logoutId);


            if (logoutRequest != null)
            {
                View.PostLogoutRedirectUri = logoutRequest.PostLogoutRedirectUri;
                View.ClientName = string.IsNullOrEmpty(logoutRequest.ClientName) ? logoutRequest.ClientId : logoutRequest.ClientName;
                View.SignOutIframeUrl = logoutRequest.SignOutIFrameUrl; // For iframe-based SLO with other RPs
            }

            return Page();
        }

        // ViewModel for the LoggedOut page
        public class LoggedOutViewModel
        {
            public string? PostLogoutRedirectUri { get; set; }
            public string? ClientName { get; set; }
            public string? SignOutIframeUrl { get; set; }
            public bool AutomaticRedirectAfterSignOut => false; // Set to true if you want auto redirect from IdP
        }
    }
}

// // Licensed to the .NET Foundation under one or more agreements.
// // The .NET Foundation licenses this file to you under the MIT license.
// #nullable disable

// using System;
// using System.Threading.Tasks;
// using Microsoft.AspNetCore.Authorization;
// using Microsoft.AspNetCore.Identity;
// using Microsoft.AspNetCore.Mvc;
// using Microsoft.AspNetCore.Mvc.RazorPages;
// using Microsoft.Extensions.Logging;
// using Rally.Models;

// namespace Rally.Areas.Identity.Pages.Account
// {
//     public class LogoutModel : PageModel
//     {
//         private readonly SignInManager<ApplicationUser> _signInManager;
//         private readonly ILogger<LogoutModel> _logger;

//         public LogoutModel(SignInManager<ApplicationUser> signInManager, ILogger<LogoutModel> logger)
//         {
//             _signInManager = signInManager;
//             _logger = logger;
//         }

//         public async Task<IActionResult> OnPost(string returnUrl = null)
//         {
//             await _signInManager.SignOutAsync();
//             _logger.LogInformation("User logged out.");
//             if (returnUrl != null)
//             {
//                 return LocalRedirect(returnUrl);
//             }
//             else
//             {
//                 // This needs to be a redirect so that the browser performs a new
//                 // request and the identity for the user gets updated.
//                 return RedirectToPage("./LoggedOut");
//             }
//         }
//     }
// }
