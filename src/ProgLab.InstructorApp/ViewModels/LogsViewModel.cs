using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Log tablosu satırı; saat yerel gösterilir.</summary>
public sealed record LogRow(LogEntryDto Entry)
{
    public string TimeText => Entry.OccurredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    public string EventType => Entry.EventType;

    public string Username => Entry.Username;

    public string FullName => Entry.FullName;

    public string? Ip => Entry.Ip;

    public string Text => Entry.Text;
}

/// <summary>Sınav logları: filtreler, sayfalama ve ham dosya.</summary>
public sealed partial class LogsViewModel(ApiClient api, int examId, Action onBack) : ObservableObject
{
    public const string AllEvents = "Tüm olaylar";

    private static readonly CultureInfo Turkish = new("tr-TR");

    private int _totalPages = 1;

    public IReadOnlyList<string> EventTypes { get; } =
    [
        AllEvents,
        "EXAM_PUBLISHED",
        "JOINED",
        "SECOND_DEVICE",
        "SUBMITTED",
        "AUTO_SUBMITTED",
        "SAVE_REJECTED",
        "SUBMIT_REJECTED",
        "SCORE_OVERRIDDEN",
        "EXAM_ENDED"
    ];

    public ObservableCollection<LogRow> Rows { get; } = [];

    [ObservableProperty]
    private string _selectedEvent = AllEvents;

    [ObservableProperty]
    private string? _studentFilter;

    [ObservableProperty]
    private string? _fromText;

    [ObservableProperty]
    private string? _toText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack), nameof(CanGoNext))]
    private int _page = 1;

    [ObservableProperty]
    private string _pageText = "";

    [ObservableProperty]
    private string? _rawText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTableVisible))]
    private bool _isRawVisible;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsTableVisible => !IsRawVisible;

    public bool CanGoBack => Page > 1;

    public bool CanGoNext => Page < _totalPages;

    /// <summary>Filtreyi uygular, ilk sayfaya döner.</summary>
    [RelayCommand]
    private Task ApplyAsync()
    {
        Page = 1;
        return LoadPageAsync();
    }

    [RelayCommand]
    private Task PreviousAsync()
    {
        if (!CanGoBack)
        {
            return Task.CompletedTask;
        }

        Page--;
        return LoadPageAsync();
    }

    [RelayCommand]
    private Task NextAsync()
    {
        if (!CanGoNext)
        {
            return Task.CompletedTask;
        }

        Page++;
        return LoadPageAsync();
    }

    [RelayCommand]
    private async Task ShowRawAsync()
    {
        try
        {
            RawText = await api.GetExamLogFileAsync(examId);
            IsRawVisible = true;
            ErrorMessage = null;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void ShowTable() => IsRawVisible = false;

    [RelayCommand]
    private void Back() => onBack();

    /// <summary>"gg.aa.yyyy" veya "gg.aa.yyyy ss:dd"; yalnız tarih yazılırsa günün başı/sonu sayılır.</summary>
    public static bool TryParseTime(string? text, bool endOfRange, out DateTimeOffset? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var input = text.Trim();
        if (DateTime.TryParseExact(input, "dd.MM.yyyy HH:mm", Turkish, DateTimeStyles.None, out var minute))
        {
            // Bitiş olarak yazılan dakika kendi sonuna kadar kapsanır.
            value = ToLocalOffset(endOfRange ? minute.AddMinutes(1).AddTicks(-1) : minute);
            return true;
        }

        if (DateTime.TryParseExact(input, "dd.MM.yyyy", Turkish, DateTimeStyles.None, out var day))
        {
            value = ToLocalOffset(endOfRange ? day.AddDays(1).AddTicks(-1) : day);
            return true;
        }

        return false;
    }

    private static DateTimeOffset ToLocalOffset(DateTime local) => new(local, TimeZoneInfo.Local.GetUtcOffset(local));

    private async Task LoadPageAsync()
    {
        if (!TryParseTime(FromText, endOfRange: false, out var from) || !TryParseTime(ToText, endOfRange: true, out var to))
        {
            ErrorMessage = "Tarih gg.aa.yyyy veya gg.aa.yyyy ss:dd biçiminde olmalıdır.";
            return;
        }

        try
        {
            var eventType = SelectedEvent == AllEvents ? null : SelectedEvent;
            var result = await api.GetExamLogsAsync(examId, eventType, StudentFilter, from, to, Page);

            Rows.Clear();
            foreach (var entry in result.Items)
            {
                Rows.Add(new LogRow(entry));
            }

            _totalPages = Math.Max(1, (int)Math.Ceiling(result.Total / (double)result.PageSize));
            PageText = $"Sayfa {result.Page} / {_totalPages} · {result.Total} kayıt";
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoBack));
            IsRawVisible = false;
            ErrorMessage = null;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
