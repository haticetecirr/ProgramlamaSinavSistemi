using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgLab.Server.Data.Entities;

namespace ProgLab.Server.Data.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);

        // NULLS NOT DISTINCT: üst kategorisi olmayanlar arasında da aynı ad bir kez kullanılabilir.
        b.HasIndex(x => new { x.ParentId, x.Name }).IsUnique().AreNullsDistinct(false);

        b.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> b)
    {
        b.ToTable(t => t.HasCheckConstraint("ck_questions_default_points", "default_points > 0"));

        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.DefaultPoints).HasPrecision(6, 2);
        b.Property(x => x.VersionNo).HasDefaultValue(1);
        b.Property(x => x.IsCurrent).HasDefaultValue(true).HasSentinel(true);
        b.Property(x => x.IsLocked).HasDefaultValue(false);
        b.Property(x => x.IsDeleted).HasDefaultValue(false);

        b.HasIndex(x => x.CategoryId);
        b.HasIndex(x => x.Type);
        // Bir sürümün en fazla bir sonraki sürümü olur.
        b.HasIndex(x => x.PreviousVersionId).IsUnique();
        // Bankadaki listeleme sorgusu için kısmi indeks.
        b.HasIndex(x => x.CategoryId, "CurrentQuestionsByCategory")
            .HasDatabaseName("ix_questions_category_id_current")
            .HasFilter("is_current AND NOT is_deleted");

        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PreviousVersion).WithOne().HasForeignKey<Question>(x => x.PreviousVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class QuestionOptionConfiguration : IEntityTypeConfiguration<QuestionOption>
{
    public void Configure(EntityTypeBuilder<QuestionOption> b)
    {
        b.Property(x => x.Text).HasMaxLength(500);

        b.HasIndex(x => new { x.QuestionId, x.OrderNo }).IsUnique();
        // Kısmi benzersiz indeks: bir soruda en fazla bir doğru seçenek olabilir.
        b.HasIndex(x => x.QuestionId, "SingleCorrectOption")
            .HasDatabaseName("ix_question_options_single_correct")
            .IsUnique()
            .HasFilter("is_correct");

        b.HasOne(x => x.Question).WithMany(q => q.Options).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ClassicAnswerKeyConfiguration : IEntityTypeConfiguration<ClassicAnswerKey>
{
    public void Configure(EntityTypeBuilder<ClassicAnswerKey> b)
    {
        b.Property(x => x.AcceptedText).HasMaxLength(500);
        b.Property(x => x.NormalizedText).HasMaxLength(500);

        b.HasIndex(x => new { x.QuestionId, x.NormalizedText }).IsUnique();

        b.HasOne(x => x.Question).WithMany(q => q.AnswerKeys).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}
