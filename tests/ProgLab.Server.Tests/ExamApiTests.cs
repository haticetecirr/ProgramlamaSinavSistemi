using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ProgLab.Client.Core;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Sınav yaşam döngüsü: VT-06, VT-14, VT-15, VT-16 ve durum geçişleri gerçek API üzerinden.</summary>
public sealed class ExamApiTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly QuestionOptionInput[] Options = [new("Doğru", true), new("Yanlış", false)];

    private async Task<(ApiClient Api, int ClassId)> InstructorAsync()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var classId = (await api.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;
        return (api, classId);
    }

    private static SaveExamRequest ExamRequest(int classId, string title = "Vize", int duration = 40) =>
        new(title, "Açıklama", ExamKind.Exam, classId, duration, null, null);

    private static SaveExamRequest AssignmentRequest(int classId, DateTimeOffset starts, DateTimeOffset ends) =>
        new("Ödev", null, ExamKind.Assignment, classId, null, starts, ends);

    private static async Task<InstructorQuestionDto> NewQuestionAsync(ApiClient api, string title, decimal points = 10)
    {
        var category = (await api.GetCategoriesAsync())[0].Id;
        return await api.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.MultipleChoice, title, "Metin", points, Options, null));
    }

    private static async Task<InstructorExamDto> ExamWithQuestionAsync(ApiClient api, int classId, string title)
    {
        var exam = await api.CreateExamAsync(ExamRequest(classId, title));
        var question = await NewQuestionAsync(api, $"{title} sorusu");
        return await api.ReplaceExamQuestionsAsync(exam.Id, [new(question.Id, 1, null)]);
    }

    [Fact]
    public async Task VT15_FiveQuestionDraft_TotalIsCalculated_AndPointsCanChange()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await api.CreateExamAsync(ExamRequest(classId, "VT15"));
        Assert.Equal(ExamStatus.Draft, exam.Status);
        Assert.Null(exam.StartsAt);
        Assert.Equal(0m, exam.TotalPoints);

        var ids = new List<int>();
        foreach (var i in Enumerable.Range(1, 5))
        {
            ids.Add((await NewQuestionAsync(api, $"VT15 soru {i}", points: 10)).Id);
        }

        // Puan verilmezse sorunun varsayılan puanı gelir.
        var saved = await api.ReplaceExamQuestionsAsync(exam.Id, ids.Select((id, i) => new ExamQuestionInput(id, (short)(i + 1), null)).ToList());
        Assert.Equal(50m, saved.TotalPoints);
        Assert.Equal(5, saved.Questions.Count);
        Assert.Equal(ids, saved.Questions.Select(q => q.QuestionId));

        // Bir sorunun puanı değişince toplam değişir; sıra da değiştirilebilir.
        var changed = await api.ReplaceExamQuestionsAsync(exam.Id, ids
            .Select((id, i) => new ExamQuestionInput(id, (short)(5 - i), i == 0 ? 25.5m : null))
            .ToList());
        Assert.Equal(65.5m, changed.TotalPoints);
        Assert.Equal(ids[4], changed.Questions[0].QuestionId);
        Assert.Equal(25.5m, changed.Questions[4].Points);

        var row = (await api.GetExamsAsync(ExamStatus.Draft)).Items.Single(e => e.Id == exam.Id);
        Assert.Equal(5, row.QuestionCount);
        Assert.Equal(65.5m, row.TotalPoints);
    }

    [Fact]
    public async Task VT16_PublishedExam_CannotBeEdited()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await ExamWithQuestionAsync(api, classId, "VT16");
        await api.PublishExamAsync(exam.Id);

        var update = await Assert.ThrowsAsync<ApiException>(() => api.UpdateExamAsync(exam.Id, ExamRequest(classId, "Değişti")));
        var questions = await Assert.ThrowsAsync<ApiException>(() => api.ReplaceExamQuestionsAsync(exam.Id, []));
        var delete = await Assert.ThrowsAsync<ApiException>(() => api.DeleteExamAsync(exam.Id));

        Assert.Equal(ErrorCodes.ExamNotDraft, update.Code);
        Assert.Equal(409, update.StatusCode);
        Assert.Equal(ErrorCodes.ExamNotDraft, questions.Code);
        Assert.Equal(ErrorCodes.ExamNotDraft, delete.Code);
        Assert.Single((await api.GetExamAsync(exam.Id)).Questions);
    }

    [Fact]
    public async Task VT14_EditingQuestionAfterPublish_DoesNotChangeExamContent()
    {
        var (api, classId) = await InstructorAsync();
        var question = await NewQuestionAsync(api, "VT14 tam senaryo");
        var exam = await api.CreateExamAsync(ExamRequest(classId, "VT14"));
        await api.ReplaceExamQuestionsAsync(exam.Id, [new(question.Id, 1, 7)]);

        await api.PublishExamAsync(exam.Id);
        Assert.True((await api.GetQuestionAsync(question.Id)).IsLocked);

        var category = question.CategoryId;
        var response = await api.UpdateQuestionAsync(question.Id, new SaveQuestionRequest(
            category, QuestionType.MultipleChoice, "VT14 yeni başlık", "Yeni metin", 99, [new("Yeni doğru", true), new("Yeni yanlış", false)], null));
        Assert.NotNull(response.NewVersionId);

        var after = await api.GetExamAsync(exam.Id);
        var examQuestion = Assert.Single(after.Questions);
        Assert.Equal(question.Id, examQuestion.QuestionId);
        Assert.Equal("VT14 tam senaryo", examQuestion.Title);
        Assert.Equal(7m, examQuestion.Points);
        Assert.False(examQuestion.IsCurrent);

        var original = await api.GetQuestionAsync(question.Id);
        Assert.Equal("Metin", original.Text);
        Assert.Equal(["Doğru", "Yanlış"], original.Options.Select(o => o.Text));
        Assert.Equal("Doğru", original.Options.Single(o => o.IsCorrect).Text);
    }

    [Fact]
    public async Task VT06_StudentToken_OnExamEndpoints_Returns403()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await api.CreateExamAsync(ExamRequest(classId, "VT06"));
        var login = await new ApiClient(fixture.Server.CreateClient()).FirstLoginAsync(TestAccounts.StudentNumber(25), TestAccounts.StudentEmail(25), TestAccounts.StudentPassword);
        var client = fixture.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/exams", new { title = "x", kind = "Exam", classId, durationMinutes = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/exams")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/exams/{exam.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/exams/{exam.Id}/publish", null)).StatusCode);
    }

    [Fact]
    public async Task Publish_Exam_SetsTimes_LocksQuestions_AndLogs()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await ExamWithQuestionAsync(api, classId, "Yayın sınavı");
        var before = DateTimeOffset.UtcNow;

        var published = await api.PublishExamAsync(exam.Id);

        Assert.Equal(ExamStatus.Published, published.Status);
        Assert.NotNull(published.PublishedAt);
        Assert.InRange(published.StartsAt!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(published.StartsAt.Value.AddMinutes(40), published.EndsAt);

        await using var db = fixture.CreateDbContext();
        var questionId = published.Questions[0].QuestionId;
        Assert.True(await db.Questions.Where(q => q.Id == questionId).Select(q => q.IsLocked).SingleAsync());
        var log = await db.ActivityLogs.SingleAsync(l => l.EventType == "EXAM_PUBLISHED" && l.ExamId == exam.Id);
        Assert.Contains("\"questionCount\": 1", log.Details); // jsonb metni bu biçimde saklar
        Assert.NotNull(log.UserId);

        var entity = await db.Exams.SingleAsync(e => e.Id == exam.Id);
        Assert.True(entity.IsOpenAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Publish_SecondTime_Returns409()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await ExamWithQuestionAsync(api, classId, "İki kez yayın");
        await api.PublishExamAsync(exam.Id);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.PublishExamAsync(exam.Id));

        Assert.Equal(ErrorCodes.ExamNotDraft, error.Code);
    }

    [Fact]
    public async Task Publish_WithoutQuestions_IsRejected_AndNothingChanges()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await api.CreateExamAsync(ExamRequest(classId, "Sorusuz"));

        var error = await Assert.ThrowsAsync<ApiException>(() => api.PublishExamAsync(exam.Id));

        Assert.Equal(ErrorCodes.ExamHasNoQuestions, error.Code);
        Assert.Equal(422, error.StatusCode);
        Assert.Equal(ExamStatus.Draft, (await api.GetExamAsync(exam.Id)).Status);
    }

    [Fact]
    public async Task Publish_ToClassWithoutStudents_IsRejected_AndQuestionsStayUnlocked()
    {
        var (api, _) = await InstructorAsync();
        var emptyClass = await api.CreateClassAsync("Boş şube", "Test");
        var exam = await ExamWithQuestionAsync(api, emptyClass.Id, "Öğrencisiz");

        var error = await Assert.ThrowsAsync<ApiException>(() => api.PublishExamAsync(exam.Id));

        Assert.Equal(ErrorCodes.ClassHasNoStudents, error.Code);
        Assert.False((await api.GetQuestionAsync(exam.Questions[0].QuestionId)).IsLocked);
    }

    [Fact]
    public async Task Publish_WithDeletedQuestion_IsRejected_UntilRemoved()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await ExamWithQuestionAsync(api, classId, "Silinmiş sorulu");
        var extra = await NewQuestionAsync(api, "Silinmiş sorulu kalan");
        var withTwo = await api.ReplaceExamQuestionsAsync(exam.Id, [new(exam.Questions[0].QuestionId, 1, null), new(extra.Id, 2, null)]);

        await api.DeleteQuestionAsync(exam.Questions[0].QuestionId);

        var shown = await api.GetExamAsync(exam.Id);
        Assert.True(shown.Questions.Single(q => q.QuestionId == exam.Questions[0].QuestionId).IsDeleted);
        var error = await Assert.ThrowsAsync<ApiException>(() => api.PublishExamAsync(exam.Id));
        Assert.Equal(ErrorCodes.ExamHasDeletedQuestion, error.Code);

        // Silinmiş soru listeden çıkarılınca yayınlanır.
        await api.ReplaceExamQuestionsAsync(exam.Id, [new(extra.Id, 1, null)]);
        Assert.Equal(ExamStatus.Published, (await api.PublishExamAsync(exam.Id)).Status);
        Assert.Equal(2, withTwo.Questions.Count);
    }

    [Fact]
    public async Task Assignment_KeepsDraftDates_AndOpensWithClock()
    {
        var (api, classId) = await InstructorAsync();
        var starts = new DateTimeOffset(DateTimeOffset.UtcNow.AddDays(1).UtcDateTime, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(3));
        var ends = starts.AddDays(2);
        var exam = await api.CreateExamAsync(AssignmentRequest(classId, starts, ends));
        Assert.Null(exam.DurationMinutes);
        var question = await NewQuestionAsync(api, "Ödev sorusu");
        await api.ReplaceExamQuestionsAsync(exam.Id, [new(question.Id, 1, null)]);

        var published = await api.PublishExamAsync(exam.Id);

        Assert.Equal(ExamStatus.Published, published.Status);
        Assert.Equal(starts.ToUnixTimeSeconds(), published.StartsAt!.Value.ToUnixTimeSeconds());
        Assert.Equal(ends.ToUnixTimeSeconds(), published.EndsAt!.Value.ToUnixTimeSeconds());

        await using var db = fixture.CreateDbContext();
        var entity = await db.Exams.SingleAsync(e => e.Id == exam.Id);
        Assert.False(entity.IsOpenAt(DateTimeOffset.UtcNow));           // Yakında
        Assert.True(entity.IsOpenAt(starts.AddMinutes(1)));             // Açık
        Assert.False(entity.IsOpenAt(ends));                            // Bitti
    }

    [Fact]
    public async Task Assignment_WithPastEnd_CannotBePublished()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await api.CreateExamAsync(AssignmentRequest(classId, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1)));
        var question = await NewQuestionAsync(api, "Geçmiş ödev sorusu");
        await api.ReplaceExamQuestionsAsync(exam.Id, [new(question.Id, 1, null)]);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.PublishExamAsync(exam.Id));

        Assert.Equal(ErrorCodes.InvalidTimeWindow, error.Code);
    }

    [Fact]
    public async Task End_CompletesExam_LogsManualReason_AndClosesWindow()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await ExamWithQuestionAsync(api, classId, "Erken bitirme");
        await api.PublishExamAsync(exam.Id);

        var ended = await api.EndExamAsync(exam.Id);

        Assert.Equal(ExamStatus.Completed, ended.Status);
        Assert.NotNull(ended.CompletedAt);
        Assert.Equal(ended.CompletedAt!.Value.ToUnixTimeSeconds(), ended.EndsAt!.Value.ToUnixTimeSeconds());

        await using var db = fixture.CreateDbContext();
        var log = await db.ActivityLogs.SingleAsync(l => l.EventType == "EXAM_ENDED" && l.ExamId == exam.Id);
        Assert.Contains("Manual", log.Details);
        Assert.False((await db.Exams.SingleAsync(e => e.Id == exam.Id)).IsOpenAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task End_NotStartedAssignment_DoesNotBreakDatabaseCheck()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await api.CreateExamAsync(AssignmentRequest(classId, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2)));
        var question = await NewQuestionAsync(api, "Başlamamış ödev sorusu");
        await api.ReplaceExamQuestionsAsync(exam.Id, [new(question.Id, 1, null)]);
        await api.PublishExamAsync(exam.Id);

        var ended = await api.EndExamAsync(exam.Id);

        Assert.Equal(ExamStatus.Completed, ended.Status);
        Assert.True(ended.StartsAt < ended.EndsAt);
    }

    [Fact]
    public async Task End_OnDraftOrCompleted_Returns409()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await ExamWithQuestionAsync(api, classId, "Geçersiz bitirme");

        Assert.Equal(ErrorCodes.InvalidState, (await Assert.ThrowsAsync<ApiException>(() => api.EndExamAsync(exam.Id))).Code);

        await api.PublishExamAsync(exam.Id);
        await api.EndExamAsync(exam.Id);

        var again = await Assert.ThrowsAsync<ApiException>(() => api.EndExamAsync(exam.Id));
        Assert.Equal(ErrorCodes.InvalidState, again.Code);
        Assert.Equal(409, again.StatusCode);
    }

    [Fact]
    public async Task Copy_CompletedExam_CreatesDraft_WithSameLockedVersionsAndPoints()
    {
        var (api, classId) = await InstructorAsync();
        var question = await NewQuestionAsync(api, "Kopya sorusu");
        var exam = await api.CreateExamAsync(ExamRequest(classId, "Kopyalanacak"));
        await api.ReplaceExamQuestionsAsync(exam.Id, [new(question.Id, 1, 12.5m)]);

        Assert.Equal(ErrorCodes.InvalidState, (await Assert.ThrowsAsync<ApiException>(() => api.CopyExamAsync(exam.Id))).Code);

        await api.PublishExamAsync(exam.Id);
        Assert.Equal(ErrorCodes.InvalidState, (await Assert.ThrowsAsync<ApiException>(() => api.CopyExamAsync(exam.Id))).Code);
        await api.EndExamAsync(exam.Id);

        // Soru bankada yeni sürüme geçse de kopya eski sürümü kullanır.
        await api.UpdateQuestionAsync(question.Id, new SaveQuestionRequest(question.CategoryId, QuestionType.MultipleChoice, "Yeni sürüm", "M", 1, Options, null));

        var copy = await api.CopyExamAsync(exam.Id);

        Assert.NotEqual(exam.Id, copy.Id);
        Assert.Equal(ExamStatus.Draft, copy.Status);
        Assert.Equal("Kopyalanacak (kopya)", copy.Title);
        Assert.Null(copy.StartsAt);
        Assert.Null(copy.EndsAt);
        var row = Assert.Single(copy.Questions);
        Assert.Equal(question.Id, row.QuestionId);
        Assert.Equal(12.5m, row.Points);
        Assert.Equal(ExamStatus.Completed, (await api.GetExamAsync(exam.Id)).Status);

        // Eski sürüm kopyanın listesinde kalabilir ve kopya yayınlanabilir.
        await api.ReplaceExamQuestionsAsync(copy.Id, [new(question.Id, 1, 20)]);
        Assert.Equal(ExamStatus.Published, (await api.PublishExamAsync(copy.Id)).Status);
    }

    [Fact]
    public async Task OldVersion_CannotBeAddedToANewExam()
    {
        var (api, classId) = await InstructorAsync();
        var published = await ExamWithQuestionAsync(api, classId, "Eski sürüm kaynağı");
        await api.PublishExamAsync(published.Id);
        var oldId = published.Questions[0].QuestionId;
        var old = await api.GetQuestionAsync(oldId);
        await api.UpdateQuestionAsync(oldId, new SaveQuestionRequest(old.CategoryId, old.Type, "Yeni hâli", old.Text, old.DefaultPoints, Options, null));

        var draft = await api.CreateExamAsync(ExamRequest(classId, "Eski sürüm hedefi"));
        var error = await Assert.ThrowsAsync<ApiException>(() => api.ReplaceExamQuestionsAsync(draft.Id, [new(oldId, 1, null)]));

        Assert.Equal(ErrorCodes.QuestionNotFound, error.Code);
    }

    [Fact]
    public async Task DraftCanBeUpdated_Deleted_AndKindChanged()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await ExamWithQuestionAsync(api, classId, "Taslak işlemleri");
        var starts = DateTimeOffset.UtcNow.AddDays(1);

        var asAssignment = await api.UpdateExamAsync(exam.Id, AssignmentRequest(classId, starts, starts.AddDays(1)));
        Assert.Equal(ExamKind.Assignment, asAssignment.Kind);
        Assert.Null(asAssignment.DurationMinutes);
        Assert.NotNull(asAssignment.StartsAt);

        var asExam = await api.UpdateExamAsync(exam.Id, ExamRequest(classId, "Yeni ad", 90));
        Assert.Equal(ExamKind.Exam, asExam.Kind);
        Assert.Equal(90, asExam.DurationMinutes);
        Assert.Null(asExam.StartsAt);
        Assert.Equal("Açıklama", asExam.Description);

        await api.DeleteExamAsync(exam.Id);
        Assert.Equal(ErrorCodes.ExamNotFound, (await Assert.ThrowsAsync<ApiException>(() => api.GetExamAsync(exam.Id))).Code);
    }

    [Fact]
    public async Task StaleRowVersion_IsRejected_SoTwoWindowsCannotOverwriteEachOther()
    {
        var (api, classId) = await InstructorAsync();
        var exam = await api.CreateExamAsync(ExamRequest(classId, "İki pencere"));

        var firstWindow = await api.UpdateExamAsync(exam.Id, ExamRequest(classId, "Birinci pencere") with { RowVersion = exam.RowVersion });
        Assert.NotEqual(exam.RowVersion, firstWindow.RowVersion);

        var secondWindow = await Assert.ThrowsAsync<ApiException>(() =>
            api.UpdateExamAsync(exam.Id, ExamRequest(classId, "İkinci pencere") with { RowVersion = exam.RowVersion }));

        Assert.Equal(ErrorCodes.InvalidState, secondWindow.Code);
        Assert.Equal(409, secondWindow.StatusCode);
        Assert.Equal("Birinci pencere", (await api.GetExamAsync(exam.Id)).Title);
    }

    [Fact]
    public async Task List_FiltersByStatusAndClass()
    {
        var (api, classId) = await InstructorAsync();
        var otherClass = await api.CreateClassAsync("Liste şubesi", "Test");
        var inOther = await api.CreateExamAsync(ExamRequest(otherClass.Id, "Başka şube"));
        var published = await ExamWithQuestionAsync(api, classId, "Liste yayın");
        await api.PublishExamAsync(published.Id);

        Assert.Equal(inOther.Id, Assert.Single((await api.GetExamsAsync(classId: otherClass.Id)).Items).Id);
        Assert.Contains((await api.GetExamsAsync(ExamStatus.Published)).Items, e => e.Id == published.Id);
        Assert.DoesNotContain((await api.GetExamsAsync(ExamStatus.Draft)).Items, e => e.Id == published.Id);
    }

    [Fact]
    public async Task ValidationErrors_ReachTheClient()
    {
        var (api, classId) = await InstructorAsync();

        Assert.Equal(ErrorCodes.ClassRequired, (await Assert.ThrowsAsync<ApiException>(() =>
            api.CreateExamAsync(new SaveExamRequest("S", null, ExamKind.Exam, null, 10, null, null)))).Code);
        Assert.Equal(ErrorCodes.ClassNotFound, (await Assert.ThrowsAsync<ApiException>(() =>
            api.CreateExamAsync(ExamRequest(999_999)))).Code);
        Assert.Equal(ErrorCodes.DurationRequired, (await Assert.ThrowsAsync<ApiException>(() =>
            api.CreateExamAsync(new SaveExamRequest("S", null, ExamKind.Exam, classId, null, null, null)))).Code);

        var exam = await api.CreateExamAsync(ExamRequest(classId, "Soru hataları"));
        var question = await NewQuestionAsync(api, "Soru hataları sorusu");
        Assert.Equal(ErrorCodes.DuplicateQuestion, (await Assert.ThrowsAsync<ApiException>(() =>
            api.ReplaceExamQuestionsAsync(exam.Id, [new(question.Id, 1, null), new(question.Id, 2, null)]))).Code);
        Assert.Equal(ErrorCodes.QuestionNotFound, (await Assert.ThrowsAsync<ApiException>(() =>
            api.ReplaceExamQuestionsAsync(exam.Id, [new(999_999, 1, null)]))).Code);
        Assert.Equal(ErrorCodes.ExamNotFound, (await Assert.ThrowsAsync<ApiException>(() => api.PublishExamAsync(999_999))).Code);
    }
}
