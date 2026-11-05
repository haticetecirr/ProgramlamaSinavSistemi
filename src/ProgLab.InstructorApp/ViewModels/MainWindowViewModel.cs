using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Ana pencere: hangi ekranın gösterileceğine CurrentPage karar verir.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly ExamHubClient? _hub;
    private readonly Action<Action> _ui;

    /// <summary>Sonuç, log veya canlı ekrandan dönünce aynı sekme açılır.</summary>
    private ExamStatus _examTab = ExamStatus.Draft;

    [ObservableProperty]
    private string _title = "Prog. Lab. Sınav Platformu — Eğitmen";

    [ObservableProperty]
    private ObservableObject _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn))]
    private string? _userName;

    /// <summary>Parola değiştirme zorunluluğu varken menü ve durum çubuğu gizlenir.</summary>
    [ObservableProperty]
    private bool _canNavigate;

    public MainWindowViewModel(ApiClient api, ExamHubClient? hub = null, Func<string?>? readFingerprint = null, Action<Action>? ui = null)
    {
        _api = api;
        _hub = hub;
        _ui = ui ?? (action => action());
        Live = new LiveStatusViewModel(api, hub, readFingerprint, _ui);
        _api.SessionExpired += () => _ui(() => ShowLogin("Oturum süreniz doldu. Lütfen yeniden giriş yapın."));
        _currentPage = new LoginViewModel(_api, OnLoggedIn);
    }

    public LiveStatusViewModel Live { get; }

    public bool IsLoggedIn => UserName is not null;

    [RelayCommand]
    private void Logout()
    {
        _api.Logout();
        ShowLogin();
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        LeaveCurrentPage();
        CanNavigate = true;
        var page = new DashboardViewModel(_api, Live);
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ShowClasses()
    {
        LeaveCurrentPage();
        var page = new ClassesViewModel(_api);
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ShowQuestionBank() => OpenQuestionBank(null);

    private void OpenQuestionBank(string? message)
    {
        LeaveCurrentPage();
        var page = new QuestionBankViewModel(_api, OpenQuestionEditor, message);
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    private void OpenQuestionEditor(int? questionId)
    {
        var page = new QuestionEditorViewModel(_api, questionId, OpenQuestionBank);
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ShowExams() => OpenExamList(null);

    private void OpenExamList(string? message) => OpenExamList(message, ExamStatus.Draft);

    private void BackToExamList() => OpenExamList(null, _examTab);

    private void OpenExamList(string? message, ExamStatus tab)
    {
        LeaveCurrentPage();
        var page = new ExamListViewModel(_api, OpenExamEditor, message, OpenLiveExam, OpenResults, OpenLogs, tab);
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    private void OpenExamEditor(int? examId)
    {
        var page = new ExamEditorViewModel(_api, examId, OpenExamList);
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    private void OpenLiveExam(int examId)
    {
        RememberExamTab();
        LeaveCurrentPage();
        var page = new LiveExamViewModel(_api, _hub, examId, _ui, BackToExamList, OpenResults);
        CurrentPage = page;
        page.Start();
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    private void OpenResults(int examId)
    {
        RememberExamTab();
        LeaveCurrentPage();
        var page = new ResultsViewModel(_api, examId, id => OpenSubmission(id, examId), BackToExamList);
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    private void OpenSubmission(long submissionId, int examId)
    {
        var page = new SubmissionDetailViewModel(_api, submissionId, () => OpenResults(examId));
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    private void OpenLogs(int examId)
    {
        RememberExamTab();
        var page = new LogsViewModel(_api, examId, BackToExamList);
        CurrentPage = page;
        _ = page.ApplyCommand.ExecuteAsync(null);
    }

    private void RememberExamTab()
    {
        if (CurrentPage is ExamListViewModel list)
        {
            _examTab = list.Status;
        }
    }

    private void LeaveCurrentPage()
    {
        if (CurrentPage is LiveExamViewModel live)
        {
            live.Stop();
        }
    }

    private void ShowLogin(string? message = null)
    {
        LeaveCurrentPage();
        Live.Stop();
        UserName = null;
        CanNavigate = false;
        CurrentPage = new LoginViewModel(_api, OnLoggedIn, message);
    }

    private void OnLoggedIn(LoginResponse response)
    {
        UserName = response.FullName;
        if (response.MustChangePassword)
        {
            CurrentPage = new ChangePasswordViewModel(_api, ShowDashboard);
        }
        else
        {
            ShowDashboard();
        }
    }
}
