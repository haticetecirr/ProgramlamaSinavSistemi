using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Veritabanı kısıtlarını ve kilitli soru korumasını gerçek PostgreSQL üzerinde dener.</summary>
public sealed class DatabaseRuleTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string RestrictViolation = "23001"; // ON DELETE RESTRICT ihlali

    [Fact]
    public async Task SecondCorrectOption_IsRejected_ByPartialUniqueIndex()
    {
        await using var db = fixture.CreateDbContext();
        var question = await NewMultipleChoiceQuestionAsync(db);

        db.QuestionOptions.Add(new QuestionOption { QuestionId = question.Id, OrderNo = 2, Text = "ikinci doğru", IsCorrect = true });

        await AssertViolationAsync(db, UniqueViolation, "ix_question_options_single_correct");
    }

    [Fact]
    public async Task SecondWrongOption_IsAllowed()
    {
        await using var db = fixture.CreateDbContext();
        var question = await NewMultipleChoiceQuestionAsync(db);

        db.QuestionOptions.AddRange(
            new QuestionOption { QuestionId = question.Id, OrderNo = 2, Text = "yanlış 1", IsCorrect = false },
            new QuestionOption { QuestionId = question.Id, OrderNo = 3, Text = "yanlış 2", IsCorrect = false });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SecondSubmission_ForSameExamAndStudent_IsRejected()
    {
        await using var db = fixture.CreateDbContext();
        var exam = await NewDraftExamAsync(db);
        var studentId = await db.Users.Where(u => u.Role == UserRole.Student).Select(u => u.Id).FirstAsync();

        db.Submissions.Add(NewSubmission(exam.Id, studentId));
        await db.SaveChangesAsync();

        db.Submissions.Add(NewSubmission(exam.Id, studentId));

        await AssertViolationAsync(db, UniqueViolation, "ix_submissions_exam_id_student_id");
    }

    [Fact]
    public async Task Email_IsUniqueCaseInsensitively()
    {
        await using var db = fixture.CreateDbContext();

        db.Users.Add(new User { Username = "kopya1", FullName = "Kopya", Role = UserRole.Student, Email = "2026900001@OGR.DEMO.LOCAL" });

        await AssertViolationAsync(db, UniqueViolation, "ix_users_email_lower");
    }

    [Fact]
    public async Task IsActiveFalse_IsReallyStored_AndDefaultIsTrue()
    {
        await using var db = fixture.CreateDbContext();
        var inactive = new User { Username = "pasif1", FullName = "Pasif", Role = UserRole.Student, IsActive = false };
        var active = new User { Username = "aktif1", FullName = "Aktif", Role = UserRole.Student };
        db.Users.AddRange(inactive, active);
        await db.SaveChangesAsync();

        await using var verify = fixture.CreateDbContext();
        Assert.False(await verify.Users.Where(u => u.Id == inactive.Id).Select(u => u.IsActive).SingleAsync());
        Assert.True(await verify.Users.Where(u => u.Id == active.Id).Select(u => u.IsActive).SingleAsync());
    }

    [Fact]
    public async Task SameRootCategoryName_IsRejected_ByNullsNotDistinct()
    {
        await using var db = fixture.CreateDbContext();

        db.Categories.Add(new Category { Name = "C Temelleri" });

        await AssertViolationAsync(db, UniqueViolation, "ix_categories_parent_id_name");
    }

    [Fact]
    public async Task SameCategoryName_UnderDifferentParents_IsAllowed()
    {
        await using var db = fixture.CreateDbContext();
        var parents = await db.Categories.OrderBy(c => c.Id).Take(2).ToListAsync();

        db.Categories.AddRange(
            new Category { Name = "Alt", ParentId = parents[0].Id },
            new Category { Name = "Alt", ParentId = parents[1].Id });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExamWithZeroDuration_IsRejected_ForExamKind()
    {
        await using var db = fixture.CreateDbContext();
        var exam = await BuildExamAsync(db);
        exam.DurationMinutes = 0;
        db.Exams.Add(exam);

        await AssertViolationAsync(db, CheckViolation, "ck_exams_duration");
    }

    [Fact]
    public async Task ExamWithNullDuration_IsRejected_ForExamKind()
    {
        await using var db = fixture.CreateDbContext();
        var exam = await BuildExamAsync(db);
        exam.DurationMinutes = null;
        db.Exams.Add(exam);

        await AssertViolationAsync(db, CheckViolation, "ck_exams_duration");
    }

    [Fact]
    public async Task Assignment_DoesNotNeedDuration()
    {
        await using var db = fixture.CreateDbContext();
        var exam = await BuildExamAsync(db);
        exam.Kind = ExamKind.Assignment;
        exam.DurationMinutes = null;
        db.Exams.Add(exam);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExamEndingBeforeItStarts_IsRejected()
    {
        await using var db = fixture.CreateDbContext();
        var exam = await BuildExamAsync(db);
        exam.StartsAt = DateTimeOffset.UtcNow;
        exam.EndsAt = exam.StartsAt.Value.AddMinutes(-1);
        db.Exams.Add(exam);

        await AssertViolationAsync(db, CheckViolation, "ck_exams_time_window");
    }

    [Fact]
    public async Task PublishedExamWithoutTimes_IsRejected()
    {
        await using var db = fixture.CreateDbContext();
        var exam = await BuildExamAsync(db);
        exam.Status = ExamStatus.Published;
        db.Exams.Add(exam);

        await AssertViolationAsync(db, CheckViolation, "ck_exams_published_has_times");
    }

    [Fact]
    public async Task ZeroPoints_IsRejected()
    {
        await using var db = fixture.CreateDbContext();
        var question = await NewMultipleChoiceQuestionAsync(db);
        question.DefaultPoints = 0;

        await AssertViolationAsync(db, CheckViolation, "ck_questions_default_points");
    }

    [Fact]
    public async Task DeletingStudentWithMembership_IsRejected_ButDeletingClassCascades()
    {
        await using var db = fixture.CreateDbContext();
        var schoolClass = new SchoolClass
        {
            Name = "Silinecek",
            Term = "Test",
            InstructorId = await InstructorIdAsync(db),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var student = new User { Username = "uyegecici", FullName = "Üye", Role = UserRole.Student };
        db.AddRange(schoolClass, student);
        await db.SaveChangesAsync();
        db.ClassMembers.Add(new ClassMember { ClassId = schoolClass.Id, StudentId = student.Id, AddedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await using (var restrict = fixture.CreateDbContext())
        {
            restrict.Users.Attach(new User { Id = student.Id, Username = "x", FullName = "x" }).State = EntityState.Deleted;
            await AssertViolationAsync(restrict, RestrictViolation, null);
        }

        await using (var cascade = fixture.CreateDbContext())
        {
            await cascade.Classes.Where(c => c.Id == schoolClass.Id).ExecuteDeleteAsync();
            Assert.False(await cascade.ClassMembers.AnyAsync(m => m.ClassId == schoolClass.Id));
        }
    }

    [Fact]
    public async Task ConcurrentExamEdit_IsDetected_ByXmin()
    {
        await using var setup = fixture.CreateDbContext();
        var exam = await NewDraftExamAsync(setup);

        await using var window1 = fixture.CreateDbContext();
        await using var window2 = fixture.CreateDbContext();
        var first = await window1.Exams.SingleAsync(e => e.Id == exam.Id);
        var second = await window2.Exams.SingleAsync(e => e.Id == exam.Id);

        first.Title = "Birinci pencere";
        await window1.SaveChangesAsync();

        second.Title = "İkinci pencere";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => window2.SaveChangesAsync());
    }

    [Fact]
    public async Task LockedQuestion_ContentChange_IsRejected()
    {
        await using var db = fixture.CreateDbContext();
        var question = await NewLockedQuestionAsync(db);

        question.Text = "Değiştirilmiş metin";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("Kilitli soru", error.Message);
    }

    [Fact]
    public async Task LockedQuestion_ContentChange_IsRejected_InSyncSaveToo()
    {
        await using var db = fixture.CreateDbContext();
        var question = await NewLockedQuestionAsync(db);

        question.Title = "Değiştirilmiş başlık";

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task LockedQuestion_OptionChanges_AreRejected()
    {
        await using var db = fixture.CreateDbContext();
        var question = await NewLockedQuestionAsync(db);

        db.QuestionOptions.Add(new QuestionOption { QuestionId = question.Id, OrderNo = 9, Text = "yeni", IsCorrect = false });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var option = await db.QuestionOptions.FirstAsync(o => o.QuestionId == question.Id);
        option.Text = "değişti";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LockedQuestion_AnswerKeyChange_IsRejected()
    {
        await using var db = fixture.CreateDbContext();
        var question = await NewLockedQuestionAsync(db);

        db.ClassicAnswerKeys.Add(new ClassicAnswerKey { QuestionId = question.Id, AcceptedText = "x", NormalizedText = "x" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LockedQuestion_HardDelete_IsRejected()
    {
        await using var db = fixture.CreateDbContext();
        var question = await NewLockedQuestionAsync(db);

        db.Questions.Remove(question);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LockedQuestion_SoftDelete_AndVersionFlag_AreAllowed()
    {
        await using var db = fixture.CreateDbContext();
        var question = await NewLockedQuestionAsync(db);

        question.IsDeleted = true;
        question.IsCurrent = false;
        question.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        await using var verify = fixture.CreateDbContext();
        var stored = await verify.Questions.SingleAsync(q => q.Id == question.Id);
        Assert.True(stored.IsDeleted);
        Assert.False(stored.IsCurrent);
    }

    [Fact]
    public async Task UnlockedQuestion_CanBeLocked_AndEdited()
    {
        await using var db = fixture.CreateDbContext();
        var question = await NewMultipleChoiceQuestionAsync(db);

        question.Text = "Kilitlenmeden önce düzenlendi";
        question.IsLocked = true;

        await db.SaveChangesAsync();
    }

    private static async Task AssertViolationAsync(AppDbContext db, string sqlState, string? constraintName)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
        if (constraintName is not null)
        {
            Assert.Equal(constraintName, postgres.ConstraintName);
        }
    }

    private static Task<int> InstructorIdAsync(AppDbContext db) =>
        db.Users.Where(u => u.Role == UserRole.Instructor).Select(u => u.Id).FirstAsync();

    private static async Task<Question> NewMultipleChoiceQuestionAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var question = new Question
        {
            CategoryId = await db.Categories.Select(c => c.Id).FirstAsync(),
            Type = QuestionType.MultipleChoice,
            Title = "Test sorusu",
            Text = "Metin",
            DefaultPoints = 5,
            CreatedBy = await InstructorIdAsync(db),
            CreatedAt = now,
            UpdatedAt = now,
            Options = [new QuestionOption { OrderNo = 1, Text = "doğru", IsCorrect = true }]
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return question;
    }

    private static async Task<Question> NewLockedQuestionAsync(AppDbContext db)
    {
        var question = await NewMultipleChoiceQuestionAsync(db);
        question.IsLocked = true;
        await db.SaveChangesAsync();
        return question;
    }

    private static async Task<Exam> BuildExamAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        return new Exam
        {
            Title = "Test sınavı",
            Kind = ExamKind.Exam,
            ClassId = await db.Classes.Select(c => c.Id).FirstAsync(),
            DurationMinutes = 40,
            CreatedBy = await InstructorIdAsync(db),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static async Task<Exam> NewDraftExamAsync(AppDbContext db)
    {
        var exam = await BuildExamAsync(db);
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        return exam;
    }

    private static Submission NewSubmission(int examId, int studentId) => new()
    {
        ExamId = examId,
        StudentId = studentId,
        Status = SubmissionStatus.InProgress,
        StartedAt = DateTimeOffset.UtcNow
    };
}
