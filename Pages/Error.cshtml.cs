// File: Rally/Pages/Error.cshtml.cs
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;

namespace Rally.Pages
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [IgnoreAntiforgeryToken]
    public class ErrorModel : PageModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        public string? ErrorMessage { get; set; }
        public int? ErrorStatusCode { get; set; } // <<< RENAMED PROPERTY
        public string? OriginalPath { get; set; }

        private readonly ILogger<ErrorModel> _logger;

        public ErrorModel(ILogger<ErrorModel> logger)
        {
            _logger = logger;
        }

        // Accept parameter with original name 'statusCode'
        public void OnGet(int? statusCode = null)
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            ErrorStatusCode = statusCode; // <<< Assign to RENAMED PROPERTY

            var exceptionHandlerPathFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();

            if (exceptionHandlerPathFeature?.Error != null)
            {
                _logger.LogError(exceptionHandlerPathFeature.Error, "Unhandled exception for path: {Path}", exceptionHandlerPathFeature.Path);
                ErrorMessage = "An unexpected internal error occurred.";
                OriginalPath = exceptionHandlerPathFeature.Path;
                // You might want to set ErrorStatusCode = 500 here explicitly
                if (!ErrorStatusCode.HasValue) ErrorStatusCode = 500;
            }
            else if (ErrorStatusCode.HasValue) // <<< Use RENAMED PROPERTY
            {
                 var statusCodeReExecuteFeature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
                 OriginalPath = statusCodeReExecuteFeature?.OriginalPath ?? HttpContext.Request.Path;

                _logger.LogWarning("Error {StatusCode} occurred for path: {Path}", ErrorStatusCode, OriginalPath); // <<< Use RENAMED PROPERTY

                switch (ErrorStatusCode) // <<< Use RENAMED PROPERTY
                {
                    case 404: ErrorMessage = "Sorry, the page you requested could not be found."; break;
                    case 403: ErrorMessage = "Sorry, you do not have permission to access this page."; break;
                    default: ErrorMessage = $"An error occurred (Status Code: {ErrorStatusCode})."; break; // <<< Use RENAMED PROPERTY
                }
            }
            else
            {
                 ErrorMessage = "An unexpected error occurred.";
                 OriginalPath = HttpContext.Request.Path;
                 _logger.LogError("Error page accessed directly or with unknown error for path: {Path}", OriginalPath);
            }
        }

         public void OnPost(int? statusCode = null)
         {
             OnGet(statusCode);
         }
    }
}