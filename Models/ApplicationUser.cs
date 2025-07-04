using Microsoft.AspNetCore.Identity;

namespace Rally.Models 
{
    /// <summary>
    /// Represents a user in the v1 Logistics and Asset Tracking Identity Provider.
    /// This class inherits from the default `IdentityUser` provided by ASP.NET Core Identity.
    /// In this initial version, no custom properties were added, demonstrating a standard, out-of-the-box user model.
    /// </summary>
    public class ApplicationUser : IdentityUser
    {
        // This class is intentionally empty for v1. It serves as an extension point.
        // Future versions of the system might add properties here, such as:
        // public string? FullName { get; set; }
        // public Guid? DepotId { get; set; } // Foreign key to a logistics depot/warehouse.
    }
}