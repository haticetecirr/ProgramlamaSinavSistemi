using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProgLab.Client.Core;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Grading;
using ProgLab.Server.Logging;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Puanlayıcılar: VT-31, VT-32, VT-33. Veritabanı gerektirmez.</summary>
public sealed class GraderTests
{
    private static Question NewQuestion() => new()
    {
        CategoryId = 1,
        Type = QuestionType.Classic,
        Title = "t",
        Text = "m"
    };

    private static ExamQuestion Mcq(decimal points) => new()
    {
        Points = points,
        Question = WithOptions(NewQuestion())
    };

    private static Question WithOptions(Question q)
    {
        q.Type = QuestionType.MultipleChoice;
        q.Options =
        [
            new QuestionOption { Id = 1, Text = "a", OrderNo = 1, IsCorrect = false },
            new QuestionOption { Id = 2, Text = "b", OrderNo = 2, IsCorrect = true }
        ];
        return q;
    }

    private static ExamQuestion Classic(decimal points, params string[] accepted)
    {
        var question = NewQuestion();
        question.AnswerKeys = accepted
            .Select(a => new ClassicAnswerKey { AcceptedText = a, NormalizedText = AnswerNormalizer.Normalize(a) })
            .ToList();
        return new ExamQuestion { Points = points, Question = question };
    }

    /// <summary>VT-31.</summary>
    [Theory]
    [InlineData(2, 7.5)] // doğru: sınavdaki puan
    [InlineData(1, 0)]   // yanlış
    [InlineData(null, 0)] // işaret kaldırılmış
    public void VT31_MultipleChoice(int? selected, double expected)
    {
        var result = new MultipleChoiceGrader().Grade(Mcq(7.5m), new Answer { SelectedOptionId = selected });

        Assert.Equal((decimal)expected, result.Score);
    }

    [Fact]
    public void VT31_MultipleChoice_NoAnswerRow_IsZero()
    {
        Assert.Equal(0m, new MultipleChoiceGrader().Grade(Mcq(10m), null).Score);
    }

    [Theory]
    [InlineData("  Yığın. ", 10)]      // VT-32
    [InlineData("IŞIK", 0)]            // anahtar "yığın"; eşleşmez
    [InlineData("STACK", 10)]          // kabul edilen alternatif
    [InlineData("yigin", 0)]           // Türkçe harf farkı eşleşme sayılmaz
    [InlineData("yığın veri yapısı", 0)] // birebir eşleşme aranır
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("...", 0)]             // noktalama atılınca boş kalır
    [InlineData(null, 0)]
    public void VT32_Classic(string? text, int expected)
    {
        var result = new ClassicGrader().Grade(Classic(10m, "yığın", "stack"), new Answer { TextAnswer = text });

        Assert.Equal(expected, result.Score);
    }

    /// <summary>VT-33 (onaylanan örnek: "IŞIK").</summary>
    [Fact]
    public void VT33_Classic_UsesTurkishLowercase()
    {
        Assert.Equal(5m, new ClassicGrader().Grade(Classic(5m, "ışık"), new Answer { TextAnswer = "IŞIK" }).Score);
    }

    [Fact]
    public void Classic_NoAnswerRow_IsZero()
    {
        Assert.Equal(0m, new ClassicGrader().Grade(Classic(5m, "ışık"), null).Score);
    }
}

/// <summary>Teslimde ve otomatik teslimde puanlama, elle düzeltme (VT-34): gerçek API ve veritabanı ile.</summary>
public sealed class GradingApiTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly QuestionOptionInput[] Options = [new("Yanlış", false), new("Doğru", true), new("Diğer", false)];

    private sealed record Sitting(ApiClient Api, long SubmissionId, StudentQuestionDto Mcq, StudentQuestionDto Classic)
    {
        public int CorrectOption => Mcq.Options[1].Id;

        public int WrongOption => Mcq.Options[0].Id;
    }

    /// <summary>Şıklı soru bankada 10, sınavda 30 puan; klasik soru 20 puan.</summary>
    private async Task<(int ExamId, int ClassicQuestionId)> PublishAsync(ApiClient instructor, string title, ExamKind kind = ExamKind.Exam)
    {
        var classId = (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var mcq = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.MultipleChoice, $"{title} 1", "Şıklı", 10, Options, null));
        var classic = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, $"{title} 2", "Klasik", 20, null, ["yığın", "stack"]));
        var request = kind == ExamKind.Exam
            ? new SaveExamRequest(title, null, kind, classId, 40, null, null)
            : new SaveExamRequest(title, null, kind, classId, null, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var exam = await instructor.CreateExamAsync(request);
        await instructor.ReplaceExamQuestionsAsync(exam.Id, [new(mcq.Id, 1, 30), new(classic.Id, 2, null)]);
        await instructor.PublishExamAsync(exam.Id);
        return (exam.Id, classic.Id);
    }

    private async Task<Sitting> SitAsync(int examId, int student)
    {
        var api = await TestAccounts.StudentAsync(fixture.Server, student);
        var joined = await api.JoinExamAsync(examId);
        var paper = await api.GetPaperAsync(examId);
        return new Sitting(api, joined.SubmissionId, paper.Questions[0], paper.Questions[1]);
    }

    private async Task<Submission> ReadAsync(long submissionId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Submissions.AsNoTracking().Include(s => s.Answers).SingleAsync(s => s.Id == submissionId);
    }

    private static Task AnswerClassic(Sitting s, string text) =>
        s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, text));

    private static Task AnswerMcq(Sitting s, int option) =>
        s.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId, new(option, null));

    [Fact]
    public async Task Submit_GradesEveryAnswer_WithExamPoints_AndWritesTotal()
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var (examId, _) = await PublishAsync(instructor, "Tam puan");
        var s = await SitAsync(examId, 1);
        await AnswerMcq(s, s.CorrectOption);
        await AnswerClassic(s, "  Yığın. ");
        var before = DateTimeOffset.UtcNow;

        await s.Api.SubmitAsync(s.SubmissionId);

        var stored = await ReadAsync(s.SubmissionId);
        Assert.Equal(50m, stored.TotalScore); // Bankadaki 10 değil, sınavdaki 30.
        Assert.InRange(stored.GradedAt!.Value, before.AddSeconds(-2), DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal(30m, stored.Answers.Single(a => a.ExamQuestionId == s.Mcq.ExamQuestionId).Score);
        Assert.Equal(20m, stored.Answers.Single(a => a.ExamQuestionId == s.Classic.ExamQuestionId).Score);
        Assert.All(stored.Answers, a => Assert.Equal(GradedBy.Auto, a.GradedBy));

        await using var db = fixture.CreateDbContext();
        var log = await db.ActivityLogs.SingleAsync(l => l.EventType == ActivityEvents.Submitted && l.SubmissionId == s.SubmissionId);
        Assert.Contains("\"totalScore\": 50", log.Details);
    }

    [Fact]
    public async Task Submit_WrongAndEmpty_AreZero_NeverNegative()
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var (examId, _) = await PublishAsync(instructor, "Sıfır");
        var wrong = await SitAsync(examId, 2);
        var empty = await SitAsync(examId, 3);
        await AnswerMcq(wrong, wrong.WrongOption);
        await AnswerClassic(wrong, "kuyruk");

        await wrong.Api.SubmitAsync(wrong.SubmissionId);
        await empty.Api.SubmitAsync(empty.SubmissionId);

        Assert.Equal(0m, (await ReadAsync(wrong.SubmissionId)).TotalScore);
        var stored = await ReadAsync(empty.SubmissionId);
        Assert.Equal(0m, stored.TotalScore);
        Assert.NotNull(stored.GradedAt);
    }

    /// <summary>Yayından sonra banka değişse de sınav kilitli sürümün anahtarıyla puanlanır.</summary>
    [Fact]
    public async Task EditedQuestion_DoesNotAffectPublishedExamGrading()
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var (examId, classicId) = await PublishAsync(instructor, "Kilit");
        var s = await SitAsync(examId, 4);
        await AnswerClassic(s, "yığın");
        var category = (await instructor.GetCategoriesAsync())[0].Id;

        await instructor.UpdateQuestionAsync(classicId, new SaveQuestionRequest(category, QuestionType.Classic, "Kilit 2", "Klasik", 20, null, ["başka cevap"]));
        await s.Api.SubmitAsync(s.SubmissionId);

        Assert.Equal(20m, (await ReadAsync(s.SubmissionId)).TotalScore);
    }

    [Fact]
    public async Task ManualEnd_GradesAutoSubmitted_AndKeepsEarlierSubmissionScore()
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var (examId, _) = await PublishAsync(instructor, "Otomatik teslim");
        var open = await SitAsync(examId, 5);
        var submitted = await SitAsync(examId, 6);
        await AnswerMcq(open, open.CorrectOption);
        await AnswerClassic(submitted, "stack");
        await submitted.Api.SubmitAsync(submitted.SubmissionId);

        await instructor.EndExamAsync(examId);

        var stored = await ReadAsync(open.SubmissionId);
        Assert.Equal(SubmissionStatus.AutoSubmitted, stored.Status);
        Assert.Equal(30m, stored.TotalScore);
        Assert.Equal(20m, (await ReadAsync(submitted.SubmissionId)).TotalScore);

        await using var db = fixture.CreateDbContext();
        var log = await db.ActivityLogs.SingleAsync(l => l.EventType == ActivityEvents.AutoSubmitted && l.SubmissionId == open.SubmissionId);
        Assert.Contains("\"totalScore\": 30", log.Details);
    }

    /// <summary>VT-34: Manual olur, toplam güncellenir, loglanır; yeniden puanlama değeri bozmaz.</summary>
    [Fact]
    public async Task VT34_Override_SetsManual_UpdatesTotal_Logs_AndSurvivesRegrade()
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var (examId, _) = await PublishAsync(instructor, "VT34");
        var s = await SitAsync(examId, 7);
        await AnswerMcq(s, s.CorrectOption);
        await AnswerClassic(s, "son giren ilk çıkar yapısı");
        await s.Api.SubmitAsync(s.SubmissionId);
        var stored = await ReadAsync(s.SubmissionId);
        Assert.Equal(30m, stored.TotalScore);
        var answer = stored.Answers.Single(a => a.ExamQuestionId == s.Classic.ExamQuestionId);

        var result = await instructor.OverrideScoreAsync(answer.Id, new(15.5m, "  Anlamca doğru.  "));

        Assert.Equal(15.5m, result.Score);
        Assert.Equal(45.5m, result.TotalScore);
        stored = await ReadAsync(s.SubmissionId);
        Assert.Equal(45.5m, stored.TotalScore);
        var updated = stored.Answers.Single(a => a.Id == answer.Id);
        Assert.Equal(15.5m, updated.Score);
        Assert.Equal(GradedBy.Manual, updated.GradedBy);
        Assert.Equal("Anlamca doğru.", updated.GradedNote);

        await using (var db = fixture.CreateDbContext())
        {
            var log = await db.ActivityLogs.SingleAsync(l => l.EventType == ActivityEvents.ScoreOverridden && l.SubmissionId == s.SubmissionId);
            Assert.Contains($"\"answerId\": {answer.Id}", log.Details);
            Assert.Contains("\"oldScore\": 0", log.Details);
            Assert.Contains("\"newScore\": 15.5", log.Details);
            Assert.Contains("Anlamca doğru.", log.Details);
        }

        // Yeniden puanlama aynı sonucu verir, elle verilen puana dokunmaz.
        using (var scope = fixture.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProgLab.Server.Data.AppDbContext>();
            var grading = scope.ServiceProvider.GetRequiredService<GradingService>();
            var submission = await db.Submissions.SingleAsync(x => x.Id == s.SubmissionId);
            Assert.Equal(45.5m, await grading.GradeSubmissionAsync(submission, default));
            await db.SaveChangesAsync();
        }

        stored = await ReadAsync(s.SubmissionId);
        Assert.Equal(45.5m, stored.TotalScore);
        Assert.Equal(15.5m, stored.Answers.Single(a => a.Id == answer.Id).Score);
        Assert.Equal(GradedBy.Manual, stored.Answers.Single(a => a.Id == answer.Id).GradedBy);

        // İkinci düzeltmede eski puan bir öncekidir; sıfır ve tam puan sınırları geçerlidir.
        Assert.Equal(50m, (await instructor.OverrideScoreAsync(answer.Id, new(20m, null))).TotalScore);
        Assert.Equal(30m, (await instructor.OverrideScoreAsync(answer.Id, new(0m, null))).TotalScore);
        Assert.Null((await ReadAsync(s.SubmissionId)).Answers.Single(a => a.Id == answer.Id).GradedNote);
    }

    [Fact]
    public async Task Override_InvalidRequests_AreRejected_AndChangeNothing()
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var (examId, _) = await PublishAsync(instructor, "Geçersiz düzeltme");
        var s = await SitAsync(examId, 8);
        await AnswerMcq(s, s.CorrectOption);
        await AnswerClassic(s, "yanlış");

        var inProgress = (await ReadAsync(s.SubmissionId)).Answers.Single(a => a.ExamQuestionId == s.Classic.ExamQuestionId);
        var early = await Assert.ThrowsAsync<ApiException>(() => instructor.OverrideScoreAsync(inProgress.Id, new(5m, null)));
        Assert.Equal(ErrorCodes.InvalidState, early.Code);
        Assert.Equal(409, early.StatusCode);

        await s.Api.SubmitAsync(s.SubmissionId);
        var stored = await ReadAsync(s.SubmissionId);
        var classic = stored.Answers.Single(a => a.ExamQuestionId == s.Classic.ExamQuestionId);
        var mcq = stored.Answers.Single(a => a.ExamQuestionId == s.Mcq.ExamQuestionId);

        foreach (var score in new[] { -1m, 20.01m, 5.555m })
        {
            var range = await Assert.ThrowsAsync<ApiException>(() => instructor.OverrideScoreAsync(classic.Id, new(score, null)));
            Assert.Equal(ErrorCodes.ScoreOutOfRange, range.Code);
            Assert.Equal(422, range.StatusCode);
        }

        var notClassic = await Assert.ThrowsAsync<ApiException>(() => instructor.OverrideScoreAsync(mcq.Id, new(0m, null)));
        Assert.Equal(ErrorCodes.NotClassicQuestion, notClassic.Code);
        Assert.Equal(409, notClassic.StatusCode);

        var longNote = await Assert.ThrowsAsync<ApiException>(() => instructor.OverrideScoreAsync(classic.Id, new(5m, new string('n', 301))));
        Assert.Equal(422, longNote.StatusCode);

        var missing = await Assert.ThrowsAsync<ApiException>(() => instructor.OverrideScoreAsync(999_999, new(5m, null)));
        Assert.Equal(ErrorCodes.AnswerNotFound, missing.Code);
        Assert.Equal(404, missing.StatusCode);

        // Öğrenci puan değiştiremez.
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => s.Api.OverrideScoreAsync(classic.Id, new(20m, null)))).StatusCode);

        var after = await ReadAsync(s.SubmissionId);
        Assert.Equal(30m, after.TotalScore);
        Assert.Equal(GradedBy.Auto, after.Answers.Single(a => a.Id == classic.Id).GradedBy);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.ActivityLogs.AnyAsync(l => l.EventType == ActivityEvents.ScoreOverridden && l.SubmissionId == s.SubmissionId));
    }

    /// <summary>Ödevde son teslim geçerlidir; düzenleme başlayınca eski toplam silinir.</summary>
    [Fact]
    public async Task Assignment_Resubmit_Regrades_AndEditClearsTotal()
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var (examId, _) = await PublishAsync(instructor, "Ödev puanı", ExamKind.Assignment);
        var s = await SitAsync(examId, 9);
        await AnswerClassic(s, "yanlış");
        await s.Api.SubmitAsync(s.SubmissionId);
        Assert.Equal(0m, (await ReadAsync(s.SubmissionId)).TotalScore);

        await AnswerClassic(s, "yığın");
        var editing = await ReadAsync(s.SubmissionId);
        Assert.Equal(SubmissionStatus.InProgress, editing.Status);
        Assert.Null(editing.TotalScore);
        Assert.Null(editing.GradedAt);

        await s.Api.SubmitAsync(s.SubmissionId);
        Assert.Equal(20m, (await ReadAsync(s.SubmissionId)).TotalScore);
    }

    /// <summary>Öğrenci cevabı aynı kaldıkça elle verilen puan korunur; cevap değişirse yeniden puanlanır.</summary>
    [Fact]
    public async Task Assignment_ManualScore_SurvivesResubmit_ButNotChangedAnswer()
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var (examId, _) = await PublishAsync(instructor, "Ödev düzeltme", ExamKind.Assignment);
        var s = await SitAsync(examId, 10);
        await AnswerClassic(s, "anlamca doğru");
        await s.Api.SubmitAsync(s.SubmissionId);
        var answerId = (await ReadAsync(s.SubmissionId)).Answers.Single().Id;
        await instructor.OverrideScoreAsync(answerId, new(12m, "kısmi"));

        // Aynı metin yeniden kaydedilip teslim edildi.
        await AnswerClassic(s, "anlamca doğru");
        await s.Api.SubmitAsync(s.SubmissionId);
        var same = await ReadAsync(s.SubmissionId);
        Assert.Equal(12m, same.TotalScore);
        Assert.Equal(GradedBy.Manual, same.Answers.Single().GradedBy);

        // Cevap değişti: eski puan bu cevaba ait değildir.
        await AnswerClassic(s, "başka metin");
        var changed = (await ReadAsync(s.SubmissionId)).Answers.Single();
        Assert.Null(changed.Score);
        Assert.Null(changed.GradedBy);
        Assert.Null(changed.GradedNote);

        await s.Api.SubmitAsync(s.SubmissionId);
        var final = await ReadAsync(s.SubmissionId);
        Assert.Equal(0m, final.TotalScore);
        Assert.Equal(GradedBy.Auto, final.Answers.Single().GradedBy);
    }

    [Fact]
    public async Task InstructorEvent_CarriesTotalScore()
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var (examId, _) = await PublishAsync(instructor, "Olay puanı");
        var tokens = new TokenStore();
        var api = new ApiClient(fixture.Server.CreateClient(), tokens);
        await api.LoginAsync("egitmen", TestAccounts.InstructorPassword, UserRole.Instructor);
        await using var hub = TestHubConnection.Create(fixture.Server, tokens);
        await hub.StartAsync();
        var received = new TaskCompletionSource<SubmissionReceivedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.SubmissionReceived += e => { if (e.ExamId == examId) { received.TrySetResult(e); } };

        var s = await SitAsync(examId, 11);
        await AnswerMcq(s, s.CorrectOption);
        await s.Api.SubmitAsync(s.SubmissionId);

        Assert.Equal(30m, (await received.Task.WaitAsync(TimeSpan.FromSeconds(20))).TotalScore);
    }

    /// <summary>Puan öğrenciye dönmez: sonuçlar eğitmen ekranındadır, cevap anahtarı kâğıtta yoktur.</summary>
    [Fact]
    public async Task StudentResponses_DoNotCarryScoreOrKey()
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var (examId, _) = await PublishAsync(instructor, "Sızıntı");
        var s = await SitAsync(examId, 12);
        await AnswerMcq(s, s.CorrectOption);
        var http = fixture.Server.CreateClient();
        var tokens = await new ApiClient(http).LoginAsync(TestAccounts.StudentNumber(12), TestAccounts.StudentPassword, UserRole.Student);
        http.DefaultRequestHeaders.Authorization = new("Bearer", tokens.Token);

        var submit = await http.PostAsync($"api/student/submissions/{s.SubmissionId}/submit", null);
        var body = await submit.Content.ReadAsStringAsync();

        Assert.True(submit.IsSuccessStatusCode, body);
        Assert.DoesNotContain("score", body, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Yük testinde bulunan hatanın kalıcı denetimi: 100 eş zamanlı istek bağlantı sınırını aşmamalı.</summary>
public sealed class ConnectionPoolTests
{
    private const string Connection = "Host=localhost;Database=x;Username=y;Password=z";

    [Fact]
    public void DefaultPool_IsLimited_BelowPostgresConnectionLimit()
    {
        var result = new Npgsql.NpgsqlConnectionStringBuilder(ProgLab.Server.Data.AppDbContext.LimitPoolSize(Connection));

        Assert.Equal(40, result.MaxPoolSize);
        Assert.Equal("Password=z".Split('=')[1], result.Password);
    }

    [Fact]
    public void SmallerConfiguredPool_IsKept()
    {
        var result = new Npgsql.NpgsqlConnectionStringBuilder(ProgLab.Server.Data.AppDbContext.LimitPoolSize(Connection + ";Maximum Pool Size=10"));

        Assert.Equal(10, result.MaxPoolSize);
    }

    [Fact]
    public void MissingConnectionString_IsLeftAlone()
    {
        Assert.Null(ProgLab.Server.Data.AppDbContext.LimitPoolSize(null));
    }
}
