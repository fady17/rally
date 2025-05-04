using Microsoft.AspNetCore.Identity.UI.Services;
using System.Threading.Tasks;

namespace Rally.Services // Use your actual project namespace
{
    public class DummyEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            // Do nothing. Logs could be added here for debugging purposes.
            Console.WriteLine($"---> Email not sent (Dummy Sender): To={email}, Subject={subject}");
            return Task.CompletedTask;
        }
    }
}