using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Listede gösterilen öğrenci satırı.</summary>
public sealed record StudentRow(UserDto User)
{
    public string Title => $"{User.Username} — {User.FullName}";

    public string Status => $"{User.Email} · {(User.IsActive ? "Aktif" : "Pasif")} · {(User.VerifiedAt is null ? "İlk giriş bekleniyor" : "İlk giriş yapıldı")}";
}

/// <summary>Şube ve öğrenci yönetimi ekranı.</summary>
public sealed partial class ClassesViewModel(ApiClient api) : ObservableObject
{
    public ObservableCollection<ClassDto> Classes { get; } = [];

    public ObservableCollection<StudentRow> Members { get; } = [];

    public ObservableCollection<StudentRow> OtherStudents { get; } = [];

    [ObservableProperty]
    private ClassDto? _selectedClass;

    [ObservableProperty]
    private StudentRow? _selectedMember;

    [ObservableProperty]
    private StudentRow? _selectedOtherStudent;

    [ObservableProperty]
    private string _newClassName = "";

    [ObservableProperty]
    private string _newClassTerm = "2026-2027 Güz";

    [ObservableProperty]
    private string _newStudentNumber = "";

    [ObservableProperty]
    private string _newStudentName = "";

    [ObservableProperty]
    private string _newStudentEmail = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    // Liste yenilenirken seçim kod tarafından değişir; o sırada üyeler ayrıca yüklenmesin.
    private bool _isReloadingClasses;

    // Hızlı şube değiştirmede yalnızca en son başlayan yükleme ekrana yazılır.
    private int _membersLoadVersion;

    partial void OnSelectedClassChanged(ClassDto? value)
    {
        if (!_isReloadingClasses)
        {
            _ = LoadMembersAsync();
        }
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(ReloadClassesAsync);

    [RelayCommand]
    private Task AddClassAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(NewClassName) || string.IsNullOrWhiteSpace(NewClassTerm))
        {
            ErrorMessage = "Şube adını ve dönemi girin.";
            return;
        }

        var created = await api.CreateClassAsync(NewClassName, NewClassTerm);
        NewClassName = "";
        StatusMessage = $"\"{created.Name}\" şubesi eklendi.";
        await ReloadClassesAsync(created.Id);
    });

    [RelayCommand]
    private Task DeleteClassAsync() => RunAsync(async () =>
    {
        if (SelectedClass is not { } target)
        {
            ErrorMessage = "Önce bir şube seçin.";
            return;
        }

        await api.DeleteClassAsync(target.Id);
        StatusMessage = $"\"{target.Name}\" şubesi silindi.";
        await ReloadClassesAsync();
    });

    [RelayCommand]
    private Task AddNewStudentAsync() => RunAsync(async () =>
    {
        if (SelectedClass is not { } target)
        {
            ErrorMessage = "Önce bir şube seçin.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewStudentNumber) || string.IsNullOrWhiteSpace(NewStudentName) || string.IsNullOrWhiteSpace(NewStudentEmail))
        {
            ErrorMessage = "Öğrenci no, ad soyad ve e-postayı girin.";
            return;
        }

        var student = await api.CreateStudentAsync(NewStudentNumber, NewStudentName, NewStudentEmail);
        await SaveMembersAsync(target, Members.Select(m => m.User.Id).Append(student.Id).ToList());
        NewStudentNumber = NewStudentName = NewStudentEmail = "";
        StatusMessage = $"{student.FullName} eklendi. İlk girişte öğrenci no ve e-postasıyla parolasını belirleyecek.";
    });

    [RelayCommand]
    private Task AddExistingStudentAsync() => RunAsync(async () =>
    {
        if (SelectedClass is not { } target || SelectedOtherStudent is not { } student)
        {
            ErrorMessage = "Önce şube ve öğrenci seçin.";
            return;
        }

        await SaveMembersAsync(target, Members.Select(m => m.User.Id).Append(student.User.Id).ToList());
        StatusMessage = $"{student.User.FullName} şubeye eklendi.";
    });

    [RelayCommand]
    private Task RemoveMemberAsync() => RunAsync(async () =>
    {
        if (SelectedClass is not { } target || SelectedMember is not { } member)
        {
            ErrorMessage = "Önce listeden bir öğrenci seçin.";
            return;
        }

        await SaveMembersAsync(target, Members.Where(m => m.User.Id != member.User.Id).Select(m => m.User.Id).ToList());
        StatusMessage = $"{member.User.FullName} şubeden çıkarıldı.";
    });

    [RelayCommand]
    private Task ResetFirstLoginAsync() => RunAsync(async () =>
    {
        if (SelectedMember is not { } member)
        {
            ErrorMessage = "Önce listeden bir öğrenci seçin.";
            return;
        }

        await api.ResetFirstLoginAsync(member.User.Id);
        await LoadMembersCoreAsync();
        StatusMessage = $"{member.User.FullName} için ilk giriş sıfırlandı.";
    });

    [RelayCommand]
    private Task ToggleActiveAsync() => RunAsync(async () =>
    {
        if (SelectedMember is not { User: var user })
        {
            ErrorMessage = "Önce listeden bir öğrenci seçin.";
            return;
        }

        await api.UpdateStudentAsync(user.Id, user.FullName, user.Email ?? "", !user.IsActive);
        await LoadMembersCoreAsync();
        StatusMessage = $"{user.FullName} {(user.IsActive ? "pasifleştirildi" : "etkinleştirildi")}.";
    });

    private Task LoadMembersAsync() => RunAsync(LoadMembersCoreAsync, clearMessages: false);

    private async Task SaveMembersAsync(ClassDto target, IReadOnlyList<int> studentIds)
    {
        await api.ReplaceClassMembersAsync(target.Id, studentIds);
        await ReloadClassesAsync(target.Id);
    }

    private async Task ReloadClassesAsync() => await ReloadClassesAsync(SelectedClass?.Id);

    private async Task ReloadClassesAsync(int? selectId)
    {
        var classes = await api.GetClassesAsync();

        _isReloadingClasses = true;
        Classes.Clear();
        foreach (var item in classes)
        {
            Classes.Add(item);
        }

        // Liste yenilenince seçim boşalır; aynı şube varsa yeniden seçilir.
        SelectedClass = classes.FirstOrDefault(c => c.Id == selectId) ?? classes.FirstOrDefault();
        _isReloadingClasses = false;

        await LoadMembersCoreAsync();
    }

    private async Task LoadMembersCoreAsync()
    {
        var version = ++_membersLoadVersion;
        var target = SelectedClass;
        if (target is null)
        {
            Members.Clear();
            OtherStudents.Clear();
            return;
        }

        var detail = await api.GetClassAsync(target.Id);
        var all = await api.GetStudentsAsync();
        if (version != _membersLoadVersion)
        {
            return; // Bu sırada daha yeni bir yükleme başladı; ekrana o yazar.
        }

        Members.Clear();
        OtherStudents.Clear();
        foreach (var member in detail.Members)
        {
            Members.Add(new StudentRow(member));
        }

        var memberIds = detail.Members.Select(m => m.Id).ToHashSet();
        foreach (var student in all.Items.Where(s => !memberIds.Contains(s.Id)))
        {
            OtherStudents.Add(new StudentRow(student));
        }
    }

    private async Task RunAsync(Func<Task> action, bool clearMessages = true)
    {
        if (clearMessages)
        {
            ErrorMessage = null;
            StatusMessage = null;
        }

        try
        {
            await action();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
