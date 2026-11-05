using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Durum çubuğu ve ana sayfanın ortak verisi: sunucu adresi, parmak izi, bağlı öğrenciler.</summary>
public sealed partial class LiveStatusViewModel(ApiClient api, ExamHubClient? hub, Func<string?>? readFingerprint, Action<Action> ui) : ObservableObject
{
    private const int ServerPort = ServerAddress.DefaultPort;

    private bool _isListening;

    public ObservableCollection<StudentConnectionDto> Students { get; } = [];

    [ObservableProperty]
    private string _connectionText = "Bağlanıyor…";

    [ObservableProperty]
    private string _fingerprint = "—";

    [ObservableProperty]
    private int _connectedCount;

    /// <summary>Öğrencilerin elle yazacağı adresler.</summary>
    public string AddressText
    {
        get
        {
            var addresses = NetworkAddresses.GetLocalIPv4();
            return addresses.Count == 0
                ? "Ağ bağlantısı bulunamadı"
                : string.Join(" · ", addresses.Select(a => $"{a}:{ServerPort}"));
        }
    }

    public string StatusBarText => $"Sunucu: {AddressText}  |  Bağlı öğrenci: {ConnectedCount}  |  {ConnectionText}";

    partial void OnConnectedCountChanged(int value) => OnPropertyChanged(nameof(StatusBarText));

    partial void OnConnectionTextChanged(string value) => OnPropertyChanged(nameof(StatusBarText));

    /// <summary>Oturum açılınca: bildirimleri dinle, o an bağlı olanları sunucudan al.</summary>
    public async Task StartAsync()
    {
        if (hub is not null && !_isListening)
        {
            _isListening = true;
            hub.StudentConnected += OnStudentConnected;
            hub.StudentDisconnected += OnStudentDisconnected;
            hub.StateChanged += OnHubStateChanged;
            hub.StartInBackground();
        }

        await RefreshAsync();
    }

    public void Stop()
    {
        if (hub is not null && _isListening)
        {
            _isListening = false;
            hub.StudentConnected -= OnStudentConnected;
            hub.StudentDisconnected -= OnStudentDisconnected;
            hub.StateChanged -= OnHubStateChanged;
            _ = hub.StopAsync();
        }

        Students.Clear();
        ConnectedCount = 0;
    }

    public async Task RefreshAsync()
    {
        try
        {
            var students = await api.GetConnectedStudentsAsync();
            Students.Clear();
            foreach (var student in students)
            {
                Students.Add(student);
            }

            ConnectedCount = Students.Count;
            Fingerprint = readFingerprint?.Invoke() ?? Fingerprint;
        }
        catch (ApiException)
        {
            // Durum çubuğu tek başına hata göstermez; ekranlar kendi hatasını gösterir.
        }
    }

    private void OnStudentConnected(StudentConnectionDto student) => ui(() =>
    {
        if (Students.All(s => s.StudentId != student.StudentId))
        {
            Students.Add(student);
        }

        ConnectedCount = Students.Count;
    });

    private void OnStudentDisconnected(StudentConnectionDto student) => ui(() =>
    {
        var existing = Students.FirstOrDefault(s => s.StudentId == student.StudentId);
        if (existing is not null)
        {
            Students.Remove(existing);
        }

        ConnectedCount = Students.Count;
    });

    private void OnHubStateChanged() => ui(() =>
    {
        var connected = hub!.IsConnected;
        ConnectionText = connected ? "Canlı bağlantı açık" : "Canlı bağlantı yok";
        if (connected)
        {
            // Kopukken değişenleri yakala.
            _ = RefreshAsync();
        }
    });
}
