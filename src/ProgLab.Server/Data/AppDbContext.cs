using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data.Entities;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Tablo adları bu özelliklerden gelir: Classes -> classes, ClassMembers -> class_members.
    public DbSet<User> Users => Set<User>();
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<ClassMember> ClassMembers => Set<ClassMember>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<ClassicAnswerKey> ClassicAnswerKeys => Set<ClassicAnswerKey>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<Answer> Answers => Set<Answer>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    /// <summary>Sunucu, testler ve migration aracı aynı ayarları kullansın diye tek yerde.</summary>
    public static void Configure(DbContextOptionsBuilder options, string? connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Tüm enum'lar veritabanında smallint olarak saklanır.
        configurationBuilder.Properties<UserRole>().HaveConversion<short>();
        configurationBuilder.Properties<QuestionType>().HaveConversion<short>();
        configurationBuilder.Properties<ExamKind>().HaveConversion<short>();
        configurationBuilder.Properties<ExamStatus>().HaveConversion<short>();
        configurationBuilder.Properties<SubmissionStatus>().HaveConversion<short>();
        configurationBuilder.Properties<GradedBy>().HaveConversion<short>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Anahtarlar: "generated always as identity".
        modelBuilder.UseIdentityAlwaysColumns();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var childQuestionIds = FindChangedChildQuestionIds();
        var lockedIds = childQuestionIds.Count == 0
            ? []
            : Questions.AsNoTracking().Where(q => q.IsLocked && childQuestionIds.Contains(q.Id)).Select(q => q.Id).ToHashSet();

        RejectLockedQuestionChanges(lockedIds);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var childQuestionIds = FindChangedChildQuestionIds();
        var lockedIds = childQuestionIds.Count == 0
            ? []
            : await Questions.AsNoTracking().Where(q => q.IsLocked && childQuestionIds.Contains(q.Id)).Select(q => q.Id).ToHashSetAsync(cancellationToken);

        RejectLockedQuestionChanges(lockedIds);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Seçeneği veya cevap anahtarı değişen soruların kimlikleri.</summary>
    private List<int> FindChangedChildQuestionIds()
    {
        var options = ChangeTracker.Entries<QuestionOption>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity.QuestionId);
        var keys = ChangeTracker.Entries<ClassicAnswerKey>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity.QuestionId);

        return options.Concat(keys).Distinct().ToList();
    }

    /// <summary>
    /// Ek güvence: kilitli sorunun içeriği hiçbir kod yolundan değişemez.
    /// Bir kilitli soruda yalnızca sürüm ve silme işaretleri değişebilir (IsCurrent, IsDeleted, UpdatedAt).
    /// </summary>
    private void RejectLockedQuestionChanges(HashSet<int> lockedChildQuestionIds)
    {
        string[] allowedOnLocked = [nameof(Question.IsCurrent), nameof(Question.IsDeleted), nameof(Question.UpdatedAt)];

        foreach (var entry in ChangeTracker.Entries<Question>())
        {
            var wasLocked = entry.State is EntityState.Modified or EntityState.Deleted
                && entry.Property(q => q.IsLocked).OriginalValue;
            if (!wasLocked)
            {
                continue;
            }

            var changesContent = entry.State == EntityState.Deleted
                || entry.Properties.Any(p => p.IsModified && !allowedOnLocked.Contains(p.Metadata.Name));
            if (changesContent)
            {
                throw new InvalidOperationException($"Kilitli soru değiştirilemez (id: {entry.Entity.Id}). Yeni sürüm oluşturulmalıdır.");
            }
        }

        if (lockedChildQuestionIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Kilitli sorunun seçenekleri veya cevap anahtarları değiştirilemez (id: {string.Join(", ", lockedChildQuestionIds)}).");
        }
    }
}
