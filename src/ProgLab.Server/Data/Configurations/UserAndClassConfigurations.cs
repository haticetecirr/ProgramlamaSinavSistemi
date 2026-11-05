using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgLab.Server.Data.Entities;

namespace ProgLab.Server.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.Username).HasMaxLength(32);
        b.Property(x => x.FullName).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.PasswordHash).HasMaxLength(100);
        b.Property(x => x.ExternalRef).HasMaxLength(50);
        b.Property(x => x.MustChangePassword).HasDefaultValue(false);
        // Sentinel: C# değeri true ise veritabanı varsayılanı kullanılır; false açıkça yazılır.
        b.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        b.HasIndex(x => x.Username).IsUnique();
        b.HasIndex(x => x.Role);
        // UNIQUE(lower(email)) EF'in modeliyle ifade edilemez; InitialCreate migration'ında SQL ile eklenir.
    }
}

public sealed class SchoolClassConfiguration : IEntityTypeConfiguration<SchoolClass>
{
    public void Configure(EntityTypeBuilder<SchoolClass> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Term).HasMaxLength(20);

        b.HasIndex(x => new { x.Name, x.Term }).IsUnique();

        b.HasOne(x => x.Instructor).WithMany().HasForeignKey(x => x.InstructorId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ClassMemberConfiguration : IEntityTypeConfiguration<ClassMember>
{
    public void Configure(EntityTypeBuilder<ClassMember> b)
    {
        b.HasKey(x => new { x.ClassId, x.StudentId });

        b.HasIndex(x => x.StudentId);

        b.HasOne(x => x.Class).WithMany(c => c.Members).HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}
