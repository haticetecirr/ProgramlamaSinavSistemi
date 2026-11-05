using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Enums;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Ana sayfa: sunucu adresi, parmak izi, bağlı öğrenciler, yayındaki sınavlar.</summary>
public sealed partial class DashboardViewModel(ApiClient api, LiveStatusViewModel live) : ObservableObject
{
    public LiveStatusViewModel Live { get; } = live;

    public ObservableCollection<ExamRow> ActiveExams { get; } = [];

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task LoadAsync()
    {
        ErrorMessage = null;
        try
        {
            await api.GetHealthAsync();
            await Live.StartAsync();

            var exams = await api.GetExamsAsync(ExamStatus.Published);
            ActiveExams.Clear();
            foreach (var exam in exams.Items)
            {
                ActiveExams.Add(new ExamRow(exam));
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
