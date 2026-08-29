using Microsoft.EntityFrameworkCore;
using Premise.Modules.Identity.Access;
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
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<GrantException> GrantExceptions => Set<GrantException>();

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

        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("roles");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(r => r.OrgId).HasColumnName("org_id");
            b.Property(r => r.Name).HasColumnName("name").HasMaxLength(120);
            b.HasIndex(r => new { r.OrgId, r.Name }).IsUnique();
        });

        modelBuilder.Entity<RoleGrant>(b =>
        {
            b.ToTable("role_grants");
            b.HasKey(g => g.Id);
            b.Property(g => g.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(g => g.OrgId).HasColumnName("org_id");
            b.Property(g => g.RoleId).HasColumnName("role_id");
            b.Property(g => g.Domain).HasColumnName("domain").HasMaxLength(60);
            b.Property(g => g.Action).HasColumnName("action").HasMaxLength(60);
            b.HasIndex(g => g.RoleId);
        });

        modelBuilder.Entity<MembershipRole>(b =>
        {
            b.ToTable("membership_roles");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(m => m.OrgId).HasColumnName("org_id");
            b.Property(m => m.MembershipId).HasColumnName("membership_id");
            b.Property(m => m.RoleId).HasColumnName("role_id");
            b.Property(m => m.ScopePath).HasColumnName("scope_path").HasMaxLength(2000);
            b.HasIndex(m => m.MembershipId);
        });

        modelBuilder.Entity<GrantException>(b =>
        {
            b.ToTable("grant_exceptions");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(e => e.OrgId).HasColumnName("org_id");
            b.Property(e => e.UserId).HasColumnName("user_id");
            b.Property(e => e.Domain).HasColumnName("domain").HasMaxLength(60);
            b.Property(e => e.Action).HasColumnName("action").HasMaxLength(60);
            b.Property(e => e.ScopePath).HasColumnName("scope_path").HasMaxLength(2000);
            b.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(500);
            b.Property(e => e.GrantedBy).HasColumnName("granted_by");
            b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.HasIndex(e => new
            {
                e.UserId,
                e.OrgId,
                e.ExpiresAt,
            });
        });
    }
}
