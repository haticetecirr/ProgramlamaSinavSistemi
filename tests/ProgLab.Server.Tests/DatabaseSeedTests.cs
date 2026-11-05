using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data;
using ProgLab.Server.Grading;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>VT-07: boş veritabanında ilk açılış seed'i oluşturur, ikinci açılış tekrarlamaz.</summary>
public sealed class DatabaseSeedTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    [Fact]
    public async Task FirstStart_CreatesDesignedSeed_AndSecondStartDoesNotRepeatIt()
    {
        var first = await CountAsync();

        Assert.Equal(new SeedCounts(31, 1, 30, 3, 10, 20, 8), first);

        // Aynı şema üzerinde sunucuyu ikinci kez başlat.
        await using (var secondServer = new ProgLabServerFactory(fixture.ConnectionString))
        {
            _ = secondServer.Services;
        }

        Assert.Equal(first, await CountAsync());
    }

    [Fact]
    public async Task Instructor_HasBcryptTemporaryPassword_AndMustChangeIt()
    {
        await using var db = fixture.CreateDbContext();

        var instructor = await db.Users.SingleAsync(u => u.Role == UserRole.Instructor);

        Assert.Equal("egitmen", instructor.Username);
        Assert.True(instructor.MustChangePassword);
        Assert.True(instructor.IsActive);
        Assert.StartsWith("$2a$11$", instructor.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(ProgLabServerFactory.InstructorTemporaryPassword, instructor.PasswordHash));
    }

    [Fact]
    public async Task DemoStudents_MatchDesign_AndHaveNoPassword()
    {
        await using var db = fixture.CreateDbContext();

        var students = await db.Users.Where(u => u.Role == UserRole.Student).OrderBy(u => u.Username).ToListAsync();

        Assert.Equal(30, students.Count);
        Assert.Equal("2026900001", students[0].Username);
        Assert.Equal("2026900030", students[^1].Username);
        Assert.Equal("2026900001@ogr.demo.local", students[0].Email);
        Assert.All(students, s =>
        {
            Assert.Null(s.PasswordHash);
            Assert.Null(s.VerifiedAt);
            Assert.Null(s.ExternalRef);
            Assert.False(s.MustChangePassword);
        });
    }

    [Fact]
    public async Task DemoClass_ContainsAllStudents()
    {
        await using var db = fixture.CreateDbContext();

        var schoolClass = await db.Classes.Include(c => c.Members).SingleAsync();

        Assert.Equal("Prog. Lab. I – A şubesi", schoolClass.Name);
        Assert.Equal("2026-2027 Güz", schoolClass.Term);
        Assert.Equal(30, schoolClass.Members.Count);
    }

    [Fact]
    public async Task DemoQuestions_FiveOfEachType_WithValidAnswers()
    {
        await using var db = fixture.CreateDbContext();

        var questions = await db.Questions.Include(q => q.Options).Include(q => q.AnswerKeys).ToListAsync();

        var multipleChoice = questions.Where(q => q.Type == QuestionType.MultipleChoice).ToList();
        var classic = questions.Where(q => q.Type == QuestionType.Classic).ToList();
        Assert.Equal(5, multipleChoice.Count);
        Assert.Equal(5, classic.Count);
        Assert.DoesNotContain(questions, q => q.Type == QuestionType.Coding);

        Assert.All(multipleChoice, q =>
        {
            Assert.Equal(4, q.Options.Count);
            Assert.Single(q.Options, o => o.IsCorrect);
        });
        Assert.All(classic, q =>
        {
            Assert.NotEmpty(q.AnswerKeys);
            Assert.All(q.AnswerKeys, k => Assert.Equal(AnswerNormalizer.Normalize(k.AcceptedText), k.NormalizedText));
        });
        Assert.All(questions, q =>
        {
            Assert.True(q.IsCurrent);
            Assert.False(q.IsLocked);
            Assert.False(q.IsDeleted);
            Assert.Equal(1, q.VersionNo);
        });
    }

    private async Task<SeedCounts> CountAsync()
    {
        await using AppDbContext db = fixture.CreateDbContext();
        return new SeedCounts(
            await db.Users.CountAsync(),
            await db.Classes.CountAsync(),
            await db.ClassMembers.CountAsync(),
            await db.Categories.CountAsync(),
            await db.Questions.CountAsync(),
            await db.QuestionOptions.CountAsync(),
            await db.ClassicAnswerKeys.CountAsync());
    }

    private sealed record SeedCounts(int Users, int Classes, int Members, int Categories, int Questions, int Options, int AnswerKeys);
}
