using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProgLab.Client.Core;
using ProgLab.InstructorApp.ViewModels;
using ProgLab.Server.Services;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;
using Student = ProgLab.StudentApp.ViewModels;

namespace ProgLab.Server.Tests;

/// <summary>SignalR bildirimleri (VT-17'nin otomatik kısmı) ve lobi (VT-18). Gerçek hub, bellekteki sunucu üzerinden.</summary>
public sealed class RealtimeTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>, IAsyncLifetime
{
    // Bekleme üst sınırı: başarılı testi yavaşlatmaz, yalnızca bilgisayar yoğunken yanlış alarmı önler.
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(20);
    private static readonly QuestionOptionInput[] Options = [new("Doğru", true), new("Yanlış", false)];

    private readonly List<ExamHubClient> _hubs = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var hub in _hubs)
        {
            await hub.DisposeAsync();
        }
    }

    private ExamHubClient NewHub(TokenStore tokens)
    {
        var hub = TestHubConnection.Create(fixture.Server, tokens);
        _hubs.Add(hub);
        return hub;
    }

    private async Task<(ApiClient Api, ExamHubClient Hub)> StudentSessionAsync(string number, string email)
    {
        var tokens = new TokenStore();
        var api = new ApiClient(fixture.Server.CreateClient(), tokens);
        await api.FirstLoginAsync(number, email, TestAccounts.StudentPassword);
        return (api, NewHub(tokens));
    }

    private Task<(ApiClient Api, ExamHubClient Hub)> DemoStudentAsync(int index) =>
        StudentSessionAsync(TestAccounts.StudentNumber(index), TestAccounts.StudentEmail(index));

    private async Task<(ApiClient Api, ExamHubClient Hub)> InstructorSessionAsync()
    {
        await TestAccounts.InstructorAsync(fixture.Server); // Geçici parolayı değiştirir.
        var tokens = new TokenStore();
        var api = new ApiClient(fixture.Server.CreateClient(), tokens);
        await api.LoginAsync("egitmen", TestAccounts.InstructorPassword, UserRole.Instructor);
        return (api, NewHub(tokens));
    }

    private static async Task<int> ClassAIdAsync(ApiClient instructor) =>
        (await instructor.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;

    private static async Task<InstructorExamDto> ExamWithQuestionAsync(ApiClient instructor, int classId, string title, int duration = 40)
    {
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var question = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.MultipleChoice, $"{title} sorusu", "Metin", 10, Options, null));
        var exam = await instructor.CreateExamAsync(new SaveExamRequest(title, null, ExamKind.Exam, classId, duration, null, null));
        return await instructor.ReplaceExamQuestionsAsync(exam.Id, [new(question.Id, 1, null)]);
    }

    /// <summary>VT-17: yayınlama bildirimi şubedeki öğrencilere 1 sn içinde ulaşır, başka şubeye gitmez.</summary>
    [Fact]
    public async Task VT17_Publish_ReachesAllClassStudentsWithinOneSecond_AndNoOthers()
    {
        var (instructor, _) = await InstructorSessionAsync();
        var classId = await ClassAIdAsync(instructor);
        var outsiderClass = await instructor.CreateClassAsync("VT17 başka şube", "Test");
        await instructor.CreateStudentAsync("2026100001", "Başka Şube Öğrencisi", "baska1@ogr.demo.local");
        var outsiderId = (await instructor.GetStudentsAsync(search: "2026100001")).Items.Single().Id;
        await instructor.ReplaceClassMembersAsync(outsiderClass.Id, [outsiderId]);

        var students = new[] { await DemoStudentAsync(1), await DemoStudentAsync(2), await DemoStudentAsync(3) };
        var outsider = await StudentSessionAsync("2026100001", "baska1@ogr.demo.local");

        var received = students.Select(_ => new TaskCompletionSource<ExamPublishedEvent>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var outsiderReceived = new TaskCompletionSource<ExamPublishedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        for (var i = 0; i < students.Length; i++)
        {
            var slot = received[i];
            students[i].Hub.ExamPublished += e => slot.TrySetResult(e);
            await students[i].Hub.StartAsync();
        }

        outsider.Hub.ExamPublished += e => outsiderReceived.TrySetResult(e);
        await outsider.Hub.StartAsync();

        var exam = await ExamWithQuestionAsync(instructor, classId, "VT17 sınavı");
        var clock = Stopwatch.StartNew();
        await instructor.PublishExamAsync(exam.Id);
        var events = await Task.WhenAll(received.Select(r => r.Task)).WaitAsync(EventTimeout);
        clock.Stop();

        Assert.InRange(clock.ElapsedMilliseconds, 0, 1000);
        Assert.All(events, e =>
        {
            Assert.Equal(exam.Id, e.ExamId);
            Assert.Equal("VT17 sınavı", e.Title);
            Assert.Equal(ExamKind.Exam, e.Kind);
            Assert.True(e.StartsAtUtc < e.EndsAtUtc);
        });

        // Başka şubenin öğrencisine bildirim gitmez.
        await Task.Delay(300);
        Assert.False(outsiderReceived.Task.IsCompleted);
    }

    /// <summary>
    /// Bağlandıktan hemen sonra yayınlanan sınav kaçmamalı. Bu test Ready sinyalinden önce
    /// rastgele başarısız oluyordu: istemci "bağlandım" dediğinde sunucu henüz gruba eklememişti.
    /// </summary>
    [Fact]
    public async Task PublishRightAfterConnecting_IsNotMissed()
    {
        var (instructor, _) = await InstructorSessionAsync();
        var classId = await ClassAIdAsync(instructor);
        var (_, hub) = await DemoStudentAsync(13);
        var received = new TaskCompletionSource<ExamPublishedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.ExamPublished += e => received.TrySetResult(e);
        var exam = await ExamWithQuestionAsync(instructor, classId, "Bağlanır bağlanmaz");

        await hub.StartAsync();
        Assert.True(hub.IsConnected);
        await instructor.PublishExamAsync(exam.Id);

        Assert.Equal(exam.Id, (await received.Task.WaitAsync(EventTimeout)).ExamId);
    }

    [Fact]
    public void PublishEvent_CarriesNoQuestionContent()
    {
        var json = JsonSerializer.Serialize(new ExamPublishedEvent(1, "Başlık", ExamKind.Exam, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        Assert.DoesNotContain("question", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("option", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correct", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExamEnded_ReachesStudentsAndInstructor_WithManualReason()
    {
        var (instructor, instructorHub) = await InstructorSessionAsync();
        var (_, studentHub) = await DemoStudentAsync(4);
        var exam = await ExamWithQuestionAsync(instructor, await ClassAIdAsync(instructor), "Bitiş bildirimi");
        await instructor.PublishExamAsync(exam.Id);

        var toStudent = new TaskCompletionSource<ExamEndedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var toInstructor = new TaskCompletionSource<ExamEndedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        studentHub.ExamEnded += e => toStudent.TrySetResult(e);
        instructorHub.ExamEnded += e => toInstructor.TrySetResult(e);
        await studentHub.StartAsync();
        await instructorHub.StartAsync();

        await instructor.EndExamAsync(exam.Id);

        Assert.Equal(ExamNotifier.ReasonManual, (await toStudent.Task.WaitAsync(EventTimeout)).Reason);
        Assert.Equal(exam.Id, (await toInstructor.Task.WaitAsync(EventTimeout)).ExamId);
    }

    [Fact]
    public async Task Instructor_SeesStudentConnectAndDisconnect_AndCurrentList()
    {
        var (instructor, instructorHub) = await InstructorSessionAsync();
        var connected = new TaskCompletionSource<StudentConnectionDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<StudentConnectionDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (studentApi, studentHub) = await DemoStudentAsync(5);
        var studentId = (await studentApi.GetMeAsync()).Id;
        instructorHub.StudentConnected += s => { if (s.StudentId == studentId) { connected.TrySetResult(s); } };
        instructorHub.StudentDisconnected += s => { if (s.StudentId == studentId) { disconnected.TrySetResult(s); } };
        await instructorHub.StartAsync();

        await studentHub.StartAsync();
        Assert.Equal("Demo Öğrenci 5", (await connected.Task.WaitAsync(EventTimeout)).FullName);
        Assert.Contains(await instructor.GetConnectedStudentsAsync(), s => s.StudentId == studentId);

        await studentHub.StopAsync();
        Assert.Equal(studentId, (await disconnected.Task.WaitAsync(EventTimeout)).StudentId);
        Assert.DoesNotContain(await instructor.GetConnectedStudentsAsync(), s => s.StudentId == studentId);
    }

    [Fact]
    public async Task SecondConnectionOfSameStudent_DoesNotDuplicate_AndClosingOneKeepsStudentOnline()
    {
        var (instructor, _) = await InstructorSessionAsync();
        var tokens = new TokenStore();
        var api = new ApiClient(fixture.Server.CreateClient(), tokens);
        await api.FirstLoginAsync(TestAccounts.StudentNumber(6), TestAccounts.StudentEmail(6), TestAccounts.StudentPassword);
        var studentId = (await api.GetMeAsync()).Id;

        var first = NewHub(tokens);
        var second = NewHub(tokens);
        await first.StartAsync();
        await second.StartAsync();
        Assert.Single(await instructor.GetConnectedStudentsAsync(), s => s.StudentId == studentId);

        await first.StopAsync();
        await Task.Delay(100);
        Assert.Single(await instructor.GetConnectedStudentsAsync(), s => s.StudentId == studentId);

        await second.StopAsync();
        await Task.Delay(100);
        Assert.DoesNotContain(await instructor.GetConnectedStudentsAsync(), s => s.StudentId == studentId);
    }

    [Fact]
    public async Task Hub_RejectsMissingAndForgedTokens()
    {
        var noToken = NewHub(new TokenStore());
        await Assert.ThrowsAnyAsync<Exception>(() => noToken.StartAsync());
        Assert.False(noToken.IsConnected);

        var forgedTokens = new TokenStore();
        forgedTokens.Set("sahte.token.degeri", DateTimeOffset.UtcNow.AddHours(1));
        var forged = NewHub(forgedTokens);
        await Assert.ThrowsAnyAsync<Exception>(() => forged.StartAsync());
        Assert.False(forged.IsConnected);
    }

    [Fact]
    public async Task Hub_DropsDisabledAccount()
    {
        var (instructor, _) = await InstructorSessionAsync();
        var (studentApi, hub) = await DemoStudentAsync(7);
        var studentId = (await studentApi.GetMeAsync()).Id;
        await using (var db = fixture.CreateDbContext())
        {
            await db.Users.Where(u => u.Id == studentId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        }

        try
        {
            await hub.StartAsync();
        }
        catch (Exception)
        {
            // Sunucu bağlantıyı el sıkışma sırasında da kapatabilir; sonuç aynıdır.
        }

        await WaitUntilAsync(() => !hub.IsConnected);
        Assert.DoesNotContain(await instructor.GetConnectedStudentsAsync(), s => s.StudentId == studentId);
    }

    /// <summary>VT-18'in lobi kısmı. join 403 kontrolü, join yazılınca Aşama 7'de eklenecek.</summary>
    [Fact]
    public async Task VT18_Lobby_ShowsOnlyOwnClassExams_AndNeverDrafts()
    {
        var (instructor, _) = await InstructorSessionAsync();
        var classId = await ClassAIdAsync(instructor);
        var draft = await ExamWithQuestionAsync(instructor, classId, "Lobi taslağı");
        var published = await ExamWithQuestionAsync(instructor, classId, "Lobi yayını");
        await instructor.PublishExamAsync(published.Id);

        var otherClass = await instructor.CreateClassAsync("Lobi başka şube", "Test");
        await instructor.CreateStudentAsync("2026100002", "Lobi Dışı Öğrenci", "baska2@ogr.demo.local");
        var outsiderId = (await instructor.GetStudentsAsync(search: "2026100002")).Items.Single().Id;
        await instructor.ReplaceClassMembersAsync(otherClass.Id, [outsiderId]);

        var member = await DemoStudentAsync(8);
        var outsider = await StudentSessionAsync("2026100002", "baska2@ogr.demo.local");

        var memberLobby = await member.Api.GetLobbyAsync();
        var row = memberLobby.Single(e => e.Id == published.Id);
        Assert.Equal(StudentExamState.Open, row.State);
        Assert.InRange(row.RemainingSeconds!.Value, 1, 40 * 60);
        Assert.DoesNotContain(memberLobby, e => e.Id == draft.Id);

        Assert.DoesNotContain(await outsider.Api.GetLobbyAsync(), e => e.Id == published.Id);
    }

    [Fact]
    public async Task Lobby_DerivesUpcomingOpenCompleted_FromServerClock()
    {
        var (instructor, _) = await InstructorSessionAsync();
        var classId = await ClassAIdAsync(instructor);
        var student = await DemoStudentAsync(9);
        var category = (await instructor.GetCategoriesAsync())[0].Id;
        var question = await instructor.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, "Ödev lobi sorusu", "M", 1, null, ["c"]));

        var starts = DateTimeOffset.UtcNow.AddDays(1);
        var assignment = await instructor.CreateExamAsync(new SaveExamRequest("Yakında ödev", null, ExamKind.Assignment, classId, null, starts, starts.AddDays(1)));
        await instructor.ReplaceExamQuestionsAsync(assignment.Id, [new(question.Id, 1, null)]);
        await instructor.PublishExamAsync(assignment.Id);

        var upcoming = (await student.Api.GetLobbyAsync()).Single(e => e.Id == assignment.Id);
        Assert.Equal(StudentExamState.Upcoming, upcoming.State);
        Assert.Null(upcoming.RemainingSeconds);

        await instructor.EndExamAsync(assignment.Id);
        var completed = (await student.Api.GetLobbyAsync()).Single(e => e.Id == assignment.Id);
        Assert.Equal(StudentExamState.Completed, completed.State);
    }

    [Fact]
    public async Task LobbyAndConnections_AreRoleProtected()
    {
        var (instructor, _) = await InstructorSessionAsync();
        var student = await DemoStudentAsync(10);

        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => instructor.GetLobbyAsync())).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => student.Api.GetConnectedStudentsAsync())).StatusCode);
    }

    [Fact]
    public async Task LobbyScreen_UpdatesItself_WhenExamIsPublishedAndEnded()
    {
        var (instructor, _) = await InstructorSessionAsync();
        var classId = await ClassAIdAsync(instructor);
        var student = await DemoStudentAsync(11);
        var lobby = new Student.LobbyViewModel(student.Api, student.Hub, "Demo Öğrenci 11", a => a());

        lobby.Start();
        await WaitUntilAsync(() => lobby.IsLoaded && student.Hub.IsConnected);
        Assert.Equal("Sunucuya bağlı", lobby.ConnectionText);

        var exam = await ExamWithQuestionAsync(instructor, classId, "Canlı lobi sınavı");
        await instructor.PublishExamAsync(exam.Id);
        await WaitUntilAsync(() => lobby.Exams.Any(e => e.Title == "Canlı lobi sınavı"));

        var row = lobby.Exams.Single(e => e.Title == "Canlı lobi sınavı");
        Assert.Equal("Açık", row.StateText);
        Assert.Equal("Sınav", row.KindText);
        Assert.StartsWith("Kalan süre: 40 dk", row.TimeText);

        await instructor.EndExamAsync(exam.Id);
        await WaitUntilAsync(() => lobby.Exams.Any(e => e.Title == "Canlı lobi sınavı" && e.StateText == "Tamamlandı"));

        lobby.Stop();
    }

    [Fact]
    public async Task Dashboard_ShowsLiveStudents_AndActiveExams()
    {
        var (instructor, instructorHub) = await InstructorSessionAsync();
        var exam = await ExamWithQuestionAsync(instructor, await ClassAIdAsync(instructor), "Pano sınavı");
        await instructor.PublishExamAsync(exam.Id);

        var main = new MainWindowViewModel(instructor, instructorHub);
        main.ShowDashboardCommand.Execute(null);
        var dashboard = Assert.IsType<DashboardViewModel>(main.CurrentPage);
        await dashboard.LoadCommand.ExecutionTask!;
        await WaitUntilAsync(() => instructorHub.IsConnected);

        Assert.Null(dashboard.ErrorMessage);
        Assert.Contains(dashboard.ActiveExams, e => e.Title == "Pano sınavı");
        Assert.Contains("Canlı bağlantı açık", main.Live.StatusBarText);

        var (_, studentHub) = await DemoStudentAsync(12);
        await studentHub.StartAsync();
        await WaitUntilAsync(() => main.Live.Students.Any(s => s.FullName == "Demo Öğrenci 12"));
        Assert.Contains($"Bağlı öğrenci: {main.Live.ConnectedCount}", main.Live.StatusBarText);

        await studentHub.StopAsync();
        await WaitUntilAsync(() => main.Live.Students.All(s => s.FullName != "Demo Öğrenci 12"));

        main.LogoutCommand.Execute(null);
        Assert.Empty(main.Live.Students);
    }

    /// <summary>Pencere denemesinde bulundu: ilk şubenin kendiliğinden seçilmesi sınavı yanlış şubeye gönderiyordu.</summary>
    [Fact]
    public async Task ExamEditor_WithSeveralClasses_DoesNotPreselect_AndRequiresChoice()
    {
        var (instructor, _) = await InstructorSessionAsync();
        await instructor.CreateClassAsync("Ön seçim şubesi", "Test");
        var editor = new ExamEditorViewModel(instructor, null, _ => { });
        await editor.LoadCommand.ExecuteAsync(null);

        Assert.True(editor.Classes.Count > 1);
        Assert.Null(editor.SelectedClass);

        editor.ExamTitle = "Şubesiz sınav";
        await editor.SaveDraftCommand.ExecuteAsync(null);
        Assert.Equal("Hedef şubeyi seçin.", editor.ErrorMessage);

        editor.SelectedClass = editor.Classes.First(c => c.Name.Contains("A şubesi"));
        await editor.SaveDraftCommand.ExecuteAsync(null);
        Assert.Null(editor.ErrorMessage);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < EventTimeout, "Beklenen durum zamanında oluşmadı.");
            await Task.Delay(25);
        }
    }
}

/// <summary>Öğrenci uygulamasının ilk ekranı: bağlan → giriş → lobi akışı.</summary>
public sealed class StudentConnectFlowTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>, IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"proglab-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task ConnectScreen_FindsServer_ThenLoginThenLobby_AndCanChangeServer()
    {
        var store = new LocalClientStore(Path.Combine(_folder, "client.json"));
        var fingerprint = new string('A', 64);
        var discovery = new FakeDiscovery(new ProgLab.Shared.DiscoveryMessage("127.0.0.1", 5000, "1.0.0.0", fingerprint));
        var main = new Student.MainWindowViewModel(new TestSessionFactory(fixture), discovery, store);

        var connect = Assert.IsType<Student.ConnectViewModel>(main.CurrentPage);
        Assert.False(main.IsLoggedIn);
        var row = Assert.Single(connect.Servers);
        Assert.Equal("127.0.0.1:5000", row.Title);
        Assert.Contains("AAAAAAAA…AAAAAAAA", row.Details);

        // Seçim olmadan ve geçersiz adresle hata.
        await connect.ConnectSelectedCommand.ExecuteAsync(null);
        Assert.StartsWith("Listeden bir sunucu seçin", connect.ErrorMessage);
        connect.ManualAddress = "http://yanlis/adres";
        await connect.ConnectManualCommand.ExecuteAsync(null);
        Assert.StartsWith("Geçerli bir adres girin", connect.ErrorMessage);

        // Aynı sunucunun tekrar duyurusu listeyi şişirmez, seçim korunur.
        connect.SelectedServer = row;
        discovery.Announce();
        Assert.Single(connect.Servers);
        Assert.NotNull(connect.SelectedServer);

        await connect.ConnectSelectedCommand.ExecuteAsync(null);
        Assert.Equal("127.0.0.1:5000", store.LastServer);
        Assert.True(discovery.WasCancelled);

        var login = Assert.IsType<Student.LoginViewModel>(main.CurrentPage);
        Assert.True(login.CanChangeServer);
        Assert.Equal("127.0.0.1:5000", login.ServerText);

        // Sunucu değiştirme geri götürür; son adres hazır gelir.
        login.ChangeServerCommand.Execute(null);
        var again = Assert.IsType<Student.ConnectViewModel>(main.CurrentPage);
        Assert.Equal("127.0.0.1:5000", again.ManualAddress);
        await again.ConnectManualCommand.ExecuteAsync(null);

        var loginAgain = Assert.IsType<Student.LoginViewModel>(main.CurrentPage);
        loginAgain.GoToFirstLoginCommand.Execute(null);
        var first = Assert.IsType<Student.FirstLoginViewModel>(main.CurrentPage);
        first.StudentNumber = TestAccounts.StudentNumber(1);
        first.Email = TestAccounts.StudentEmail(1);
        first.NewPassword = first.NewPasswordRepeat = TestAccounts.StudentPassword;
        await first.SubmitCommand.ExecuteAsync(null);

        var lobby = Assert.IsType<Student.LobbyViewModel>(main.CurrentPage);
        Assert.True(main.IsLoggedIn);
        main.LogoutCommand.Execute(null);
        Assert.IsType<Student.LoginViewModel>(main.CurrentPage);
        Assert.False(main.IsLoggedIn);
        Assert.NotNull(lobby);
    }

    [Fact]
    public void StaleServers_AreRemovedFromList()
    {
        var store = new LocalClientStore(Path.Combine(_folder, "stale.json"));
        var discovery = new FakeDiscovery(new ProgLab.Shared.DiscoveryMessage("10.0.0.1", 5000, "1", new string('A', 64)));
        var connect = new Student.ConnectViewModel(new TestSessionFactory(fixture), discovery, store, _ => { }, a => a());
        connect.StartDiscovery();
        Assert.Single(connect.Servers);

        connect.RemoveStaleServers(DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Single(connect.Servers);

        connect.RemoveStaleServers(DateTimeOffset.UtcNow.AddSeconds(11));
        Assert.Empty(connect.Servers);
    }

    private sealed class TestSessionFactory(MigratedDatabaseFixture fixture) : IServerSessionFactory
    {
        public ServerSession Create(Uri baseAddress, string? expectedFingerprint = null)
        {
            var tokens = new TokenStore();
            var hub = TestHubConnection.Create(fixture.Server, tokens);
            return new ServerSession(baseAddress, new ApiClient(fixture.Server.CreateClient(), tokens), hub);
        }
    }

    private sealed class FakeDiscovery(ProgLab.Shared.DiscoveryMessage message) : IServerDiscovery
    {
        private Action<ProgLab.Shared.DiscoveryMessage>? _onFound;
        private CancellationToken _token;

        public bool WasCancelled => _token.IsCancellationRequested;

        public Task ListenAsync(Action<ProgLab.Shared.DiscoveryMessage> onFound, CancellationToken ct)
        {
            _onFound = onFound;
            _token = ct;
            Announce();
            return Task.CompletedTask;
        }

        public void Announce() => _onFound?.Invoke(message);
    }
}
