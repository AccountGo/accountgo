using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Api.Data
{
    public class ApplicationIdentityDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationIdentityDbContext(DbContextOptions<ApplicationIdentityDbContext> options)
            : base(options)
        {
        }

        public DbSet<RefreshTokenSession> RefreshTokens { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<RefreshTokenSession>(entity =>
            {
                entity.ToTable("RefreshTokens");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.TokenHash);
                entity.HasIndex(e => e.UserId);
            });
        }
    }
}
