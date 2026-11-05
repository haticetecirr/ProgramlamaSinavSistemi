using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgLab.Server.Data.Entities;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Data.Configurations;

public sealed class ExamConfiguration : IEntityTypeConfiguration<Exam>
{
    public void Configure(EntityTypeBuilder<Exam> b)
    {
        b.ToTable(t =>
        {
            // IS NOT NULL şart: tek başına "duration_minutes > 0" boş süreyi engellemez (NULL > 0 sonucu NULL'dur).
            t.HasCheckConstraint("ck_exams_duration", "kind <> 1 OR (duration_minutes IS NOT NULL AND duration_minutes > 0)");
            t.HasCheckConstraint("ck_exams_time_window", "ends_at IS NULL OR starts_at < ends_at");
            t.HasCheckConstraint("ck_exams_published_has_times", "status = 1 OR (starts_at IS NOT NULL AND ends_at IS NOT NULL)");
        });

        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Status).HasDefaultValue(ExamStatus.Draft);
        // IsRowVersion + uint: Npgsql bunu PostgreSQL'in xmin sistem kolonuna bağlar, yeni kolon oluşturmaz.
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.ClassId, x.Status });
        b.HasIndex(x => new { x.Status, x.EndsAt });

        b.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ExamQuestionConfiguration : IEntityTypeConfiguration<ExamQuestion>
{
    public void Configure(EntityTypeBuilder<ExamQuestion> b)
    {
        b.ToTable(t => t.HasCheckConstraint("ck_exam_questions_points", "points > 0"));

        b.Property(x => x.Points).HasPrecision(6, 2);

        b.HasIndex(x => new { x.ExamId, x.QuestionId }).IsUnique();
        b.HasIndex(x => new { x.ExamId, x.OrderNo }).IsUnique();

        b.HasOne(x => x.Exam).WithMany(e => e.Questions).HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> b)
    {
        b.Property(x => x.SubmitCount).HasDefaultValue(0);
        b.Property(x => x.TotalScore).HasPrecision(7, 2);

        // Bir öğrenciye bir sınavda tek gönderim; yeniden bağlanan aynı satıra döner.
        b.HasIndex(x => new { x.ExamId, x.StudentId }).IsUnique();
        b.HasIndex(x => new { x.ExamId, x.Status });

        b.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AnswerConfiguration : IEntityTypeConfiguration<Answer>
{
    public void Configure(EntityTypeBuilder<Answer> b)
    {
        b.Property(x => x.TextAnswer).HasMaxLength(2000);
        b.Property(x => x.Score).HasPrecision(6, 2);
        b.Property(x => x.GradedNote).HasMaxLength(300);

        // Otomatik kayıt bu indeks üzerinden ON CONFLICT ile upsert yapar.
        b.HasIndex(x => new { x.SubmissionId, x.ExamQuestionId }).IsUnique();

        b.HasOne(x => x.Submission).WithMany(s => s.Answers).HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ExamQuestion).WithMany().HasForeignKey(x => x.ExamQuestionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SelectedOption).WithMany().HasForeignKey(x => x.SelectedOptionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> b)
    {
        b.Property(x => x.OccurredAt).HasDefaultValueSql("now()");
        b.Property(x => x.EventType).HasMaxLength(40);
        b.Property(x => x.Details).HasColumnType("jsonb");

        b.HasIndex(x => new { x.ExamId, x.OccurredAt });
        b.HasIndex(x => new { x.UserId, x.OccurredAt });
        b.HasIndex(x => x.EventType);

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
    }
}
