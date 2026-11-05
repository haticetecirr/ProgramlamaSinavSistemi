using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProgLab.Client.Core;
using ProgLab.Server.Logging;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Cevap kaydı ve teslim: VT-21, VT-22, VT-23, VT-25, VT-26, VT-28.</summary>
public sealed class AnswerCollectionApiTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>, IAsyncLifetime
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(20);
    private static readonly QuestionOptionInput[] Options = [new("Yanlış", false), new("Doğru", true), new("Diğer", false)];

    private readonly List<ExamHubClient> _hubs = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var hub in _hubs)
        {
            await hub.DisposeAsync();
        }
    }

    private sealed record Sitting(ApiClient Api, long SubmissionId, int ExamId, int StudentId, StudentQuestionDto Mcq, StudentQuestionDto Classic);

    private Task<ApiClient> InstructorAsync() => TestAccounts.InstructorAsync(fixture.Server);

    private static async Task<int> ClassAIdAsync(ApiClient instructor) =>
        (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;

    private static async Task<InstructorExamDto> PublishAsync(ApiClient instructor, string title, ExamKind kind = ExamKind.Exam)
    {
        var classId = await ClassAIdAsync(instructor);
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var mcq = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.MultipleChoice, $"{title} 1", "Şıklı", 10, Options, null));
        var classic = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, $"{title} 2", "Klasik", 5, null, ["cevap"]));
        var request = kind == ExamKind.Exam
            ? new SaveExamRequest(title, null, kind, classId, 40, null, null)
            : new SaveExamRequest(title, null, kind, classId, null, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var exam = await instructor.CreateExamAsync(request);
        await instructor.ReplaceExamQuestionsAsync(exam.Id, [new(mcq.Id, 1, null), new(classic.Id, 2, null)]);
        return await instructor.PublishExamAsync(exam.Id);
    }

    private async Task<Sitting> SitAsync(int examId, int studentIndex)
    {
        var api = await TestAccounts.StudentAsync(fixture.Server, studentIndex);
        var joined = await api.JoinExamAsync(examId);
        var paper = await api.GetPaperAsync(examId);
        var me = await api.GetMeAsync();
        return new Sitting(api, joined.SubmissionId, examId, me.Id, paper.Questions[0], paper.Questions[1]);
    }

    private async Task<(int? Option, string? Text)> ReadAnswerAsync(long submissionId, int examQuestionId)
    {
        await using var db = fixture.CreateDbContext();
        var answer = await db.Answers.SingleAsync(a => a.SubmissionId == submissionId && a.ExamQuestionId == examQuestionId);
        return (answer.SelectedOptionId, answer.TextAnswer);
    }

    private async Task ExpireExamAsync(int examId)
    {
        await using var db = fixture.CreateDbContext();
        var past = DateTimeOffset.UtcNow.AddSeconds(-5);
        await db.Exams.Where(e => e.Id == examId).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.StartsAt, past.AddMinutes(-40))
            .SetProperty(e => e.EndsAt, past));
    }

    /// <summary>VT-22: son seçim kaydedilir, işaret kaldırılabilir, tekrar göndermek satır çoğaltmaz.</summary>
    [Fact]
    public async Task VT22_SelectionIsUpserted_ChangedAndCleared()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "VT22");
        var s = await SitAsync(exam.Id, 1);
        var option1 = s.Mcq.Options[0].Id;
        var option2 = s.Mcq.Options[1].Id;

        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId, new(option1, null));
        Assert.Equal(option1, (await ReadAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId)).Option);

        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId, new(option2, null));
        Assert.Equal(option2, (await ReadAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId)).Option);

        // Aynısını tekrar göndermek aynı sonucu verir.
        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId, new(option2, null));

        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId, new(null, null));
        Assert.Null((await ReadAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId)).Option);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Answers.CountAsync(a => a.SubmissionId == s.SubmissionId));
        Assert.NotNull((await db.Submissions.SingleAsync(x => x.Id == s.SubmissionId)).LastSavedAt);

        // Kâğıt yeniden açılınca kayıtlı cevap gelir.
        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId, new(option1, null));
        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, "yazdığım"));
        var paper = await s.Api.GetPaperAsync(exam.Id);
        Assert.Equal(option1, paper.Answers.Single(a => a.ExamQuestionId == s.Mcq.ExamQuestionId).SelectedOptionId);
        Assert.Equal("yazdığım", paper.Answers.Single(a => a.ExamQuestionId == s.Classic.ExamQuestionId).TextAnswer);
    }

    [Fact]
    public async Task ParallelSaves_LeaveOneRow_WithAValueSent()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "Paralel kayıt");
        var s = await SitAsync(exam.Id, 2);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, $"değer {i}"))));

        var (_, text) = await ReadAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId);
        Assert.StartsWith("değer ", text);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Answers.CountAsync(a => a.SubmissionId == s.SubmissionId));
    }

    /// <summary>VT-23: sınır 2000 karakter.</summary>
    [Fact]
    public async Task VT23_ClassicAnswerLimit()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "VT23");
        var s = await SitAsync(exam.Id, 3);

        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, new string('a', 2000)));

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, new string('a', 2001))));

        Assert.Equal(ErrorCodes.AnswerTooLong, error.Code);
        Assert.Equal(422, error.StatusCode);
        Assert.Equal(2000, (await ReadAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId)).Text!.Length);
    }

    [Fact]
    public async Task OptionOfAnotherQuestion_IsRejected()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "Yanlış şık");
        var other = await PublishAsync(instructor, "Başka sınav şıkkı");
        var s = await SitAsync(exam.Id, 4);
        var otherPaperStudent = await SitAsync(other.Id, 5);

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            s.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId, new(otherPaperStudent.Mcq.Options[0].Id, null)));

        Assert.Equal(ErrorCodes.OptionNotInQuestion, error.Code);
        Assert.Equal(422, error.StatusCode);
    }

    [Fact]
    public async Task QuestionOfAnotherExam_IsRejected()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "Soru sahipliği");
        var other = await PublishAsync(instructor, "Soru sahipliği 2");
        var s = await SitAsync(exam.Id, 6);
        var o = await SitAsync(other.Id, 7);

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            s.Api.SaveAnswerAsync(s.SubmissionId, o.Mcq.ExamQuestionId, new(null, null)));

        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task AnotherStudentsSubmission_IsForbidden_ForSaveAndSubmit()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "Sahiplik");
        var owner = await SitAsync(exam.Id, 8);
        var other = await SitAsync(exam.Id, 9);

        var save = await Assert.ThrowsAsync<ApiException>(() =>
            other.Api.SaveAnswerAsync(owner.SubmissionId, owner.Mcq.ExamQuestionId, new(null, null)));
        var submit = await Assert.ThrowsAsync<ApiException>(() => other.Api.SubmitAsync(owner.SubmissionId));

        Assert.Equal(ErrorCodes.NotOwner, save.Code);
        Assert.Equal(403, save.StatusCode);
        Assert.Equal(ErrorCodes.NotOwner, submit.Code);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(SubmissionStatus.InProgress, (await db.Submissions.SingleAsync(s => s.Id == owner.SubmissionId)).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => owner.Api.SubmitAsync(999_999))).StatusCode);
    }

    [Fact]
    public async Task Submit_RecordsServerTime_CountsAnswers_AndLogsSummary()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "Teslim");
        var s = await SitAsync(exam.Id, 10);
        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId, new(s.Mcq.Options[1].Id, null));
        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, "   ")); // Boş sayılır.
        var before = DateTimeOffset.UtcNow;

        var result = await s.Api.SubmitAsync(s.SubmissionId);

        Assert.Equal(1, result.AnsweredCount);
        Assert.InRange(result.SubmittedAt, before.AddSeconds(-2), DateTimeOffset.UtcNow.AddSeconds(2));

        await using var db = fixture.CreateDbContext();
        var stored = await db.Submissions.SingleAsync(x => x.Id == s.SubmissionId);
        Assert.Equal(SubmissionStatus.Submitted, stored.Status);
        Assert.Equal(1, stored.SubmitCount);
        Assert.NotNull(stored.SubmittedAt);

        var log = await db.ActivityLogs.SingleAsync(l => l.EventType == ActivityEvents.Submitted && l.SubmissionId == s.SubmissionId);
        Assert.Contains("\"answeredCount\": 1", log.Details);
        Assert.Contains("\"submitNo\": 1", log.Details);
        Assert.Contains("answersSummary", log.Details);
    }

    /// <summary>VT-25: sınavda ikinci teslim yok, teslimden sonra cevap değişmez.</summary>
    [Fact]
    public async Task VT25_ExamCannotBeSubmittedTwice_OrChangedAfterSubmit()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "VT25");
        var s = await SitAsync(exam.Id, 11);
        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, "ilk"));
        await s.Api.SubmitAsync(s.SubmissionId);

        var second = await Assert.ThrowsAsync<ApiException>(() => s.Api.SubmitAsync(s.SubmissionId));
        var save = await Assert.ThrowsAsync<ApiException>(() =>
            s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, "sonradan")));

        Assert.Equal(ErrorCodes.AlreadySubmitted, second.Code);
        Assert.Equal(409, second.StatusCode);
        Assert.Equal(ErrorCodes.AlreadySubmitted, save.Code);
        Assert.Equal("ilk", (await ReadAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId)).Text);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, (await db.Submissions.SingleAsync(x => x.Id == s.SubmissionId)).SubmitCount);
        Assert.Single(await db.ActivityLogs.Where(l => l.EventType == ActivityEvents.Submitted && l.SubmissionId == s.SubmissionId).ToListAsync());
        Assert.True(await db.ActivityLogs.AnyAsync(l => l.EventType == ActivityEvents.SubmitRejected && l.SubmissionId == s.SubmissionId));
        Assert.True(await db.ActivityLogs.AnyAsync(l => l.EventType == ActivityEvents.SaveRejected && l.SubmissionId == s.SubmissionId));
    }

    /// <summary>VT-26: ödevde bitişten önce ikinci teslim; her teslim ayrı loglanır.</summary>
    [Fact]
    public async Task VT26_AssignmentCanBeResubmitted_AndEachSubmitIsLogged()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "VT26", ExamKind.Assignment);
        var s = await SitAsync(exam.Id, 12);
        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, "birinci teslim"));

        await s.Api.SubmitAsync(s.SubmissionId);
        await Task.Delay(20);
        var second = await s.Api.SubmitAsync(s.SubmissionId);

        await using var db = fixture.CreateDbContext();
        var stored = await db.Submissions.SingleAsync(x => x.Id == s.SubmissionId);
        Assert.Equal(2, stored.SubmitCount);
        Assert.InRange((stored.SubmittedAt!.Value - second.SubmittedAt).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(2));

        var logs = await db.ActivityLogs.Where(l => l.EventType == ActivityEvents.Submitted && l.SubmissionId == s.SubmissionId).OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Contains("\"submitNo\": 1", logs[0].Details);
        Assert.Contains("\"submitNo\": 2", logs[1].Details);
        Assert.Contains("birinci teslim", logs[1].Details);
    }

    [Fact]
    public async Task AssignmentEditedAfterSubmit_ReturnsToInProgress_AndCanBeContinuedFromLobby()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "Ödev düzenleme", ExamKind.Assignment);
        var s = await SitAsync(exam.Id, 13);
        await s.Api.SubmitAsync(s.SubmissionId);

        var submittedRow = (await s.Api.GetLobbyAsync()).Single(e => e.Id == exam.Id);
        Assert.Equal(StudentExamState.Submitted, submittedRow.State);
        Assert.True(submittedRow.CanContinue);

        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, "sonradan düzenlendi"));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(SubmissionStatus.InProgress, (await db.Submissions.SingleAsync(x => x.Id == s.SubmissionId)).Status);
        Assert.Equal(StudentExamState.Open, (await s.Api.GetLobbyAsync()).Single(e => e.Id == exam.Id).State);
    }

    [Fact]
    public async Task SubmittedExam_CannotContinueFromLobby()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "Sınav lobi");
        var s = await SitAsync(exam.Id, 14);
        await s.Api.SubmitAsync(s.SubmissionId);

        var row = (await s.Api.GetLobbyAsync()).Single(e => e.Id == exam.Id);

        Assert.Equal(StudentExamState.Submitted, row.State);
        Assert.False(row.CanContinue);
    }

    /// <summary>VT-28: bitişten sonra cevap da teslim de kabul edilmez, ikisi de loglanır; tolerans yoktur.</summary>
    [Fact]
    public async Task VT28_AfterEnd_SaveAndSubmitAreRejected_AndLogged()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "VT28");
        var s = await SitAsync(exam.Id, 15);
        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, "süre içinde"));
        await ExpireExamAsync(exam.Id);

        var save = await Assert.ThrowsAsync<ApiException>(() =>
            s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, "süre sonrası")));
        var submit = await Assert.ThrowsAsync<ApiException>(() => s.Api.SubmitAsync(s.SubmissionId));

        Assert.Equal(ErrorCodes.ExamClosed, save.Code);
        Assert.Equal(409, save.StatusCode);
        Assert.Equal(ErrorCodes.ExamClosed, submit.Code);
        Assert.Equal("süre içinde", (await ReadAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId)).Text);

        await using var db = fixture.CreateDbContext();
        var save0 = await db.ActivityLogs.SingleAsync(l => l.EventType == ActivityEvents.SaveRejected && l.SubmissionId == s.SubmissionId);
        Assert.Contains(ErrorCodes.ExamClosed, save0.Details);
        Assert.True(await db.ActivityLogs.AnyAsync(l => l.EventType == ActivityEvents.SubmitRejected && l.SubmissionId == s.SubmissionId));
        Assert.Equal(SubmissionStatus.InProgress, (await db.Submissions.SingleAsync(x => x.Id == s.SubmissionId)).Status);
    }

    /// <summary>VT-21: ikinci bilgisayardan katılım engellenmez, loglanır ve eğitmen uyarılır.</summary>
    [Fact]
    public async Task VT21_SecondDevice_IsAllowed_LoggedAndWarned()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "VT21");

        // Eğitmen canlı dinliyor.
        var tokens = new TokenStore();
        var instructorApi = new ApiClient(fixture.Server.CreateClient(), tokens);
        await instructorApi.LoginAsync("egitmen", TestAccounts.InstructorPassword, UserRole.Instructor);
        var hub = TestHubConnection.Create(fixture.Server, tokens);
        _hubs.Add(hub);
        var warning = new TaskCompletionSource<SecondDeviceEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.SecondDeviceWarning += e => warning.TrySetResult(e);
        await hub.StartAsync();

        var login = await new ApiClient(fixture.Server.CreateClient()).FirstLoginAsync(TestAccounts.StudentNumber(16), TestAccounts.StudentEmail(16), TestAccounts.StudentPassword);

        ApiClient FromIp(string ip)
        {
            var http = fixture.Server.CreateClient();
            http.DefaultRequestHeaders.Add(ProgLabServerFactory.TestIpHeader, ip);
            var store = new TokenStore();
            store.Set(login.Token, login.ExpiresAt);
            return new ApiClient(http, store);
        }

        var first = await FromIp("192.168.1.23").JoinExamAsync(exam.Id);

        // Aynı adresten dönüş uyarı üretmez.
        await FromIp("192.168.1.23").JoinExamAsync(exam.Id);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.False(await db.ActivityLogs.AnyAsync(l => l.EventType == ActivityEvents.SecondDevice && l.SubmissionId == first.SubmissionId));
        }

        var second = await FromIp("192.168.1.99").JoinExamAsync(exam.Id);

        Assert.Equal(first.SubmissionId, second.SubmissionId);
        var received = await warning.Task.WaitAsync(EventTimeout);
        Assert.Equal(exam.Id, received.ExamId);
        Assert.Equal("192.168.1.23", received.Ip1);
        Assert.Equal("192.168.1.99", received.Ip2);
        Assert.Equal("Demo Öğrenci 16", received.FullName);

        await using var verify = fixture.CreateDbContext();
        var log = await verify.ActivityLogs.SingleAsync(l => l.EventType == ActivityEvents.SecondDevice && l.SubmissionId == first.SubmissionId);
        Assert.Contains("192.168.1.23", log.Details);
        Assert.Contains("192.168.1.99", log.Details);

        var live = await instructor.GetLiveExamAsync(exam.Id);
        Assert.Contains(live.Warnings, w => w.Contains("Demo Öğrenci 16") && w.Contains("192.168.1.99"));
    }

    [Fact]
    public async Task Instructor_SeesProgressAndSubmissionLive_AndLiveEndpoint()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "Canlı");
        var tokens = new TokenStore();
        var instructorApi = new ApiClient(fixture.Server.CreateClient(), tokens);
        await instructorApi.LoginAsync("egitmen", TestAccounts.InstructorPassword, UserRole.Instructor);
        var hub = TestHubConnection.Create(fixture.Server, tokens);
        _hubs.Add(hub);
        await hub.StartAsync();

        var s = await SitAsync(exam.Id, 17);
        var progress = new TaskCompletionSource<AnswerProgressEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var submitted = new TaskCompletionSource<SubmissionReceivedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.AnswerProgress += e => { if (e.StudentId == s.StudentId && e.AnsweredCount > 0) { progress.TrySetResult(e); } };
        hub.SubmissionReceived += e => { if (e.StudentId == s.StudentId) { submitted.TrySetResult(e); } };

        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId, new(s.Mcq.Options[0].Id, null));
        Assert.Equal(1, (await progress.Task.WaitAsync(EventTimeout)).AnsweredCount);

        var live = await instructor.GetLiveExamAsync(exam.Id);
        Assert.Equal(2, live.TotalQuestions);
        Assert.InRange(live.RemainingSeconds, 1, 40 * 60);
        Assert.True(live.Students.Count >= 30);
        var row = live.Students.Single(x => x.StudentId == s.StudentId);
        Assert.Equal(1, row.AnsweredCount);
        Assert.Equal(SubmissionStatus.InProgress, row.Status);
        Assert.Null(live.Students.First(x => x.StudentId != s.StudentId && x.Username == TestAccounts.StudentNumber(30)).Status); // Katılmadı.

        await s.Api.SubmitAsync(s.SubmissionId);
        Assert.Equal(exam.Id, (await submitted.Task.WaitAsync(EventTimeout)).ExamId);
        Assert.Equal(SubmissionStatus.Submitted, (await instructor.GetLiveExamAsync(exam.Id)).Students.Single(x => x.StudentId == s.StudentId).Status);

        // Öğrenci bu uç noktaya erişemez.
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => s.Api.GetLiveExamAsync(exam.Id))).StatusCode);
    }

    [Fact]
    public async Task ManualEnd_AutoSubmitsOpenSubmissions_AndClosesSaving()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishAsync(instructor, "Elle bitiş");
        var open = await SitAsync(exam.Id, 18);
        var done = await SitAsync(exam.Id, 19);
        await open.Api.SaveAnswerAsync(open.SubmissionId, open.Classic.ExamQuestionId, new(null, "kayıtlı"));
        await done.Api.SubmitAsync(done.SubmissionId);

        await instructor.EndExamAsync(exam.Id);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(SubmissionStatus.AutoSubmitted, (await db.Submissions.SingleAsync(x => x.Id == open.SubmissionId)).Status);
        Assert.Equal(SubmissionStatus.Submitted, (await db.Submissions.SingleAsync(x => x.Id == done.SubmissionId)).Status);
        var ended = await db.ActivityLogs.SingleAsync(l => l.EventType == ActivityEvents.ExamEnded && l.ExamId == exam.Id);
        Assert.Contains("\"autoSubmittedCount\": 1", ended.Details);
        Assert.Equal(ErrorCodes.ExamClosed, (await Assert.ThrowsAsync<ApiException>(() =>
            open.Api.SaveAnswerAsync(open.SubmissionId, open.Classic.ExamQuestionId, new(null, "geç")))).Code);
    }
}

/// <summary>Süre sonu: VT-29, VT-30'un otomatik kısmı ve log dosyası (VT-36'nın dosya kısmı). Gerçek zamanlayıcı ile.</summary>
public sealed class ExamTimerAndLogFileTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly string _logFolder = Path.Combine(Path.GetTempPath(), $"proglab-log-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_logFolder))
        {
            Directory.Delete(_logFolder, recursive: true);
        }
    }

    private static async Task<InstructorExamDto> PublishAsync(ApiClient instructor, string title)
    {
        var classId = (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var classic = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, $"{title} s", "Klasik", 5, null, ["cevap"]));
        var exam = await instructor.CreateExamAsync(new SaveExamRequest(title, null, ExamKind.Exam, classId, 40, null, null));
        await instructor.ReplaceExamQuestionsAsync(exam.Id, [new(classic.Id, 1, null)]);
        return await instructor.PublishExamAsync(exam.Id);
    }

    private static async Task ShortenAsync(TestDatabase database, int examId, DateTimeOffset endsAt)
    {
        await using var connection = new Npgsql.NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("UPDATE exams SET starts_at = @s, ends_at = @e WHERE id = @id", connection);
        command.Parameters.AddWithValue("s", endsAt.AddMinutes(-40));
        command.Parameters.AddWithValue("e", endsAt);
        command.Parameters.AddWithValue("id", examId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(clock.Elapsed < Timeout, "Beklenen durum zamanında oluşmadı.");
            await Task.Delay(100);
        }
    }

    private static Microsoft.EntityFrameworkCore.DbContextOptions<ProgLab.Server.Data.AppDbContext> Options(TestDatabase database)
    {
        var options = new DbContextOptionsBuilder<ProgLab.Server.Data.AppDbContext>();
        ProgLab.Server.Data.AppDbContext.Configure(options, database.ConnectionString);
        return options.Options;
    }

    /// <summary>VT-29: süre biter, teslim etmeyenler otomatik teslim edilir; ExamEnded ulaşır; log dosyası yazılır.</summary>
    [Fact]
    public async Task VT29_TimeUp_AutoSubmitsWithSavedAnswers_NotifiesAndWritesLogFile()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var server = new ProgLabServerFactory(database.ConnectionString, examCheckSeconds: 1, examLogDirectory: _logFolder);

        var instructor = await TestAccounts.InstructorAsync(server);
        var exam = await PublishAsync(instructor, "VT29");

        var one = await TestAccounts.StudentAsync(server, 1);
        var two = await TestAccounts.StudentAsync(server, 2);
        var three = await TestAccounts.StudentAsync(server, 3);
        var oneJoin = await one.JoinExamAsync(exam.Id);
        var twoJoin = await two.JoinExamAsync(exam.Id);
        var threeJoin = await three.JoinExamAsync(exam.Id);
        var question = (await one.GetPaperAsync(exam.Id)).Questions[0].ExamQuestionId;
        await one.SaveAnswerAsync(oneJoin.SubmissionId, question, new(null, "birinci öğrenci"));
        await three.SaveAnswerAsync(threeJoin.SubmissionId, question, new(null, "üçüncü öğrenci"));
        await three.SubmitAsync(threeJoin.SubmissionId);

        var tokens = new TokenStore();
        var studentApi = new ApiClient(server.CreateClient(), tokens);
        await studentApi.FirstLoginAsync(TestAccounts.StudentNumber(4), TestAccounts.StudentEmail(4), TestAccounts.StudentPassword);
        await using var hub = TestHubConnection.Create(server, tokens);
        var ended = new TaskCompletionSource<ExamEndedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.ExamEnded += e => ended.TrySetResult(e);
        await hub.StartAsync();

        await ShortenAsync(database, exam.Id, DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.Equal("TimeUp", (await ended.Task.WaitAsync(Timeout)).Reason);

        await using var db = new ProgLab.Server.Data.AppDbContext(Options(database));
        var stored = await db.Exams.SingleAsync(e => e.Id == exam.Id);
        Assert.Equal(ExamStatus.Completed, stored.Status);
        Assert.NotNull(stored.CompletedAt);

        var submissions = await db.Submissions.Where(s => s.ExamId == exam.Id).ToDictionaryAsync(s => s.Id);
        Assert.Equal(SubmissionStatus.AutoSubmitted, submissions[oneJoin.SubmissionId].Status);
        Assert.Equal(SubmissionStatus.AutoSubmitted, submissions[twoJoin.SubmissionId].Status);
        Assert.Equal(SubmissionStatus.Submitted, submissions[threeJoin.SubmissionId].Status);
        Assert.NotNull(submissions[twoJoin.SubmissionId].SubmittedAt);
        Assert.Equal(0, submissions[twoJoin.SubmissionId].SubmitCount);
        Assert.Equal(3, submissions.Count); // Katılmayanlara satır açılmaz.

        // Kayıtlı cevap korunur.
        Assert.Equal("birinci öğrenci", (await db.Answers.SingleAsync(a => a.SubmissionId == oneJoin.SubmissionId)).TextAnswer);

        var endedLog = await db.ActivityLogs.SingleAsync(l => l.EventType == ActivityEvents.ExamEnded && l.ExamId == exam.Id);
        Assert.Contains("TimeUp", endedLog.Details);
        Assert.Contains("\"autoSubmittedCount\": 2", endedLog.Details);
        Assert.Equal(2, await db.ActivityLogs.CountAsync(l => l.EventType == ActivityEvents.AutoSubmitted && l.ExamId == exam.Id));

        // Log dosyası: ders isterinin istediği teslim zamanı kaydı.
        var path = Path.Combine(_logFolder, $"exam-{exam.Id}.log");
        string[] lines = [];
        await WaitUntilAsync(async () =>
        {
            lines = await File.ReadAllLinesAsync(path);
            return lines.Any(l => l.Contains(ActivityEvents.ExamEnded));
        });

        Assert.Contains(lines, l => l.Contains(ActivityEvents.ExamPublished) && l.Contains("egitmen") && l.Contains("1 soru"));
        Assert.Contains(lines, l => l.Contains(ActivityEvents.Joined) && l.Contains(TestAccounts.StudentNumber(1)) && l.Contains("yeni gönderim"));
        var submitLine = Assert.Single(lines, l => l.Contains($"| {ActivityEvents.Submitted} "));
        Assert.Contains(TestAccounts.StudentNumber(3), submitLine);
        Assert.Contains("Demo Öğrenci 3", submitLine);
        Assert.Contains("1/1 cevap, 1. teslim", submitLine);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \|", submitLine);
        Assert.Equal(2, lines.Count(l => l.Contains(ActivityEvents.AutoSubmitted)));
        Assert.Contains(lines, l => l.Contains(ActivityEvents.ExamEnded) && l.Contains("süre doldu, 2 otomatik teslim"));
    }

    /// <summary>VT-30'un sunucu kısmı: sunucu kapalıyken süresi dolan sınav açılışta tamamlanır, süresi dolmayan sürer.</summary>
    [Fact]
    public async Task VT30_OnStartup_ExpiredExamIsCompleted_RunningExamContinues()
    {
        await using var database = await TestDatabase.CreateAsync();
        int expiredId, runningId;
        long submissionId;
        await using (var firstRun = new ProgLabServerFactory(database.ConnectionString))
        {
            var instructor = await TestAccounts.InstructorAsync(firstRun);
            var expired = await PublishAsync(instructor, "Kapalıyken biten");
            var running = await PublishAsync(instructor, "Süren");
            expiredId = expired.Id;
            runningId = running.Id;
            var student = await TestAccounts.StudentAsync(firstRun, 1);
            submissionId = (await student.JoinExamAsync(expiredId)).SubmissionId;
            await student.JoinExamAsync(runningId);
        }

        // Sunucu kapalı; bu sırada süre doldu.
        await ShortenAsync(database, expiredId, DateTimeOffset.UtcNow.AddMinutes(-1));

        await using var secondRun = new ProgLabServerFactory(database.ConnectionString, examCheckSeconds: 1);
        _ = secondRun.Services;

        await using var db = new ProgLab.Server.Data.AppDbContext(Options(database));
        await WaitUntilAsync(async () =>
            await db.Exams.AsNoTracking().Where(e => e.Id == expiredId).Select(e => e.Status).SingleAsync() == ExamStatus.Completed);

        Assert.Equal(SubmissionStatus.AutoSubmitted, await db.Submissions.AsNoTracking().Where(s => s.Id == submissionId).Select(s => s.Status).SingleAsync());
        Assert.Equal(ExamStatus.Published, await db.Exams.AsNoTracking().Where(e => e.Id == runningId).Select(e => e.Status).SingleAsync());

        // Süren sınava öğrenci kaldığı yerden devam eder.
        var again = new ApiClient(secondRun.CreateClient());
        await again.LoginAsync(TestAccounts.StudentNumber(1), TestAccounts.StudentPassword, UserRole.Student);
        Assert.InRange((await again.JoinExamAsync(runningId)).RemainingSeconds, 1, 40 * 60);
    }

    [Fact]
    public async Task ConcurrentCompletion_HappensOnlyOnce()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var server = new ProgLabServerFactory(database.ConnectionString);
        var instructor = await TestAccounts.InstructorAsync(server);
        var exam = await PublishAsync(instructor, "Tek tamamlama");
        var student = await TestAccounts.StudentAsync(server, 1);
        await student.JoinExamAsync(exam.Id);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            try
            {
                await instructor.EndExamAsync(exam.Id);
                return true;
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                return false;
            }
        }));

        Assert.Single(attempts, a => a);
        await using var db = new ProgLab.Server.Data.AppDbContext(Options(database));
        Assert.Equal(1, await db.ActivityLogs.CountAsync(l => l.EventType == ActivityEvents.ExamEnded && l.ExamId == exam.Id));
        Assert.Equal(1, await db.ActivityLogs.CountAsync(l => l.EventType == ActivityEvents.AutoSubmitted && l.ExamId == exam.Id));
    }

    [Fact]
    public void LogLine_HasDesignedShape()
    {
        var at = new DateTimeOffset(2026, 11, 20, 7, 32, 13, 512, TimeSpan.Zero);

        var line = ExamLogWriter.FormatLine(at, "SUBMITTED", "2026900012", "Demo Öğrenci 12", "192.168.1.23", "18/20 cevap, 1. teslim");

        Assert.Contains("| SUBMITTED       | 2026900012 | Demo Öğrenci 12 | 192.168.1.23    | 18/20 cevap, 1. teslim", line);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:13\.512 [+-]\d{2}:\d{2} ", line);
    }

    /// <summary>60 öğrenci aynı anda teslim etse de satırlar karışmaz.</summary>
    [Fact]
    public async Task Writer_ConcurrentLines_DoNotInterleave()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var server = new ProgLabServerFactory(database.ConnectionString, examLogDirectory: _logFolder);
        var writer = server.Services.GetRequiredService<ExamLogWriter>();
        const int lineCount = 300;

        await Task.WhenAll(Enumerable.Range(0, lineCount).Select(i => Task.Run(() => writer.WriteAsync(77, $"satir-{i}-son"))));

        var lines = await File.ReadAllLinesAsync(Path.Combine(_logFolder, "exam-77.log"));
        Assert.Equal(lineCount, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"^satir-\d+-son$", l));
        Assert.Equal(lineCount, lines.Distinct().Count());
    }
}
