using Microsoft.EntityFrameworkCore;
using ProgLab.Client.Core;
using ProgLab.InstructorApp.ViewModels;
using ProgLab.Server.Logging;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Sonuç listesi (VT-35), gönderim ayrıntısı, log paneli (VT-36) ve bunların ekran akışları.</summary>
public sealed class ResultsAndLogsTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly QuestionOptionInput[] Options = [new("Yanlış", false), new("Doğru", true)];

    private sealed record Sitting(ApiClient Api, long SubmissionId, StudentQuestionDto Mcq, StudentQuestionDto Classic);

    private Task<ApiClient> InstructorAsync() => TestAccounts.InstructorAsync(fixture.Server);

    /// <summary>Şıklı 30 puan, klasik 20 puan.</summary>
    private static async Task<int> PublishAsync(ApiClient instructor, string title)
    {
        var classId = (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var mcq = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.MultipleChoice, $"{title} 1", "Şıklı metin", 30, Options, null));
        var classic = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, $"{title} 2", "Klasik metin", 20, null, ["yığın", "stack"]));
        var exam = await instructor.CreateExamAsync(new SaveExamRequest(title, null, ExamKind.Exam, classId, 40, null, null));
        await instructor.ReplaceExamQuestionsAsync(exam.Id, [new(mcq.Id, 1, null), new(classic.Id, 2, null)]);
        await instructor.PublishExamAsync(exam.Id);
        return exam.Id;
    }

    private async Task<Sitting> SitAsync(int examId, int student)
    {
        var api = await TestAccounts.StudentAsync(fixture.Server, student);
        var joined = await api.JoinExamAsync(examId);
        var paper = await api.GetPaperAsync(examId);
        return new Sitting(api, joined.SubmissionId, paper.Questions[0], paper.Questions[1]);
    }

    private static async Task SubmitAsync(Sitting s, bool mcqRight, string? classic)
    {
        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId, new(s.Mcq.Options[mcqRight ? 1 : 0].Id, null));
        if (classic is not null)
        {
            await s.Api.SaveAnswerAsync(s.SubmissionId, s.Classic.ExamQuestionId, new(null, classic));
        }

        await s.Api.SubmitAsync(s.SubmissionId);
    }

    /// <summary>VT-35: 3 öğrencili sınavda puanlar, teslim saatleri ve "Katılmadı" satırları doğru.</summary>
    [Fact]
    public async Task VT35_ResultList_IsCorrect()
    {
        var instructor = await InstructorAsync();
        var examId = await PublishAsync(instructor, "VT35");
        var full = await SitAsync(examId, 1);
        var half = await SitAsync(examId, 2);
        var open = await SitAsync(examId, 3);
        var before = DateTimeOffset.UtcNow;
        await SubmitAsync(full, true, "stack");
        await SubmitAsync(half, true, "yanlış");
        await open.Api.SaveAnswerAsync(open.SubmissionId, open.Mcq.ExamQuestionId, new(open.Mcq.Options[1].Id, null));

        // Sınav sürerken: teslim edenin puanı görünür, çözenin görünmez.
        var running = await instructor.GetExamResultsAsync(examId);
        Assert.Equal(ExamStatus.Published, running.Status);
        Assert.Equal(50m, running.MaxScore);
        Assert.Equal(2, running.TotalQuestions);
        Assert.True(running.Rows.Count >= 30);
        var fullRow = running.Rows.Single(r => r.Username == TestAccounts.StudentNumber(1));
        Assert.Equal(SubmissionStatus.Submitted, fullRow.Status);
        Assert.Equal(50m, fullRow.TotalScore);
        Assert.Equal(2, fullRow.CorrectCount);
        Assert.InRange(fullRow.SubmittedAt!.Value, before.AddSeconds(-2), DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal("Demo Öğrenci 1", fullRow.FullName);
        var halfRow = running.Rows.Single(r => r.Username == TestAccounts.StudentNumber(2));
        Assert.Equal(30m, halfRow.TotalScore);
        Assert.Equal(1, halfRow.CorrectCount);
        var openRow = running.Rows.Single(r => r.Username == TestAccounts.StudentNumber(3));
        Assert.Equal(SubmissionStatus.InProgress, openRow.Status);
        Assert.Null(openRow.TotalScore);
        Assert.Null(openRow.CorrectCount);
        Assert.NotNull(openRow.SubmissionId);
        var absentRow = running.Rows.Single(r => r.Username == TestAccounts.StudentNumber(30));
        Assert.Null(absentRow.Status);
        Assert.Null(absentRow.SubmissionId);

        await instructor.EndExamAsync(examId);

        // Sınav bitince otomatik teslim edilen de puanlıdır.
        var ended = await instructor.GetExamResultsAsync(examId);
        var autoRow = ended.Rows.Single(r => r.Username == TestAccounts.StudentNumber(3));
        Assert.Equal(SubmissionStatus.AutoSubmitted, autoRow.Status);
        Assert.Equal(30m, autoRow.TotalScore);
        Assert.NotNull(autoRow.SubmittedAt);
    }

    [Fact]
    public async Task Results_Errors()
    {
        var instructor = await InstructorAsync();
        var classId = (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;
        var draft = await instructor.CreateExamAsync(new SaveExamRequest("Taslak sonuç", null, ExamKind.Exam, classId, 40, null, null));

        Assert.Equal(ErrorCodes.ExamNotFound, (await Assert.ThrowsAsync<ApiException>(() => instructor.GetExamResultsAsync(draft.Id))).Code);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => instructor.GetExamResultsAsync(999_999))).StatusCode);
        Assert.Equal(ErrorCodes.SubmissionNotFound, (await Assert.ThrowsAsync<ApiException>(() => instructor.GetSubmissionDetailAsync(999_999))).Code);
        Assert.Equal(ErrorCodes.ExamNotFound, (await Assert.ThrowsAsync<ApiException>(() => instructor.GetExamLogsAsync(999_999))).Code);
        Assert.Equal(ErrorCodes.ExamNotFound, (await Assert.ThrowsAsync<ApiException>(() => instructor.GetExamLogFileAsync(999_999))).Code);
    }

    /// <summary>Öğrenci hiçbirine erişemez: ayrıntı doğru cevabı içerir.</summary>
    [Fact]
    public async Task Student_CannotReadResultsOrLogs()
    {
        var instructor = await InstructorAsync();
        var examId = await PublishAsync(instructor, "Yetki");
        var s = await SitAsync(examId, 4);
        await s.Api.SubmitAsync(s.SubmissionId);

        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => s.Api.GetExamResultsAsync(examId))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => s.Api.GetSubmissionDetailAsync(s.SubmissionId))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => s.Api.GetExamLogsAsync(examId))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => s.Api.GetExamLogFileAsync(examId))).StatusCode);
    }

    [Fact]
    public async Task SubmissionDetail_ShowsAnswersKeysAndScores()
    {
        var instructor = await InstructorAsync();
        var examId = await PublishAsync(instructor, "Ayrıntı");
        var s = await SitAsync(examId, 5);
        await SubmitAsync(s, false, "kuyruk");

        var detail = await instructor.GetSubmissionDetailAsync(s.SubmissionId);

        Assert.Equal("Ayrıntı", detail.ExamTitle);
        Assert.Equal(TestAccounts.StudentNumber(5), detail.Username);
        Assert.Equal(SubmissionStatus.Submitted, detail.Status);
        Assert.Equal(0m, detail.TotalScore);
        Assert.Equal(50m, detail.MaxScore);

        var mcq = detail.Questions[0];
        Assert.Equal(QuestionType.MultipleChoice, mcq.Type);
        Assert.Equal("Şıklı metin", mcq.Text);
        Assert.Equal("A) Yanlış", mcq.AnswerText);
        Assert.Equal("B) Doğru", mcq.CorrectAnswer);
        Assert.Equal(0m, mcq.Score);
        Assert.False(mcq.CanOverride);

        var classic = detail.Questions[1];
        Assert.Equal("kuyruk", classic.AnswerText);
        Assert.Equal("yığın / stack", classic.CorrectAnswer);
        Assert.True(classic.CanOverride);
        Assert.NotNull(classic.AnswerId);
    }

    [Fact]
    public async Task SubmissionDetail_EmptyAndInProgress_CannotBeOverridden()
    {
        var instructor = await InstructorAsync();
        var examId = await PublishAsync(instructor, "Boş ayrıntı");
        var s = await SitAsync(examId, 6);
        await s.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq.ExamQuestionId, new(s.Mcq.Options[1].Id, null));

        var running = await instructor.GetSubmissionDetailAsync(s.SubmissionId);
        Assert.Equal(SubmissionStatus.InProgress, running.Status);
        Assert.Null(running.TotalScore);
        Assert.All(running.Questions, q => Assert.False(q.CanOverride));

        await s.Api.SubmitAsync(s.SubmissionId);
        var done = await instructor.GetSubmissionDetailAsync(s.SubmissionId);
        var empty = done.Questions[1];
        Assert.Null(empty.AnswerId);
        Assert.Null(empty.AnswerText);
        Assert.False(empty.CanOverride);
    }

    /// <summary>VT-36 panel kısmı: teslim satırı görünür; öğrenci no, olay ve zaman filtreleri çalışır.</summary>
    [Fact]
    public async Task VT36_LogPanel_ShowsSubmit_AndFilters()
    {
        var instructor = await InstructorAsync();
        var examId = await PublishAsync(instructor, "VT36");
        var a = await SitAsync(examId, 7);
        var b = await SitAsync(examId, 8);
        var started = DateTimeOffset.UtcNow.AddSeconds(-1);
        await SubmitAsync(a, true, "stack");
        await SubmitAsync(b, false, null);

        var all = await instructor.GetExamLogsAsync(examId);
        Assert.Equal(100, all.PageSize);
        Assert.Equal(all.Items.OrderByDescending(i => i.Id).Select(i => i.Id), all.Items.Select(i => i.Id));
        Assert.Contains(all.Items, i => i.EventType == ActivityEvents.ExamPublished && i.Username == "egitmen" && i.FullName == "");

        var submitted = await instructor.GetExamLogsAsync(examId, ActivityEvents.Submitted);
        Assert.Equal(2, submitted.Total);
        Assert.All(submitted.Items, i => Assert.Equal(ActivityEvents.Submitted, i.EventType));

        var onlyA = await instructor.GetExamLogsAsync(examId, ActivityEvents.Submitted, TestAccounts.StudentNumber(7));
        var line = Assert.Single(onlyA.Items);
        Assert.Equal(TestAccounts.StudentNumber(7), line.Username);
        Assert.Equal("Demo Öğrenci 7", line.FullName);
        Assert.Equal("2/2 cevap, 50.00 puan, 1. teslim", line.Text);

        Assert.Empty((await instructor.GetExamLogsAsync(examId, to: started.AddDays(-1))).Items);
        Assert.Empty((await instructor.GetExamLogsAsync(examId, from: DateTimeOffset.UtcNow.AddMinutes(5))).Items);
        Assert.Contains((await instructor.GetExamLogsAsync(examId, ActivityEvents.Submitted, from: started, to: DateTimeOffset.UtcNow.AddMinutes(1))).Items, i => i.Id == line.Id);
    }

    /// <summary>Düzeltme satırının sahibi eğitmendir ama öğrencinin numarasıyla da bulunur.</summary>
    [Fact]
    public async Task LogFilter_FindsScoreOverride_ByStudentNumber()
    {
        var instructor = await InstructorAsync();
        var examId = await PublishAsync(instructor, "Düzeltme logu");
        var s = await SitAsync(examId, 9);
        await SubmitAsync(s, true, "uzun cevap");
        var detail = await instructor.GetSubmissionDetailAsync(s.SubmissionId);
        await instructor.OverrideScoreAsync(detail.Questions[1].AnswerId!.Value, new(12m, "kısmi"));

        var row = Assert.Single((await instructor.GetExamLogsAsync(examId, ActivityEvents.ScoreOverridden, TestAccounts.StudentNumber(9))).Items);

        Assert.Equal("egitmen", row.Username);
        Assert.Equal($"{TestAccounts.StudentNumber(9)}: 0.00 -> 12.00 puan, toplam 42.00", row.Text);
    }

    [Fact]
    public async Task LogPaging_Works()
    {
        var instructor = await InstructorAsync();
        var examId = await PublishAsync(instructor, "Sayfalama");
        await using (var db = fixture.CreateDbContext())
        {
            db.ActivityLogs.AddRange(Enumerable.Range(0, 120).Select(i => new ProgLab.Server.Data.Entities.ActivityLog
            {
                OccurredAt = DateTimeOffset.UtcNow,
                EventType = ActivityEvents.SaveRejected,
                ExamId = examId,
                Details = "{\"summary\": \"satır " + i + "\"}"
            }));
            await db.SaveChangesAsync();
        }

        var first = await instructor.GetExamLogsAsync(examId, ActivityEvents.SaveRejected);
        var second = await instructor.GetExamLogsAsync(examId, ActivityEvents.SaveRejected, page: 2);

        Assert.Equal(120, first.Total);
        Assert.Equal(100, first.Items.Count);
        Assert.Equal(20, second.Items.Count);
        Assert.Equal("satır 119", first.Items[0].Text);
        Assert.Equal("satır 0", second.Items[^1].Text);

        var logs = new LogsViewModel(instructor, examId, () => { }) { SelectedEvent = ActivityEvents.SaveRejected };
        await logs.ApplyCommand.ExecuteAsync(null);
        Assert.Equal("Sayfa 1 / 2 · 120 kayıt", logs.PageText);
        Assert.False(logs.CanGoBack);
        Assert.True(logs.CanGoNext);
        await logs.NextCommand.ExecuteAsync(null);
        Assert.Equal(20, logs.Rows.Count);
        Assert.False(logs.CanGoNext);
        await logs.PreviousCommand.ExecuteAsync(null);
        Assert.Equal(100, logs.Rows.Count);
    }

    [Fact]
    public async Task ResultsScreen_SortsAndOpensDetail()
    {
        var instructor = await InstructorAsync();
        var examId = await PublishAsync(instructor, "Sonuç ekranı");
        var low = await SitAsync(examId, 10);
        var high = await SitAsync(examId, 11);
        var open = await SitAsync(examId, 12);
        await SubmitAsync(low, false, null);
        await Task.Delay(20);
        await SubmitAsync(high, true, "yığın");

        long? opened = null;
        var backs = 0;
        var results = new ResultsViewModel(instructor, examId, id => opened = id, () => backs++);
        await results.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Sonuç ekranı", results.Title);
        Assert.Contains("3 katıldı · 2 teslim", results.SummaryText);
        Assert.Contains("sınav sürüyor", results.SummaryText);
        Assert.Equal(TestAccounts.StudentNumber(1), results.Rows[0].Username);

        var row = results.Rows.Single(r => r.Username == TestAccounts.StudentNumber(11));
        Assert.Equal("Teslim etti", row.StatusText);
        Assert.Equal("2/2", row.CorrectText);
        Assert.Equal("50", row.ScoreText);
        var absent = results.Rows.Single(r => r.Username == TestAccounts.StudentNumber(30));
        Assert.Equal("Katılmadı", absent.StatusText);
        Assert.Equal("—", absent.ScoreText);
        Assert.Equal("—", absent.SubmittedText);
        Assert.Equal("Çözüyor", results.Rows.Single(r => r.Username == TestAccounts.StudentNumber(12)).StatusText);

        results.SortByScore = true;
        Assert.Equal(TestAccounts.StudentNumber(11), results.Rows[0].Username);
        Assert.Equal(TestAccounts.StudentNumber(10), results.Rows[1].Username);
        Assert.Equal("—", results.Rows[2].ScoreText); // Puanı olmayan sonda.

        results.SortBySubmittedAt = true;
        Assert.Equal(TestAccounts.StudentNumber(10), results.Rows[0].Username);
        Assert.Equal(TestAccounts.StudentNumber(11), results.Rows[1].Username);

        results.SortByName = true;
        Assert.Equal("Demo Öğrenci 1", results.Rows[0].FullName);

        results.OpenSubmissionCommand.Execute(null);
        Assert.Equal("Önce listeden bir öğrenci seçin.", results.ErrorMessage);
        results.SelectedRow = absent;
        results.OpenSubmissionCommand.Execute(null);
        Assert.Equal("Bu öğrenci sınava katılmadı.", results.ErrorMessage);
        Assert.Null(opened);
        results.SelectedRow = row;
        results.OpenSubmissionCommand.Execute(null);
        Assert.Equal(high.SubmissionId, opened);

        results.BackCommand.Execute(null);
        Assert.Equal(1, backs);
        _ = open;
    }

    [Fact]
    public async Task DetailScreen_OverridesScore_AndShowsErrors()
    {
        var instructor = await InstructorAsync();
        var examId = await PublishAsync(instructor, "Düzeltme ekranı");
        var s = await SitAsync(examId, 13);
        await SubmitAsync(s, true, "son giren ilk çıkar");

        var detail = new SubmissionDetailViewModel(instructor, s.SubmissionId, () => { });
        await detail.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Toplam: 30 / 50", detail.TotalText);
        Assert.Contains("Demo Öğrenci 13", detail.StudentText);
        Assert.False(detail.Questions[0].CanOverride);
        var classic = detail.Questions[1];
        Assert.True(classic.CanOverride);
        Assert.Equal("0 / 20", classic.ScoreText);

        classic.ScoreInput = "abc";
        await classic.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Puan sayı olmalıdır.", detail.ErrorMessage);

        classic.ScoreInput = "25";
        await classic.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(detail.ErrorMessage);
        Assert.Equal("Toplam: 30 / 50", detail.TotalText);

        classic.ScoreInput = "17,5"; // Virgül de kabul edilir.
        classic.NoteInput = "Anlamca doğru";
        await classic.SaveCommand.ExecuteAsync(null);

        Assert.Null(detail.ErrorMessage);
        Assert.Equal("Toplam: 47.5 / 50".Replace(".", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator), detail.TotalText);
        Assert.Contains("elle düzeltildi", classic.ScoreText);
        Assert.Equal("Anlamca doğru", classic.NoteText);
        Assert.Equal("Soru 2 puanı düzeltildi.", detail.StatusMessage);

        // Yeniden açınca kalıcı.
        var reopened = new SubmissionDetailViewModel(instructor, s.SubmissionId, () => { });
        await reopened.LoadCommand.ExecuteAsync(null);
        Assert.Contains("elle düzeltildi", reopened.Questions[1].ScoreText);
        Assert.Equal("17.5", reopened.Questions[1].ScoreInput);
        Assert.Equal("Anlamca doğru", reopened.Questions[1].NoteInput);
    }

    [Fact]
    public async Task LogsScreen_FiltersAndShowsRawFile()
    {
        var instructor = await InstructorAsync();
        var examId = await PublishAsync(instructor, "Log ekranı");
        var s = await SitAsync(examId, 14);
        await SubmitAsync(s, true, "stack");

        var logs = new LogsViewModel(instructor, examId, () => { });
        await logs.ApplyCommand.ExecuteAsync(null);
        Assert.Contains(logs.Rows, r => r.EventType == ActivityEvents.Submitted);

        logs.StudentFilter = TestAccounts.StudentNumber(14);
        logs.SelectedEvent = ActivityEvents.Submitted;
        await logs.ApplyCommand.ExecuteAsync(null);
        var row = Assert.Single(logs.Rows);
        Assert.Equal("2/2 cevap, 50.00 puan, 1. teslim", row.Text);
        Assert.Matches(@"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}$", row.TimeText);

        logs.FromText = "saçma";
        await logs.ApplyCommand.ExecuteAsync(null);
        Assert.Contains("gg.aa.yyyy", logs.ErrorMessage);

        logs.FromText = DateTime.Now.AddDays(1).ToString("dd.MM.yyyy");
        await logs.ApplyCommand.ExecuteAsync(null);
        Assert.Empty(logs.Rows);
        Assert.Null(logs.ErrorMessage);

        logs.FromText = DateTime.Now.ToString("dd.MM.yyyy");
        logs.ToText = DateTime.Now.ToString("dd.MM.yyyy");
        await logs.ApplyCommand.ExecuteAsync(null);
        Assert.Single(logs.Rows);

        await logs.ShowRawCommand.ExecuteAsync(null);
        Assert.True(logs.IsRawVisible);
        Assert.False(logs.IsTableVisible);
        Assert.Contains("| SUBMITTED ", logs.RawText);
        Assert.Contains(TestAccounts.StudentNumber(14), logs.RawText);
        logs.ShowTableCommand.Execute(null);
        Assert.True(logs.IsTableVisible);
    }

    [Theory]
    [InlineData("", false, true)]
    [InlineData("05.11.2026", false, true)]
    [InlineData("05.11.2026 14:30", true, true)]
    [InlineData("2026-11-05", false, false)]
    [InlineData("32.11.2026", false, false)]
    public void LogTimeFilter_Parsing(string text, bool end, bool valid)
    {
        Assert.Equal(valid, LogsViewModel.TryParseTime(text, end, out _));
    }

    [Fact]
    public void LogTimeFilter_EndCoversWholeMinuteAndDay()
    {
        LogsViewModel.TryParseTime("05.11.2026", false, out var dayStart);
        LogsViewModel.TryParseTime("05.11.2026", true, out var dayEnd);
        LogsViewModel.TryParseTime("05.11.2026 14:30", true, out var minuteEnd);

        Assert.Equal(new DateTime(2026, 11, 5, 0, 0, 0), dayStart!.Value.DateTime);
        Assert.Equal(new DateTime(2026, 11, 5, 23, 59, 59), dayEnd!.Value.DateTime.AddTicks(-(dayEnd.Value.DateTime.Ticks % TimeSpan.TicksPerSecond)));
        Assert.Equal(59, minuteEnd!.Value.Second);
    }

    [Fact]
    public async Task MainWindow_ReachesResultsDetailAndLogs_AndReturnsToSameTab()
    {
        var examId = await PublishAsync(await InstructorAsync(), "Gezinti");
        var s = await SitAsync(examId, 15);
        await SubmitAsync(s, true, "stack");

        var api = new ApiClient(fixture.Server.CreateClient());
        var main = new ProgLab.InstructorApp.ViewModels.MainWindowViewModel(api);
        var login = Assert.IsType<ProgLab.InstructorApp.ViewModels.LoginViewModel>(main.CurrentPage);
        login.Username = "egitmen";
        login.Password = TestAccounts.InstructorPassword;
        await login.LoginCommand.ExecuteAsync(null);

        main.ShowExamsCommand.Execute(null);
        var list = Assert.IsType<ExamListViewModel>(main.CurrentPage);
        Assert.False(list.CanViewResults);
        list.ShowPublished = true;
        await list.TabLoadTask;
        Assert.True(list.CanViewResults);
        list.SelectedExam = list.Exams.Single(e => e.Exam.Id == examId);

        list.OpenResultsCommand.Execute(null);
        var results = Assert.IsType<ResultsViewModel>(main.CurrentPage);
        await results.LoadCommand.ExecutionTask!;
        results.SelectedRow = results.Rows.Single(r => r.Username == TestAccounts.StudentNumber(15));
        results.OpenSubmissionCommand.Execute(null);
        var detail = Assert.IsType<SubmissionDetailViewModel>(main.CurrentPage);
        detail.BackCommand.Execute(null);
        results = Assert.IsType<ResultsViewModel>(main.CurrentPage);
        results.BackCommand.Execute(null);

        // Yayında sekmesine döner.
        list = Assert.IsType<ExamListViewModel>(main.CurrentPage);
        Assert.True(list.ShowPublished);
        await list.LoadCommand.ExecutionTask!;
        list.SelectedExam = list.Exams.Single(e => e.Exam.Id == examId);
        list.OpenLogsCommand.Execute(null);
        var logs = Assert.IsType<LogsViewModel>(main.CurrentPage);
        logs.BackCommand.Execute(null);
        Assert.True(Assert.IsType<ExamListViewModel>(main.CurrentPage).ShowPublished);

        // Menüden girince varsayılan sekme.
        main.ShowExamsCommand.Execute(null);
        Assert.True(Assert.IsType<ExamListViewModel>(main.CurrentPage).ShowDrafts);

        list = Assert.IsType<ExamListViewModel>(main.CurrentPage);
        list.OpenResultsCommand.Execute(null);
        Assert.Equal("Önce listeden bir sınav seçin.", list.ErrorMessage);
        main.LogoutCommand.Execute(null);
    }
}

/// <summary>VT-36 dosya kısmı ve kaybolan dosyanın tablodan üretilmesi.</summary>
public sealed class RawLogFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"proglab-log-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task File_IsServed_AndRegeneratedFromTable_WhenMissing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var server = new ProgLabServerFactory(database.ConnectionString, examLogDirectory: _folder);
        var instructor = await TestAccounts.InstructorAsync(server);
        var classId = (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var question = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, "Dosya", "Klasik", 5, null, ["cevap"]));
        var exam = await instructor.CreateExamAsync(new SaveExamRequest("Dosya", null, ExamKind.Exam, classId, 40, null, null));
        await instructor.ReplaceExamQuestionsAsync(exam.Id, [new(question.Id, 1, null)]);
        await instructor.PublishExamAsync(exam.Id);
        var student = await TestAccounts.StudentAsync(server, 1);
        var joined = await student.JoinExamAsync(exam.Id);
        var eq = (await student.GetPaperAsync(exam.Id)).Questions[0].ExamQuestionId;
        await student.SaveAnswerAsync(joined.SubmissionId, eq, new(null, "cevap"));
        await student.SubmitAsync(joined.SubmissionId);

        var path = Path.Combine(_folder, $"exam-{exam.Id}.log");
        var written = await File.ReadAllTextAsync(path);
        var served = await instructor.GetExamLogFileAsync(exam.Id);
        Assert.Equal(written, served);

        // Kabul kriteri: öğrenci no, ad, IP, sunucu saati, cevap sayısı aynı satırda.
        var line = Assert.Single(served.Split(Environment.NewLine), l => l.Contains("| SUBMITTED "));
        Assert.Contains(TestAccounts.StudentNumber(1), line);
        Assert.Contains("Demo Öğrenci 1", line);
        Assert.Contains("1/1 cevap, 5.00 puan, 1. teslim", line);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} ", line);

        // Dosya silindi: tablodan aynı satırlarla üretilir ve diske de yazılır.
        File.Delete(path);
        var regenerated = await instructor.GetExamLogFileAsync(exam.Id);

        Assert.Equal(written, regenerated);
        Assert.True(File.Exists(path));
        Assert.Equal(written, await File.ReadAllTextAsync(path));

        // Sonraki olay aynı dosyaya eklenir.
        await instructor.EndExamAsync(exam.Id);
        Assert.Contains("| EXAM_ENDED ", await instructor.GetExamLogFileAsync(exam.Id));
        Assert.Contains("| SUBMITTED ", await instructor.GetExamLogFileAsync(exam.Id));
    }

    [Fact]
    public void Summary_FallsBackToRawDetails_ForOlderRows()
    {
        Assert.Equal("metin", ActivityRecorder.SummaryOf("{\"summary\": \"metin\", \"a\": 1}"));
        Assert.Equal("{\"a\": 1}", ActivityRecorder.SummaryOf("{\"a\": 1}"));
        Assert.Equal("", ActivityRecorder.SummaryOf(null));
    }
}
