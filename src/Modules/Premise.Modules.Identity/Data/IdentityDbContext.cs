using Microsoft.EntityFrameworkCore;
using Premise.Modules.Identity.Users;
using Premise.Platform.Data;
using Premise.Platform.Kernel;

namespace Premise.Modules.Identity.Data;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "identity";

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(b =>
        {
            b.ToTable("users");
            b.HasKey(u => u.Id);
            b.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(u => u.Provider).HasColumnName("provider").HasMaxLength(40);
            b.Property(u => u.Subject).HasColumnName("subject").HasMaxLength(200);
            b.Property(u => u.Email).HasColumnName("email").HasMaxLength(320);
            b.Property(u => u.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(u => u.CreatedAt).HasColumnName("created_at");
            b.HasIndex(u => new { u.Provider, u.Subject }).IsUnique();
            b.HasIndex(u => u.Email);
        });

        modelBuilder.Entity<Membership>(b =>
        {
            b.ToTable("memberships");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(m => m.UserId).HasColumnName("user_id");
            b.Property(m => m.OrgId).HasColumnName("org_id");
            b.Property(m => m.CreatedAt).HasColumnName("created_at");
            b.HasIndex(m => new { m.UserId, m.OrgId }).IsUnique();
            b.HasIndex(m => m.OrgId);
        });
    }
}
