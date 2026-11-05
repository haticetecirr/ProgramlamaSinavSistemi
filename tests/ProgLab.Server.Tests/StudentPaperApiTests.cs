using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProgLab.Client.Core;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Katılma ve sınav kâğıdı: VT-18 (join), VT-19, VT-20, VT-38.</summary>
public sealed class StudentPaperApiTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly QuestionOptionInput[] Options = [new("Yanlış şık", false), new("Doğru şık", true), new("Diğer şık", false)];

    private Task<ApiClient> InstructorAsync() => TestAccounts.InstructorAsync(fixture.Server);

    private static async Task<int> ClassAIdAsync(ApiClient instructor) =>
        (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;

    /// <summary>Bir çoktan seçmeli ve bir klasik sorulu yayınlanmış sınav.</summary>
    private static async Task<InstructorExamDto> PublishedExamAsync(ApiClient instructor, int classId, string title, int duration = 40)
    {
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var mcq = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.MultipleChoice, $"{title} GIZLI-BASLIK", $"{title} şıklı soru", 10, Options, null));
        var classic = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, $"{title} klasik", $"{title} klasik soru", 5, null, ["GIZLI-CEVAP", "ikinci gizli"]));
        var exam = await instructor.CreateExamAsync(new SaveExamRequest(title, "Açıklama metni", ExamKind.Exam, classId, duration, null, null));
        await instructor.ReplaceExamQuestionsAsync(exam.Id, [new(mcq.Id, 1, 12), new(classic.Id, 2, null)]);
        return await instructor.PublishExamAsync(exam.Id);
    }

    private async Task<ApiClient> StudentAsync(int index) => await TestAccounts.StudentAsync(fixture.Server, index);

    [Fact]
    public async Task Join_CreatesSubmission_WithServerRemainingTime_AndLogs()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Katılım");
        var student = await StudentAsync(1);

        var joined = await student.JoinExamAsync(exam.Id);

        Assert.Equal(SubmissionStatus.InProgress, joined.Status);
        Assert.InRange(joined.RemainingSeconds, 40 * 60 - 30, 40 * 60);
        Assert.Equal(exam.EndsAt!.Value.ToUnixTimeSeconds(), joined.EndsAtUtc.ToUnixTimeSeconds());

        await using var db = fixture.CreateDbContext();
        var submission = await db.Submissions.SingleAsync(s => s.Id == joined.SubmissionId);
        Assert.Equal(exam.Id, submission.ExamId);
        Assert.Equal(0, submission.SubmitCount);
        Assert.Null(submission.SubmittedAt);
        var log = await db.ActivityLogs.SingleAsync(l => l.EventType == "JOINED" && l.SubmissionId == joined.SubmissionId);
        Assert.Contains("\"isNew\": true", log.Details);
        Assert.Equal(exam.Id, log.ExamId);
    }

    /// <summary>VT-19'un sunucu kısmı: yeniden katılmak aynı gönderimi döner ve süreyi sıfırlamaz.</summary>
    [Fact]
    public async Task VT19_RejoiningReturnsSameSubmission_AndDoesNotResetTime()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Yeniden katılım");
        var firstDevice = await StudentAsync(2);

        var first = await firstDevice.JoinExamAsync(exam.Id);
        await Task.Delay(1100);

        // Uygulama kapatılıp açıldı: yeni oturum, aynı öğrenci.
        var reopened = new ApiClient(fixture.Server.CreateClient());
        await reopened.LoginAsync(TestAccounts.StudentNumber(2), TestAccounts.StudentPassword, UserRole.Student);
        var second = await reopened.JoinExamAsync(exam.Id);

        Assert.Equal(first.SubmissionId, second.SubmissionId);
        Assert.True(second.RemainingSeconds < first.RemainingSeconds);
        Assert.Equal(first.EndsAtUtc, second.EndsAtUtc);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Submissions.CountAsync(s => s.ExamId == exam.Id && s.Id == first.SubmissionId));
        var logs = await db.ActivityLogs.Where(l => l.EventType == "JOINED" && l.SubmissionId == first.SubmissionId).OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Contains("\"isNew\": false", logs[1].Details);

        var lobbyRow = (await reopened.GetLobbyAsync()).Single(e => e.Id == exam.Id);
        Assert.True(lobbyRow.HasStarted);
    }

    [Fact]
    public async Task ParallelJoins_CreateOnlyOneSubmission()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Eş zamanlı katılım");
        var student = await StudentAsync(3);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => student.JoinExamAsync(exam.Id)));

        Assert.Single(results.Select(r => r.SubmissionId).Distinct());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Submissions.CountAsync(s => s.Id == results[0].SubmissionId));
    }

    [Fact]
    public async Task VT18_StudentOfAnotherClass_CannotJoinOrReadPaper()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Başka şube");
        var otherClass = await instructor.CreateClassAsync("Paper başka şube", "Test");
        var created = await instructor.CreateStudentAsync("2026200001", "Dış Öğrenci", "dis1@ogr.demo.local");
        await instructor.ReplaceClassMembersAsync(otherClass.Id, [created.Id]);
        var outsider = new ApiClient(fixture.Server.CreateClient());
        await outsider.FirstLoginAsync("2026200001", "dis1@ogr.demo.local", TestAccounts.StudentPassword);

        var join = await Assert.ThrowsAsync<ApiException>(() => outsider.JoinExamAsync(exam.Id));
        var paper = await Assert.ThrowsAsync<ApiException>(() => outsider.GetPaperAsync(exam.Id));

        Assert.Equal(ErrorCodes.NotInClass, join.Code);
        Assert.Equal(403, join.StatusCode);
        Assert.Equal(ErrorCodes.NotInClass, paper.Code);

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Submissions.AnyAsync(s => s.ExamId == exam.Id && s.StudentId == created.Id));
    }

    [Fact]
    public async Task VT20_AssignmentBeforeStart_CannotBeJoined()
    {
        var instructor = await InstructorAsync();
        var classId = await ClassAIdAsync(instructor);
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var question = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, "Ödev S", "M", 1, null, ["c"]));
        var starts = DateTimeOffset.UtcNow.AddDays(1);
        var assignment = await instructor.CreateExamAsync(new SaveExamRequest("Yakında", null, ExamKind.Assignment, classId, null, starts, starts.AddDays(1)));
        await instructor.ReplaceExamQuestionsAsync(assignment.Id, [new(question.Id, 1, null)]);
        await instructor.PublishExamAsync(assignment.Id);
        var student = await StudentAsync(4);

        var join = await Assert.ThrowsAsync<ApiException>(() => student.JoinExamAsync(assignment.Id));
        var paper = await Assert.ThrowsAsync<ApiException>(() => student.GetPaperAsync(assignment.Id));

        Assert.Equal(ErrorCodes.ExamNotOpen, join.Code);
        Assert.Equal(409, join.StatusCode);
        Assert.Equal(ErrorCodes.ExamNotOpen, paper.Code);
    }

    [Fact]
    public async Task ClosedExam_CannotBeJoinedOrRead_EvenByParticipant()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Kapanan");
        var participant = await StudentAsync(5);
        await participant.JoinExamAsync(exam.Id);
        var other = await StudentAsync(6);

        await instructor.EndExamAsync(exam.Id);

        Assert.Equal(ErrorCodes.ExamClosed, (await Assert.ThrowsAsync<ApiException>(() => other.JoinExamAsync(exam.Id))).Code);
        Assert.Equal(ErrorCodes.ExamClosed, (await Assert.ThrowsAsync<ApiException>(() => participant.JoinExamAsync(exam.Id))).Code);
        Assert.Equal(ErrorCodes.ExamClosed, (await Assert.ThrowsAsync<ApiException>(() => participant.GetPaperAsync(exam.Id))).Code);
    }

    [Fact]
    public async Task ExpiredByClock_IsClosed_EvenBeforeTimerCompletesIt()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Süresi geçen");
        var student = await StudentAsync(7);
        await student.JoinExamAsync(exam.Id);

        // Süre doldu ama sınav hâlâ Published: tolerans yoktur.
        await using (var db = fixture.CreateDbContext())
        {
            var past = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.Exams.Where(e => e.Id == exam.Id).ExecuteUpdateAsync(s => s
                .SetProperty(e => e.StartsAt, past.AddMinutes(-40))
                .SetProperty(e => e.EndsAt, past));
        }

        Assert.Equal(ErrorCodes.ExamClosed, (await Assert.ThrowsAsync<ApiException>(() => student.GetPaperAsync(exam.Id))).Code);
    }

    [Fact]
    public async Task DraftExam_IsInvisible_AsNotFound()
    {
        var instructor = await InstructorAsync();
        var draft = await instructor.CreateExamAsync(new SaveExamRequest("Görünmez taslak", null, ExamKind.Exam, await ClassAIdAsync(instructor), 10, null, null));
        var student = await StudentAsync(8);

        var join = await Assert.ThrowsAsync<ApiException>(() => student.JoinExamAsync(draft.Id));
        var paper = await Assert.ThrowsAsync<ApiException>(() => student.GetPaperAsync(draft.Id));

        Assert.Equal(404, join.StatusCode);
        Assert.Equal(ErrorCodes.ExamNotFound, paper.Code);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => student.JoinExamAsync(999_999))).StatusCode);
    }

    [Fact]
    public async Task Paper_RequiresJoin()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Katılmadan");
        var student = await StudentAsync(9);

        var error = await Assert.ThrowsAsync<ApiException>(() => student.GetPaperAsync(exam.Id));

        Assert.Equal(ErrorCodes.InvalidState, error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task Paper_ShowsQuestionsInOrder_WithExamPoints_AndNoAnswerKey()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Kâğıt");
        var student = await StudentAsync(10);
        var joined = await student.JoinExamAsync(exam.Id);

        var paper = await student.GetPaperAsync(exam.Id);

        Assert.Equal(joined.SubmissionId, paper.SubmissionId);
        Assert.Equal("Kâğıt", paper.Title);
        Assert.Equal("Açıklama metni", paper.Description);
        Assert.InRange(paper.RemainingSeconds, 1, 40 * 60);
        Assert.Empty(paper.Answers);

        Assert.Equal(2, paper.Questions.Count);
        var mcq = paper.Questions[0];
        Assert.Equal(QuestionType.MultipleChoice, mcq.Type);
        Assert.Equal("Kâğıt şıklı soru", mcq.Text);
        Assert.Equal(12m, mcq.Points); // Sınava özel puan.
        Assert.Equal(["Yanlış şık", "Doğru şık", "Diğer şık"], mcq.Options.Select(o => o.Text));

        var classic = paper.Questions[1];
        Assert.Equal(QuestionType.Classic, classic.Type);
        Assert.Equal(5m, classic.Points); // Varsayılan puan.
        Assert.Empty(classic.Options);
    }

    /// <summary>VT-38: gerçek HTTP cevabında cevap anahtarından hiçbir iz olmamalı.</summary>
    [Fact]
    public async Task VT38_PaperJson_ContainsNoAnswerKey()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Sızıntı");
        var login = await new ApiClient(fixture.Server.CreateClient()).FirstLoginAsync(TestAccounts.StudentNumber(11), TestAccounts.StudentEmail(11), TestAccounts.StudentPassword);
        var client = fixture.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/student/exams/{exam.Id}/join", null)).StatusCode);

        var json = await client.GetStringAsync($"/api/student/exams/{exam.Id}/paper");

        Assert.DoesNotContain("isCorrect", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acceptedText", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("normalizedText", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GIZLI-CEVAP", json);
        Assert.DoesNotContain("ikinci gizli", json);
        Assert.DoesNotContain("GIZLI-BASLIK", json); // Bankadaki başlık da öğrenciye gitmez.
        Assert.Contains("Doğru şık", json);          // Şıklar gider ama hangisinin doğru olduğu gitmez.

        using var document = JsonDocument.Parse(json);
        var option = document.RootElement.GetProperty("questions")[0].GetProperty("options")[0];
        Assert.Equal(["id", "orderNo", "text"], option.EnumerateObject().Select(p => p.Name).Order());
    }

    /// <summary>VT-38'in birim kısmı: öğrenci DTO'larında bu alanlar tip düzeyinde yoktur.</summary>
    [Theory]
    [InlineData(typeof(StudentPaperDto))]
    [InlineData(typeof(StudentQuestionDto))]
    [InlineData(typeof(StudentOptionDto))]
    [InlineData(typeof(StudentAnswerDto))]
    [InlineData(typeof(StudentExamSummaryDto))]
    [InlineData(typeof(JoinExamResponse))]
    public void VT38_StudentDtos_HaveNoAnswerKeyOrScoreFields(Type dto)
    {
        string[] forbidden = ["IsCorrect", "AcceptedText", "NormalizedText", "AnswerKeys", "Score", "TotalScore", "GradedBy", "GradedNote"];

        var properties = dto.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);

        Assert.Empty(properties.Intersect(forbidden));
    }

    [Fact]
    public void VT38_StudentQuestionDto_SerializesWithoutForbiddenFields()
    {
        var json = JsonSerializer.Serialize(
            new StudentQuestionDto(1, 1, QuestionType.MultipleChoice, "Metin", 5, [new StudentOptionDto(1, 1, "A")]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("isCorrect", json);
        Assert.DoesNotContain("acceptedText", json);
    }

    [Fact]
    public async Task Paper_ReturnsSavedAnswers()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Kayıtlı cevap");
        var student = await StudentAsync(12);
        var joined = await student.JoinExamAsync(exam.Id);
        var paper = await student.GetPaperAsync(exam.Id);
        var mcq = paper.Questions[0];
        var classic = paper.Questions[1];

        // Cevap kaydı Aşama 8'de; burada satırlar doğrudan yazılır.
        await using (var db = fixture.CreateDbContext())
        {
            db.Answers.AddRange(
                new Answer { SubmissionId = joined.SubmissionId, ExamQuestionId = mcq.ExamQuestionId, SelectedOptionId = mcq.Options[1].Id, UpdatedAt = DateTimeOffset.UtcNow },
                new Answer { SubmissionId = joined.SubmissionId, ExamQuestionId = classic.ExamQuestionId, TextAnswer = "yazdığım cevap", UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var again = await student.GetPaperAsync(exam.Id);

        Assert.Equal(mcq.Options[1].Id, again.Answers.Single(a => a.ExamQuestionId == mcq.ExamQuestionId).SelectedOptionId);
        Assert.Equal("yazdığım cevap", again.Answers.Single(a => a.ExamQuestionId == classic.ExamQuestionId).TextAnswer);
    }

    [Fact]
    public async Task Paper_ShowsLockedVersion_AfterQuestionIsEditedInBank()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Sürüm kâğıdı");
        var questionId = exam.Questions[0].QuestionId;
        var original = await instructor.GetQuestionAsync(questionId);
        await instructor.UpdateQuestionAsync(questionId, new SaveQuestionRequest(original.CategoryId, original.Type, "Yeni", "BANKADA DEĞİŞEN METİN", 1, [new("Yeni A", true), new("Yeni B", false)], null));
        var student = await StudentAsync(13);
        await student.JoinExamAsync(exam.Id);

        var paper = await student.GetPaperAsync(exam.Id);

        Assert.Equal("Sürüm kâğıdı şıklı soru", paper.Questions[0].Text);
        Assert.Equal(["Yanlış şık", "Doğru şık", "Diğer şık"], paper.Questions[0].Options.Select(o => o.Text));
    }

    [Fact]
    public async Task SubmittedExam_CannotBeRejoined_ButAssignmentCan()
    {
        var instructor = await InstructorAsync();
        var classId = await ClassAIdAsync(instructor);
        var exam = await PublishedExamAsync(instructor, classId, "Teslim edilen sınav");
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var question = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, "Teslim ödev", "M", 1, null, ["c"]));
        var assignment = await instructor.CreateExamAsync(new SaveExamRequest("Teslim edilen ödev", null, ExamKind.Assignment, classId, null, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1)));
        await instructor.ReplaceExamQuestionsAsync(assignment.Id, [new(question.Id, 1, null)]);
        await instructor.PublishExamAsync(assignment.Id);

        var student = await StudentAsync(14);
        var examJoin = await student.JoinExamAsync(exam.Id);
        var assignmentJoin = await student.JoinExamAsync(assignment.Id);

        // Teslim Aşama 8'de; durum doğrudan yazılır.
        await using (var db = fixture.CreateDbContext())
        {
            long[] ids = [examJoin.SubmissionId, assignmentJoin.SubmissionId];
            await db.Submissions.Where(s => ids.Contains(s.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubmissionStatus.Submitted));
        }

        var rejoin = await Assert.ThrowsAsync<ApiException>(() => student.JoinExamAsync(exam.Id));
        Assert.Equal(ErrorCodes.AlreadySubmitted, rejoin.Code);
        Assert.Equal(409, rejoin.StatusCode);
        Assert.Equal(ErrorCodes.AlreadySubmitted, (await Assert.ThrowsAsync<ApiException>(() => student.GetPaperAsync(exam.Id))).Code);

        Assert.Equal(SubmissionStatus.Submitted, (await student.JoinExamAsync(assignment.Id)).Status);
        Assert.Single((await student.GetPaperAsync(assignment.Id)).Questions);

        var lobby = await student.GetLobbyAsync();
        Assert.Equal(StudentExamState.Submitted, lobby.Single(e => e.Id == exam.Id).State);
    }

    [Fact]
    public async Task Instructor_CannotUseStudentEndpoints()
    {
        var instructor = await InstructorAsync();
        var exam = await PublishedExamAsync(instructor, await ClassAIdAsync(instructor), "Rol");

        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => instructor.JoinExamAsync(exam.Id))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => instructor.GetPaperAsync(exam.Id))).StatusCode);
    }
}

/// <summary>TimeSync: kalan süre yalnızca sınava katılanlara, gerçek hub üzerinden gider.</summary>
public sealed class TimeSyncTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task TimeSync_ReachesOnlyJoinedStudents_AndContinuesAfterReconnect()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var server = new ProgLabServerFactory(database.ConnectionString, timeSyncSeconds: 1);

        var instructor = await TestAccounts.InstructorAsync(server);
        var classId = (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var question = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, "TS", "M", 1, null, ["c"]));
        var exam = await instructor.CreateExamAsync(new SaveExamRequest("Süre bildirimi", null, ExamKind.Exam, classId, 40, null, null));
        await instructor.ReplaceExamQuestionsAsync(exam.Id, [new(question.Id, 1, null)]);
        await instructor.PublishExamAsync(exam.Id);

        var joinedTokens = new TokenStore();
        var joinedApi = new ApiClient(server.CreateClient(), joinedTokens);
        await joinedApi.FirstLoginAsync(TestAccounts.StudentNumber(1), TestAccounts.StudentEmail(1), TestAccounts.StudentPassword);
        await using var joinedHub = TestHubConnection.Create(server, joinedTokens);

        var watcherTokens = new TokenStore();
        var watcherApi = new ApiClient(server.CreateClient(), watcherTokens);
        await watcherApi.FirstLoginAsync(TestAccounts.StudentNumber(2), TestAccounts.StudentEmail(2), TestAccounts.StudentPassword);
        await using var watcherHub = TestHubConnection.Create(server, watcherTokens);

        var received = new TaskCompletionSource<TimeSyncEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcherReceived = false;
        joinedHub.TimeSync += e => received.TrySetResult(e);
        watcherHub.TimeSync += _ => watcherReceived = true;

        await joinedHub.StartAsync();
        await watcherHub.StartAsync();

        // Önce bağlandı, sonra katıldı: bağlantı sınav grubuna eklenir.
        await joinedApi.JoinExamAsync(exam.Id);

        var sync = await received.Task.WaitAsync(EventTimeout);
        Assert.Equal(exam.Id, sync.ExamId);
        Assert.InRange(sync.RemainingSeconds, 40 * 60 - 60, 40 * 60);

        await Task.Delay(1500);
        Assert.False(watcherReceived); // Lobide bekleyen öğrenciye süre bildirimi gitmez.

        // Bağlantı kopup yeniden kurulunca bildirim sürer.
        await joinedHub.StopAsync();
        var afterReconnect = new TaskCompletionSource<TimeSyncEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        joinedHub.TimeSync += e => afterReconnect.TrySetResult(e);
        await joinedHub.StartAsync();
        Assert.Equal(exam.Id, (await afterReconnect.Task.WaitAsync(EventTimeout)).ExamId);
    }
}
