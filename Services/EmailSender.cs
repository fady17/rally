using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace Rally.Services
{
    // Class to hold configuration options from User Secrets
    public class SmtpSettings
    {
        public string? Server { get; set; }
        public int Port { get; set; }
        public string? SenderEmail { get; set; }
        public string? SenderName { get; set; } // Optional: Display name for sender
        public string? Username { get; set; } // Usually same as SenderEmail for Outlook
        public string? Password { get; set; } // Your App Password or regular password
    }

    public class EmailSender : IEmailSender
    {
        private readonly ILogger<EmailSender> _logger;
        private readonly SmtpSettings _smtpSettings;

        // Inject configuration options and logger
        public EmailSender(IOptions<SmtpSettings> smtpSettings, ILogger<EmailSender> logger)
        {
            _smtpSettings = smtpSettings.Value; // Get configured settings
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (string.IsNullOrEmpty(_smtpSettings.Server) ||
                string.IsNullOrEmpty(_smtpSettings.SenderEmail) ||
                string.IsNullOrEmpty(_smtpSettings.Username) ||
                string.IsNullOrEmpty(_smtpSettings.Password))
            {
                _logger.LogError("SMTP settings are not fully configured. Email not sent to {Email}.", email);
                // Fallback or throw? For dev, maybe just log is okay.
                // throw new InvalidOperationException("SMTP settings are not fully configured.");
                return; // Exit silently if not configured
            }

            try
            {
                var fromAddress = new MailAddress(_smtpSettings.SenderEmail, _smtpSettings.SenderName ?? _smtpSettings.SenderEmail);
                var toAddress = new MailAddress(email);

                var smtp = new SmtpClient
                {
                    Host = _smtpSettings.Server,
                    Port = _smtpSettings.Port,
                    EnableSsl = true, // Outlook requires SSL/TLS
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(fromAddress.Address, _smtpSettings.Password)
                    // Timeout = 20000 // Optional: Set timeout in milliseconds
                };

                using (var message = new MailMessage(fromAddress, toAddress)
                {
                    Subject = subject,
                    Body = htmlMessage,
                    IsBodyHtml = true
                })
                {
                    _logger.LogInformation("Attempting to send email via SMTP to {Email}...", email);
                    await smtp.SendMailAsync(message);
                    _logger.LogInformation("Email successfully sent to {Email}.", email);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email}. Subject: {Subject}", email, subject);
                // Optional: Re-throw or handle specific exceptions (e.g., SmtpException)
                // throw; // Re-throwing might break user flow (e.g., registration completes but email fails)
            }
        }
    }
}