using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ProgLab.Client.Core;
using ProgLab.InstructorApp.ViewModels;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;
using ProgLab.StudentApp.ViewModels;

namespace ProgLab.Server.Tests;

/// <summary>Otomatik kayıt, teslim ve canlı izleme ekranlarının akışı, gerçek API ve hub'a karşı.</summary>
public sealed class SubmitAndLiveViewModelTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly QuestionOptionInput[] Options = [new("Bir", false), new("İki", true)];

    private async Task<int> PublishedAsync(string title, ExamKind kind = ExamKind.Exam)
    {
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var classId = (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var q1 = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.MultipleChoice, $"{title} 1", "Birinci", 10, Options, null));
        var q2 = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, $"{title} 2", "İkinci", 5, null, ["c"]));
        var request = kind == ExamKind.Exam
            ? new SaveExamRequest(title, null, kind, classId, 40, null, null)
            : new SaveExamRequest(title, null, kind, classId, null, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var exam = await instructor.CreateExamAsync(request);
        await instructor.ReplaceExamQuestionsAsync(exam.Id, [new(q1.Id, 1, null), new(q2.Id, 2, null)]);
        await instructor.PublishExamAsync(exam.Id);
        return exam.Id;
    }

    private async Task<(ExamPaperViewModel Paper, List<ExamFinishedInfo> Finished, ApiClient Api)> OpenAsync(int examId, int student)
    {
        var api = await TestAccounts.StudentAsync(fixture.Server, student);
        await api.JoinExamAsync(examId);
        var finished = new List<ExamFinishedInfo>();
        var paper = new ExamPaperViewModel(api, null, examId, a => a(), () => { }, null, finished.Add, Debounce);
        await paper.LoadCommand.ExecuteAsync(null);
        return (paper, finished, api);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!Holds(condition))
        {
            Assert.True(clock.Elapsed < Timeout, "Beklenen durum zamanında oluşmadı.");
            await Task.Delay(10);
        }
    }

    /// <summary>Testte arayüz iş parçacığı yok: liste o anda tazeleniyorsa bir sonraki denemeye bırakılır.</summary>
    private static bool Holds(Func<bool> condition)
    {
        try
        {
            return condition();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [Fact]
    public async Task ChangingAnswers_SavesAutomatically_AndStatusShowsIt()
    {
        var examId = await PublishedAsync("Otomatik kayıt");
        var (paper, _, api) = await OpenAsync(examId, 1);
        Assert.Equal("Tüm cevaplar kaydedildi", paper.SaveStatusText);

        var mcq = Assert.IsType<MultipleChoiceQuestionViewModel>(paper.Questions[0]);
        var classic = Assert.IsType<ClassicQuestionViewModel>(paper.Questions[1]);
        mcq.Options[1].IsSelected = true;
        classic.TextAnswer = "yazdım";

        await WaitUntilAsync(() => paper.SaveStatusText == "Tüm cevaplar kaydedildi");
        await WaitUntilAsync(() => CountAnswers(examId) == 2);

        // Uygulama kapanıp açılınca (yeni ekran) cevaplar sunucudan gelir: VT-19'un cevap kısmı.
        var reopened = new ExamPaperViewModel(api, null, examId, a => a(), () => { });
        await reopened.LoadCommand.ExecuteAsync(null);
        Assert.Equal(mcq.Options[1].Id, Assert.IsType<MultipleChoiceQuestionViewModel>(reopened.Questions[0]).SelectedOptionId);
        Assert.Equal("yazdım", Assert.IsType<ClassicQuestionViewModel>(reopened.Questions[1]).TextAnswer);
        Assert.Equal(2, reopened.AnsweredCount);
        paper.Stop();
        reopened.Stop();
    }

    private int CountAnswers(int examId)
    {
        using var db = fixture.CreateDbContext();
        return db.Answers.Count(a => a.Submission!.ExamId == examId);
    }

    [Fact]
    public async Task Submit_AsksConfirmation_WarnsEmpty_SendsPending_ThenFinishes()
    {
        var examId = await PublishedAsync("Teslim ekranı");
        var api = await TestAccounts.StudentAsync(fixture.Server, 2);
        await api.JoinExamAsync(examId);
        var finished = new List<ExamFinishedInfo>();
        // Bekletme uzun: teslimin bekleyen cevabı kendisinin gönderdiği görülsün.
        var paper = new ExamPaperViewModel(api, null, examId, a => a(), () => { }, null, finished.Add, TimeSpan.FromMinutes(5));
        await paper.LoadCommand.ExecuteAsync(null);

        var classic = Assert.IsType<ClassicQuestionViewModel>(paper.Questions[1]);
        classic.TextAnswer = "son anda yazdım";

        paper.AskSubmitCommand.Execute(null);
        Assert.True(paper.IsConfirmingSubmit);
        Assert.StartsWith("1 soru boş.", paper.SubmitWarning);
        Assert.Contains("değiştiremezsiniz", paper.SubmitWarning);

        paper.CancelSubmitCommand.Execute(null);
        Assert.False(paper.IsConfirmingSubmit);
        Assert.Empty(finished);

        paper.AskSubmitCommand.Execute(null);
        await paper.ConfirmSubmitCommand.ExecuteAsync(null);

        var info = Assert.Single(finished);
        Assert.Equal("Teslim ekranı", info.Title);
        Assert.StartsWith("Teslim alındı:", info.Headline);
        Assert.Equal("1/2 soru cevaplandı.", info.Detail);

        await using var db = fixture.CreateDbContext();
        var submission = await db.Submissions.Include(s => s.Answers).SingleAsync(s => s.ExamId == examId);
        Assert.Equal(SubmissionStatus.Submitted, submission.Status);
        Assert.Equal("son anda yazdım", Assert.Single(submission.Answers).TextAnswer);
        paper.Stop();
    }

    [Fact]
    public async Task Assignment_Message_AllowsResubmit_AndLobbyOffersEditing()
    {
        var examId = await PublishedAsync("Ödev teslimi", ExamKind.Assignment);
        var (paper, finished, api) = await OpenAsync(examId, 3);

        paper.AskSubmitCommand.Execute(null);
        Assert.Contains("yeniden teslim edebilirsiniz", paper.SubmitWarning);
        await paper.ConfirmSubmitCommand.ExecuteAsync(null);
        Assert.Contains("yeniden teslim edebilirsiniz", Assert.Single(finished).Detail);
        paper.Stop();

        var lobby = new LobbyViewModel(api, null, "Öğrenci", a => a());
        await lobby.RefreshCommand.ExecuteAsync(null);
        var row = lobby.Exams.Single(e => e.Exam.Id == examId);
        Assert.True(row.CanJoin);
        Assert.Equal("Yeniden düzenle", row.JoinText);
        Assert.Equal("Teslim edildi", row.StateText);
    }

    [Fact]
    public async Task SubmittedExam_HasNoButtonInLobby()
    {
        var examId = await PublishedAsync("Sınav teslimi");
        var (paper, _, api) = await OpenAsync(examId, 4);
        paper.AskSubmitCommand.Execute(null);
        await paper.ConfirmSubmitCommand.ExecuteAsync(null);
        paper.Stop();

        var lobby = new LobbyViewModel(api, null, "Öğrenci", a => a());
        await lobby.RefreshCommand.ExecuteAsync(null);
        var row = lobby.Exams.Single(e => e.Exam.Id == examId);
        Assert.False(row.CanJoin);
        Assert.Equal("Teslim edildi", row.StateText);
    }

    [Fact]
    public async Task ServerClosesExam_DuringSave_ScreenClosesEvenIfLocalTimerIsBehind()
    {
        var examId = await PublishedAsync("Sayaç geride");
        var (paper, finished, _) = await OpenAsync(examId, 5);

        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        await instructor.EndExamAsync(examId);

        Assert.IsType<ClassicQuestionViewModel>(paper.Questions[1]).TextAnswer = "geç kaldı";
        await WaitUntilAsync(() => paper.IsClosed);

        Assert.Contains("Süre doldu", paper.ClosedMessage);
        await WaitUntilAsync(() => finished.Count == 1);
        Assert.Contains("otomatik olarak teslim", finished[0].Detail);
        paper.Stop();
    }

    [Fact]
    public async Task OfflineStudent_SeesBanner_CannotSubmit_AndAnswerGoesWhenBack()
    {
        var examId = await PublishedAsync("Kopuk öğrenci");
        var login = await new ApiClient(fixture.Server.CreateClient()).FirstLoginAsync(TestAccounts.StudentNumber(6), TestAccounts.StudentEmail(6), TestAccounts.StudentPassword);
        var switchable = new SwitchableHandler(fixture.Server.Server.CreateHandler());
        var tokens = new TokenStore();
        tokens.Set(login.Token, login.ExpiresAt);
        var api = new ApiClient(new HttpClient(switchable) { BaseAddress = new Uri("http://localhost/") }, tokens);
        await api.JoinExamAsync(examId);
        var backs = 0;
        var paper = new ExamPaperViewModel(api, null, examId, a => a(), () => backs++, null, null, Debounce, TimeSpan.FromMilliseconds(50));
        await paper.LoadCommand.ExecuteAsync(null);

        switchable.IsOffline = true;
        Assert.IsType<ClassicQuestionViewModel>(paper.Questions[1]).TextAnswer = "kablo çekikken yazıldı";
        await WaitUntilAsync(() => paper.IsSaveOffline);
        Assert.Contains("bağlantı gelince gönderilecek", paper.SaveStatusText);

        // Kopukken teslim edilemez.
        paper.AskSubmitCommand.Execute(null);
        await paper.ConfirmSubmitCommand.ExecuteAsync(null);
        Assert.StartsWith("Bağlantı yok, teslim edilemedi", paper.ErrorMessage);

        // Lobiye dönerken uyarır, ikinci basışta çıkar.
        await paper.BackToLobbyCommand.ExecuteAsync(null);
        Assert.Contains("kaybolur", paper.ErrorMessage);
        Assert.Equal(0, backs);

        switchable.IsOffline = false;
        await WaitUntilAsync(() => !paper.IsSaveOffline && paper.SaveStatusText == "Tüm cevaplar kaydedildi");
        await WaitUntilAsync(() => CountAnswers(examId) == 1);

        paper.AskSubmitCommand.Execute(null);
        await paper.ConfirmSubmitCommand.ExecuteAsync(null);
        await using var db = fixture.CreateDbContext();
        var submission = await db.Submissions.Include(s => s.Answers).SingleAsync(s => s.ExamId == examId);
        Assert.Equal(SubmissionStatus.Submitted, submission.Status);
        Assert.Equal("kablo çekikken yazıldı", Assert.Single(submission.Answers).TextAnswer);
        paper.Stop();
    }

    [Fact]
    public async Task MainWindow_ReachesSubmittedScreen_AndBackToLobby()
    {
        var examId = await PublishedAsync("Teslim akışı");
        var api = new ApiClient(fixture.Server.CreateClient());
        var main = new ProgLab.StudentApp.ViewModels.MainWindowViewModel(api);
        var login = Assert.IsType<ProgLab.StudentApp.ViewModels.LoginViewModel>(main.CurrentPage);
        login.GoToFirstLoginCommand.Execute(null);
        var first = Assert.IsType<FirstLoginViewModel>(main.CurrentPage);
        first.StudentNumber = TestAccounts.StudentNumber(7);
        first.Email = TestAccounts.StudentEmail(7);
        first.NewPassword = first.NewPasswordRepeat = TestAccounts.StudentPassword;
        await first.SubmitCommand.ExecuteAsync(null);

        var lobby = Assert.IsType<LobbyViewModel>(main.CurrentPage);
        await lobby.RefreshCommand.ExecuteAsync(null);
        await lobby.Exams.Single(e => e.Exam.Id == examId).JoinCommand.ExecuteAsync(null);

        var paper = Assert.IsType<ExamPaperViewModel>(main.CurrentPage);
        await paper.LoadCommand.ExecutionTask!;
        paper.AskSubmitCommand.Execute(null);
        await paper.ConfirmSubmitCommand.ExecuteAsync(null);

        var submitted = Assert.IsType<SubmittedViewModel>(main.CurrentPage);
        Assert.Equal("Teslim akışı", submitted.Title);
        Assert.StartsWith("Teslim alındı:", submitted.Headline);

        submitted.BackToLobbyCommand.Execute(null);
        Assert.IsType<LobbyViewModel>(main.CurrentPage);
        main.LogoutCommand.Execute(null);
    }

    [Fact]
    public async Task LiveScreen_ShowsStudents_UpdatesLive_AndEnds()
    {
        var examId = await PublishedAsync("Canlı ekran");
        await TestAccounts.InstructorAsync(fixture.Server);
        var tokens = new TokenStore();
        var instructorApi = new ApiClient(fixture.Server.CreateClient(), tokens);
        await instructorApi.LoginAsync("egitmen", TestAccounts.InstructorPassword, UserRole.Instructor);
        await using var hub = TestHubConnection.Create(fixture.Server, tokens);
        await hub.StartAsync();

        var backs = 0;
        var live = new LiveExamViewModel(instructorApi, hub, examId, a => a(), () => backs++);
        live.Start();
        await live.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Canlı ekran", live.Title);
        Assert.True(live.IsRunning);
        Assert.Matches("^(39:[0-5][0-9]|40:00)$", live.TimeText);
        Assert.True(live.Students.Count >= 30);
        var waiting = live.Students.First(s => s.Username == TestAccounts.StudentNumber(30));
        Assert.Equal("Katılmadı", waiting.StatusText);
        Assert.Equal("0/2", waiting.ProgressText);

        var api = await TestAccounts.StudentAsync(fixture.Server, 8);
        var joined = await api.JoinExamAsync(examId);
        var paper = await api.GetPaperAsync(examId);

        // Katılım tabloya kendiliğinden yansır.
        LiveStudentRow Row() => live.Students.Single(s => s.Username == TestAccounts.StudentNumber(8));
        await WaitUntilAsync(() => Row().StatusText == "Çözüyor");

        // İlerleme satırda anında güncellenir.
        await api.SaveAnswerAsync(joined.SubmissionId, paper.Questions[1].ExamQuestionId, new(null, "c"));
        await WaitUntilAsync(() => Row().ProgressText == "1/2");

        // Teslim tabloyu tazeler.
        await api.SubmitAsync(joined.SubmissionId);
        await WaitUntilAsync(() => Row().StatusText.StartsWith("Teslim etti"));
        await WaitUntilAsync(() => live.SummaryText.Contains("1 teslim"));

        // Bitirme onay ister.
        live.AskEndCommand.Execute(null);
        Assert.True(live.IsConfirmingEnd);
        live.CancelEndCommand.Execute(null);
        Assert.False(live.IsConfirmingEnd);
        Assert.True(live.IsRunning);

        live.AskEndCommand.Execute(null);
        await live.ConfirmEndCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => !live.IsRunning);
        Assert.Equal("00:00", live.TimeText);

        live.BackCommand.Execute(null);
        Assert.Equal(1, backs);
        live.Stop();
    }

    /// <summary>Ağ kablosunu çekmeyi taklit eder: kapalıyken her istek bağlantı hatası verir.</summary>
    private sealed class SwitchableHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public volatile bool IsOffline;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            IsOffline ? throw new HttpRequestException("Ağ yok") : base.SendAsync(request, ct);
    }
}
