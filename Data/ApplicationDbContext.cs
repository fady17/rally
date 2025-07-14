using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Rally.Models; 
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Rally.Data 
{
    /// <summary>
    /// The primary Entity Framework Core database context for the v1 Identity Provider.
    /// This class is responsible for managing the database schema for both ASP.NET Core Identity
    /// users and the ASP.NET Core Data Protection key store.
    /// </summary>
    /// <remarks>
    /// This DbContext inherits from `IdentityDbContext<ApplicationUser>`, which automatically
    /// configures the necessary DbSets for users, roles, and other Identity entities.
    /// It also implements `IDataProtectionKeyContext` to serve as a persistence store for
    /// data protection keys, which is crucial for stable session management in a deployed environment.
    /// </remarks>
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationDbContext"/> class.
        /// </summary>
        /// <param name="options">The options to be used by a DbContext.</param>
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// Represents the collection of data protection keys in the database.
        /// ASP.NET Core's Data Protection system uses this DbSet to persist keys
        /// used for encrypting and decrypting sensitive information like authentication cookies and anti-forgery tokens.
        /// </summary>
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = default!;

        /// <summary>
        /// Configures the schema needed for the identity framework.
        /// </summary>
        /// <param name="builder">The builder being used to construct the model for this context.</param>
        protected override void OnModelCreating(ModelBuilder builder)
        {
            // This is critical. It calls the base `OnModelCreating` method from `IdentityDbContext`,
            // which applies all the default configurations for the ASP.NET Core Identity tables
            // (e.g., table names, keys, indexes, relationships).
            base.OnModelCreating(builder);
        }
    }
}