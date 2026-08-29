using Premise.Modules.Tenancy.Organizations;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Premise.Modules.Tenancy.Data;

public sealed class TenancyDbContext(
    DbContextOptions<TenancyDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "tenancy";

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationSetting> OrganizationSettings => Set<OrganizationSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Organization>(b =>
        {
            b.ToTable("organizations");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(o => o.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(o => o.Slug).HasColumnName("slug").HasMaxLength(80);
            b.Property(o => o.Region).HasColumnName("region").HasMaxLength(40);
            b.Property(o => o.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(o => o.CreatedAt).HasColumnName("created_at");
            b.HasIndex(o => o.Slug).IsUnique();
        });

        modelBuilder.Entity<OrganizationSetting>(b =>
        {
            b.ToTable("organization_settings");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(s => s.OrgId).HasColumnName("org_id");
            b.Property(s => s.Key).HasColumnName("key").HasMaxLength(120);
            b.Property(s => s.Value).HasColumnName("value");
            b.Property(s => s.DeletedAt).HasColumnName("deleted_at");
            b.HasIndex(s => new { s.OrgId, s.Key }).IsUnique().HasFilter("deleted_at IS NULL");
        });
    }
}
