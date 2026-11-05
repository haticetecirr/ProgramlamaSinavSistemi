using ProgLab.Client.Core;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;
using ProgLab.StudentApp.ViewModels;

namespace ProgLab.Server.Tests;

/// <summary>Sayaç: bilgisayar saatine hiç bakmaz (VT-27'nin otomatik kısmı). Zaman elle ilerletilir.</summary>
public sealed class CountdownClockTests
{
    private TimeSpan _now = TimeSpan.FromHours(5);

    private CountdownClock NewClock() => new(() => _now);

    [Fact]
    public void CountsDownFromServerValue()
    {
        var clock = NewClock();
        clock.Sync(100);
        Assert.Equal(100, clock.RemainingSeconds);

        _now += TimeSpan.FromSeconds(30);
        Assert.Equal(70, clock.RemainingSeconds);
    }

    [Fact]
    public void RoundsUp_AndStopsAtZero()
    {
        var clock = NewClock();
        clock.Sync(10);

        _now += TimeSpan.FromSeconds(9.5);
        Assert.Equal(1, clock.RemainingSeconds);

        _now += TimeSpan.FromSeconds(0.5);
        Assert.Equal(0, clock.RemainingSeconds);

        _now += TimeSpan.FromHours(1);
        Assert.Equal(0, clock.RemainingSeconds);
    }

    [Fact]
    public void TimeSync_CorrectsDrift_InBothDirections()
    {
        var clock = NewClock();
        clock.Sync(100);
        _now += TimeSpan.FromSeconds(10);
        Assert.Equal(90, clock.RemainingSeconds);

        clock.Sync(85);
        Assert.Equal(85, clock.RemainingSeconds);

        clock.Sync(95);
        _now += TimeSpan.FromSeconds(5);
        Assert.Equal(90, clock.RemainingSeconds);
    }

    /// <summary>
    /// VT-27'nin tasarım güvencesi: sayaç sınıfının tek zaman kaynağı monoton Stopwatch'tır;
    /// kaynak kodda DateTime veya DateTimeOffset'in şimdiki zaman okuması yoktur.
    /// Saatin gerçekten değiştirilmesi manuel denemedir.
    /// </summary>
    [Theory]
    [InlineData("src/ProgLab.Client.Core/CountdownClock.cs")]
    [InlineData("src/ProgLab.Client.Core/AnswerSaveQueue.cs")]
    [InlineData("src/ProgLab.StudentApp/ViewModels/ExamPaperViewModel.cs")]
    public void TimerCode_NeverReadsSystemClock(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(SolutionPaths.Root, relativePath));

        Assert.DoesNotContain("DateTime.Now", source);
        Assert.DoesNotContain("DateTime.UtcNow", source);
        Assert.DoesNotContain("DateTimeOffset.Now", source);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", source);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(59, "00:59")]
    [InlineData(2400, "40:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3909, "1:05:09")]
    [InlineData(-5, "00:00")]
    public void Format_IsReadable(int seconds, string expected)
    {
        Assert.Equal(expected, CountdownClock.Format(seconds));
    }
}

/// <summary>Sınav ekranının arkasındaki akış, gerçek API ve hub'a karşı (pencere açmadan).</summary>
public sealed class ExamPaperViewModelTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly QuestionOptionInput[] Options = [new("Bir", false), new("İki", true), new("Üç", false)];

    private TimeSpan _clockNow = TimeSpan.Zero;

    private async Task<int> PublishedExamAsync(string title, int duration = 40)
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var classId = (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var q1 = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.MultipleChoice, $"{title} 1", $"{title} birinci", 10, Options, null));
        var q2 = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, $"{title} 2", $"{title} ikinci", 5, null, ["c"]));
        var q3 = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.MultipleChoice, $"{title} 3", $"{title} üçüncü", 5, Options, null));
        var exam = await instructor.CreateExamAsync(new SaveExamRequest(title, "Açıklama", ExamKind.Exam, classId, duration, null, null));
        await instructor.ReplaceExamQuestionsAsync(exam.Id, [new(q1.Id, 1, null), new(q2.Id, 2, null), new(q3.Id, 3, null)]);
        await instructor.PublishExamAsync(exam.Id);
        return exam.Id;
    }

    private async Task<ExamPaperViewModel> OpenPaperAsync(int examId, int studentIndex, Action? onBack = null)
    {
        var student = await TestAccounts.StudentAsync(fixture.Server, studentIndex);
        await student.JoinExamAsync(examId);
        var paper = new ExamPaperViewModel(student, null, examId, a => a(), onBack ?? (() => { }), new CountdownClock(() => _clockNow));
        await paper.LoadCommand.ExecuteAsync(null);
        return paper;
    }

    [Fact]
    public async Task Paper_LoadsQuestions_AndNavigatesFreely()
    {
        var examId = await PublishedExamAsync("Gezinme");
        var paper = await OpenPaperAsync(examId, 1);

        Assert.Null(paper.ErrorMessage);
        Assert.Equal("Gezinme", paper.Title);
        Assert.Equal("Açıklama", paper.Description);
        Assert.Equal(3, paper.Questions.Count);
        Assert.IsType<MultipleChoiceQuestionViewModel>(paper.Questions[0]);
        Assert.IsType<ClassicQuestionViewModel>(paper.Questions[1]); // Tipe göre farklı şablon.
        Assert.Equal("Soru 1 · 10 puan", paper.CurrentQuestion!.Heading);
        Assert.Equal("1 / 3 · 0 cevaplandı", paper.PositionText);

        // Sınırlarda taşmaz.
        paper.PreviousCommand.Execute(null);
        Assert.Equal(1, paper.CurrentQuestion!.Number);
        paper.NextCommand.Execute(null);
        paper.NextCommand.Execute(null);
        paper.NextCommand.Execute(null);
        Assert.Equal(3, paper.CurrentQuestion!.Number);

        // Listeden serbest geçiş.
        paper.CurrentQuestion = paper.Questions[1];
        Assert.Equal("2 / 3 · 0 cevaplandı", paper.PositionText);
    }

    [Fact]
    public async Task MultipleChoice_KeepsOneSelection_CanClear_AndUpdatesList()
    {
        var examId = await PublishedExamAsync("Şık seçimi");
        var paper = await OpenPaperAsync(examId, 2);
        var question = Assert.IsType<MultipleChoiceQuestionViewModel>(paper.Questions[0]);

        Assert.Equal("A) Bir", question.Options[0].Text);
        Assert.Equal("C) Üç", question.Options[2].Text);
        Assert.Equal("Boş", question.StateText);

        question.Options[0].IsSelected = true;
        Assert.Equal("Cevaplandı", question.StateText);
        Assert.Equal(1, paper.AnsweredCount);

        // Başka şık seçilince öncekinin işareti kalkar.
        question.Options[2].IsSelected = true;
        Assert.False(question.Options[0].IsSelected);
        Assert.Equal(question.Options[2].Id, question.SelectedOptionId);
        Assert.Equal(1, paper.AnsweredCount);

        // İşareti kaldırma.
        question.ClearSelectionCommand.Execute(null);
        Assert.Null(question.SelectedOptionId);
        Assert.Equal("Boş", question.StateText);
        Assert.Equal(0, paper.AnsweredCount);
    }

    [Fact]
    public async Task Classic_BlankIsNotAnswered_AndAnswersStayWhenNavigating()
    {
        var examId = await PublishedExamAsync("Klasik");
        var paper = await OpenPaperAsync(examId, 3);
        var question = Assert.IsType<ClassicQuestionViewModel>(paper.Questions[1]);

        question.TextAnswer = "   ";
        Assert.False(question.IsAnswered);

        question.TextAnswer = "cevabım";
        Assert.Equal("Cevaplandı", question.StateText);
        Assert.Equal(1, paper.AnsweredCount);

        paper.CurrentQuestion = paper.Questions[2];
        paper.CurrentQuestion = paper.Questions[1];
        Assert.Equal("cevabım", Assert.IsType<ClassicQuestionViewModel>(paper.CurrentQuestion).TextAnswer);
    }

    [Fact]
    public async Task Timer_CountsFromServerValue_TurnsUrgent_ThenClosesScreen()
    {
        var examId = await PublishedExamAsync("Sayaç");
        var paper = await OpenPaperAsync(examId, 4);

        Assert.Matches("^(39:[0-5][0-9]|40:00)$", paper.TimeText); // Süre sunucudan gelir.
        Assert.False(paper.IsUrgent);
        Assert.True(paper.IsEditable);

        // Sınavın 5 dakikası kalana kadar ilerlet.
        _clockNow += TimeSpan.FromMinutes(35) + TimeSpan.FromSeconds(1);
        paper.Tick();
        Assert.True(paper.IsUrgent);
        Assert.True(paper.IsEditable);

        // Süre bitti.
        _clockNow += TimeSpan.FromMinutes(6);
        paper.Tick();
        Assert.Equal("00:00", paper.TimeText);
        Assert.True(paper.IsClosed);
        Assert.False(paper.IsEditable);
        Assert.Contains("Süre doldu", paper.ClosedMessage);
    }

    [Fact]
    public async Task SavedAnswers_AreRestoredOnScreen()
    {
        var examId = await PublishedExamAsync("Geri yükleme");
        var student = await TestAccounts.StudentAsync(fixture.Server, 5);
        var joined = await student.JoinExamAsync(examId);
        var first = await student.GetPaperAsync(examId);

        await using (var db = fixture.CreateDbContext())
        {
            db.Answers.AddRange(
                new() { SubmissionId = joined.SubmissionId, ExamQuestionId = first.Questions[0].ExamQuestionId, SelectedOptionId = first.Questions[0].Options[1].Id, UpdatedAt = DateTimeOffset.UtcNow },
                new() { SubmissionId = joined.SubmissionId, ExamQuestionId = first.Questions[1].ExamQuestionId, TextAnswer = "kayıtlı metin", UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var paper = new ExamPaperViewModel(student, null, examId, a => a(), () => { });
        await paper.LoadCommand.ExecuteAsync(null);

        var mcq = Assert.IsType<MultipleChoiceQuestionViewModel>(paper.Questions[0]);
        Assert.Equal(first.Questions[0].Options[1].Id, mcq.SelectedOptionId);
        Assert.Equal("kayıtlı metin", Assert.IsType<ClassicQuestionViewModel>(paper.Questions[1]).TextAnswer);
        Assert.Equal(2, paper.AnsweredCount);
    }

    [Fact]
    public async Task LoadError_IsShownInTurkish()
    {
        var examId = await PublishedExamAsync("Katılmadan aç");
        var student = await TestAccounts.StudentAsync(fixture.Server, 6);

        var paper = new ExamPaperViewModel(student, null, examId, a => a(), () => { });
        await paper.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(paper.ErrorMessage);
        Assert.Empty(paper.Questions);
    }

    [Fact]
    public async Task HubEvents_CorrectTimer_AndExamEndClosesScreen()
    {
        var examId = await PublishedExamAsync("Canlı sayaç");
        var tokens = new TokenStore();
        var student = new ApiClient(fixture.Server.CreateClient(), tokens);
        await student.FirstLoginAsync(TestAccounts.StudentNumber(7), TestAccounts.StudentEmail(7), TestAccounts.StudentPassword);
        await using var hub = TestHubConnection.Create(fixture.Server, tokens);
        await hub.StartAsync();
        await student.JoinExamAsync(examId);

        var paper = new ExamPaperViewModel(student, hub, examId, a => a(), () => { }, new CountdownClock(() => _clockNow));
        await paper.LoadCommand.ExecuteAsync(null);
        paper.Start();
        Assert.Equal("Sunucuya bağlı", paper.ConnectionText);

        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        await instructor.EndExamAsync(examId);

        var waited = 0;
        while (!paper.IsClosed && waited < 20000)
        {
            await Task.Delay(25);
            waited += 25;
        }

        Assert.True(paper.IsClosed);
        Assert.Contains("eğitmen tarafından bitirildi", paper.ClosedMessage);
        Assert.Equal("00:00", paper.TimeText);
        paper.Stop();
    }

    [Fact]
    public async Task Lobby_JoinsAndOpensPaper_ThenBackAndContinue()
    {
        var examId = await PublishedExamAsync("Lobiden giriş");
        var api = new ApiClient(fixture.Server.CreateClient());
        await api.FirstLoginAsync(TestAccounts.StudentNumber(8), TestAccounts.StudentEmail(8), TestAccounts.StudentPassword);
        var opened = new List<int>();
        var lobby = new LobbyViewModel(api, null, "Öğrenci", a => a(), opened.Add);

        await lobby.RefreshCommand.ExecuteAsync(null);
        var row = lobby.Exams.Single(e => e.Exam.Id == examId);
        Assert.True(row.CanJoin);
        Assert.Equal("Katıl", row.JoinText);
        Assert.Equal("Katıl: Lobiden giriş", row.JoinName);

        await row.JoinCommand.ExecuteAsync(null);
        Assert.Equal([examId], opened);
        Assert.Null(lobby.ErrorMessage);

        await lobby.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Devam et", lobby.Exams.Single(e => e.Exam.Id == examId).JoinText);
    }

    [Fact]
    public async Task Lobby_ShowsError_WhenExamEndedBeforeJoinClick()
    {
        var examId = await PublishedExamAsync("Geç tıklama");
        var api = new ApiClient(fixture.Server.CreateClient());
        await api.FirstLoginAsync(TestAccounts.StudentNumber(9), TestAccounts.StudentEmail(9), TestAccounts.StudentPassword);
        var lobby = new LobbyViewModel(api, null, "Öğrenci", a => a());
        await lobby.RefreshCommand.ExecuteAsync(null);
        var row = lobby.Exams.Single(e => e.Exam.Id == examId);

        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        await instructor.EndExamAsync(examId);
        await row.JoinCommand.ExecuteAsync(null);

        Assert.Equal("Sınav süresi doldu.", lobby.ErrorMessage);
        Assert.False(lobby.Exams.Single(e => e.Exam.Id == examId).CanJoin);
        Assert.Equal("Tamamlandı", lobby.Exams.Single(e => e.Exam.Id == examId).StateText);
    }

    [Fact]
    public async Task UpcomingOrCompleted_HasNoJoinButton()
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var classId = (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var question = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, "Düğme", "M", 1, null, ["c"]));
        var starts = DateTimeOffset.UtcNow.AddDays(1);
        var assignment = await instructor.CreateExamAsync(new SaveExamRequest("Yakında düğmesiz", null, ExamKind.Assignment, classId, null, starts, starts.AddDays(1)));
        await instructor.ReplaceExamQuestionsAsync(assignment.Id, [new(question.Id, 1, null)]);
        await instructor.PublishExamAsync(assignment.Id);

        var api = await TestAccounts.StudentAsync(fixture.Server, 10);
        var lobby = new LobbyViewModel(api, null, "Öğrenci", a => a());
        await lobby.RefreshCommand.ExecuteAsync(null);

        var row = lobby.Exams.Single(e => e.Exam.Id == assignment.Id);
        Assert.False(row.CanJoin);
        Assert.Equal("Yakında", row.StateText);
    }

    [Fact]
    public async Task MainWindow_FlowsFromLobbyToPaperAndBack()
    {
        var examId = await PublishedExamAsync("Ana pencere akışı");
        var api = await TestAccounts.StudentAsync(fixture.Server, 11);

        // Giriş ekranından başlamak için aynı öğrenci yeniden girer.
        var fresh = new ApiClient(fixture.Server.CreateClient());
        var main = new MainWindowViewModel(fresh);
        var login = Assert.IsType<LoginViewModel>(main.CurrentPage);
        login.StudentNumber = TestAccounts.StudentNumber(11);
        login.Password = TestAccounts.StudentPassword;
        await login.LoginCommand.ExecuteAsync(null);

        var lobby = Assert.IsType<LobbyViewModel>(main.CurrentPage);
        await lobby.RefreshCommand.ExecuteAsync(null);
        await lobby.Exams.Single(e => e.Exam.Id == examId).JoinCommand.ExecuteAsync(null);

        var paper = Assert.IsType<ExamPaperViewModel>(main.CurrentPage);
        await paper.LoadCommand.ExecutionTask!;
        Assert.Equal("Ana pencere akışı", paper.Title);

        paper.BackToLobbyCommand.Execute(null);
        Assert.IsType<LobbyViewModel>(main.CurrentPage);

        main.LogoutCommand.Execute(null);
        Assert.IsType<LoginViewModel>(main.CurrentPage);
        Assert.NotNull(api);
    }
}
