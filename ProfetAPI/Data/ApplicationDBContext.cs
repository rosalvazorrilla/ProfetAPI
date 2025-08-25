// En el archivo Data/ApplicationDbContext.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Models; // ¡Importante! Para que encuentre ApplicationUser y ApplicationRole

namespace ProfetAPI.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        // --- DBSETS PARA TUS ENTIDADES DEL CRM ---
        public DbSet<Customer> Customers { get; set; }
        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<UserTeam> UserTeams { get; set; }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            // Esta línea es importante para que la configuración base de Identity se aplique primero.
            base.OnModelCreating(builder);

            // --- MAPEAMOS LOS NOMBRES DE TABLA DE IDENTITY ---
            // Le decimos a EF Core que use tus nombres de tabla existentes.
            builder.Entity<ApplicationUser>().ToTable("Users");
            builder.Entity<ApplicationRole>().ToTable("Roles");
            builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
            builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
            builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
            builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
            builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");

            // --- CONFIGURAMOS LAS RELACIONES DE TUS ENTIDADES DEL CRM ---

            builder.Entity<ApplicationUser>(b => {
                // Relación 1-a-1 con UserProfiles
                b.HasOne(u => u.UserProfile).WithOne(p => p.User).HasForeignKey<UserProfile>(p => p.UserId);
                // Relación de Jerarquía (reflexiva)
                b.HasMany(u => u.Subordinates).WithOne(u => u.Parent).HasForeignKey(u => u.ParentId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Customer>(b => {
                b.ToTable("Customers");
                // Relación 1-a-muchos con Users
                b.HasMany(c => c.Users).WithOne(u => u.Customer).HasForeignKey(u => u.CustomerId).IsRequired(false);
                // Relación 1-a-muchos con Teams
                b.HasMany(c => c.Teams).WithOne(t => t.Customer).HasForeignKey(t => t.CustomerId).IsRequired(false);
            });

            builder.Entity<Team>().ToTable("Teams");

            // Configuración de la tabla intermedia UserTeams (muchos-a-muchos)
            builder.Entity<UserTeam>(b => {
                b.ToTable("UserTeams");
                b.HasKey(ut => new { ut.UserId, ut.TeamId }); // Llave primaria compuesta
                b.HasOne(ut => ut.User).WithMany(u => u.UserTeams).HasForeignKey(ut => ut.UserId);
                b.HasOne(ut => ut.Team).WithMany(t => t.UserTeams).HasForeignKey(ut => ut.TeamId);
            });
        }
    }
}