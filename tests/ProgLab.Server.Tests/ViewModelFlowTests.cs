using ProgLab.Client.Core;
using ProgLab.Server.Tests.Infrastructure;
using Instructor = ProgLab.InstructorApp.ViewModels;
using Student = ProgLab.StudentApp.ViewModels;

namespace ProgLab.Server.Tests;

/// <summary>Ekranların arkasındaki ViewModel akışları, gerçek API'ye karşı (pencere açmadan).</summary>
public sealed class InstructorViewModelFlowTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    [Fact]
    public async Task Login_ChangePassword_ManageClasses_Logout()
    {
        var main = new Instructor.MainWindowViewModel(new ApiClient(fixture.Server.CreateClient()));
        var login = Assert.IsType<Instructor.LoginViewModel>(main.CurrentPage);
        Assert.False(main.IsLoggedIn);

        // Boş alan ve yanlış parola Türkçe mesaj verir, ekran değişmez.
        await login.LoginCommand.ExecuteAsync(null);
        Assert.Equal("Kullanıcı adı ve parolayı girin.", login.ErrorMessage);

        login.Username = "egitmen";
        login.Password = "yanlis-parola";
        await login.LoginCommand.ExecuteAsync(null);
        Assert.Equal("Bilgiler yanlış. Lütfen kontrol edip tekrar deneyin.", login.ErrorMessage);
        Assert.Same(login, main.CurrentPage);

        // Geçici parola: parola değiştirme ekranı açılır.
        login.Password = ProgLabServerFactory.InstructorTemporaryPassword;
        await login.LoginCommand.ExecuteAsync(null);
        var change = Assert.IsType<Instructor.ChangePasswordViewModel>(main.CurrentPage);
        Assert.Equal("Demo Eğitmen", main.UserName);
        Assert.True(main.IsLoggedIn);

        change.CurrentPassword = ProgLabServerFactory.InstructorTemporaryPassword;
        change.NewPassword = "Yeni-Parola-123";
        change.NewPasswordRepeat = "Farkli-Parola-123";
        await change.ChangeCommand.ExecuteAsync(null);
        Assert.Equal("Yeni parolalar birbiriyle aynı değil.", change.ErrorMessage);

        change.NewPasswordRepeat = "Yeni-Parola-123";
        await change.ChangeCommand.ExecuteAsync(null);
        await Assert.IsType<Instructor.DashboardViewModel>(main.CurrentPage).LoadCommand.ExecutionTask!;
        Assert.True(main.CanNavigate);

        main.ShowClassesCommand.Execute(null);
        var classes = Assert.IsType<Instructor.ClassesViewModel>(main.CurrentPage);

        // Şube listesi ve üyeler ekran açılırken kendiliğinden yüklenir.
        await classes.LoadCommand.ExecutionTask!;
        Assert.Null(classes.ErrorMessage);
        Assert.Contains(classes.Classes, c => c.Name.Contains("A şubesi"));
        Assert.NotNull(classes.SelectedClass);
        Assert.Equal(30, classes.Members.Count);

        // Yeni şube ve yeni öğrenci.
        classes.NewClassName = "Test şubesi";
        await classes.AddClassCommand.ExecuteAsync(null);
        Assert.Null(classes.ErrorMessage);
        Assert.Equal("Test şubesi", classes.SelectedClass!.Name);
        Assert.Empty(classes.Members);
        Assert.Equal(30, classes.OtherStudents.Count);

        classes.NewStudentNumber = "2026000010";
        classes.NewStudentName = "Arayüz Öğrencisi";
        classes.NewStudentEmail = "arayuz@ogr.demo.local";
        await classes.AddNewStudentCommand.ExecuteAsync(null);
        Assert.Null(classes.ErrorMessage);
        Assert.Equal("Arayüz Öğrencisi", Assert.Single(classes.Members).User.FullName);
        Assert.Equal(1, classes.SelectedClass!.MemberCount);

        // Kayıtlı öğrenci ekleme, seçili üyeyi pasifleştirme, sıfırlama, çıkarma.
        classes.SelectedOtherStudent = classes.OtherStudents[0];
        await classes.AddExistingStudentCommand.ExecuteAsync(null);
        Assert.Equal(2, classes.Members.Count);

        classes.SelectedMember = classes.Members.Single(m => m.User.Username == "2026000010");
        await classes.ToggleActiveCommand.ExecuteAsync(null);
        Assert.False(classes.Members.Single(m => m.User.Username == "2026000010").User.IsActive);

        classes.SelectedMember = classes.Members.Single(m => m.User.Username == "2026000010");
        await classes.ResetFirstLoginCommand.ExecuteAsync(null);
        Assert.Null(classes.ErrorMessage);

        classes.SelectedMember = classes.Members.Single(m => m.User.Username == "2026000010");
        await classes.RemoveMemberCommand.ExecuteAsync(null);
        Assert.Single(classes.Members);

        // Aynı numara ikinci kez: sunucu hatası Türkçe gösterilir.
        classes.NewStudentNumber = "2026000010";
        classes.NewStudentName = "Kopya";
        classes.NewStudentEmail = "kopya10@ogr.demo.local";
        await classes.AddNewStudentCommand.ExecuteAsync(null);
        Assert.Equal("Bu öğrenci numarası zaten kayıtlı.", classes.ErrorMessage);

        // Çıkış: giriş ekranı döner ve eski oturum kullanılamaz.
        main.LogoutCommand.Execute(null);
        Assert.IsType<Instructor.LoginViewModel>(main.CurrentPage);
        Assert.False(main.IsLoggedIn);
    }
}

public sealed class StudentViewModelFlowTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    [Fact]
    public async Task FirstLogin_Logout_Login()
    {
        const string number = "2026900001";
        var main = new Student.MainWindowViewModel(new ApiClient(fixture.Server.CreateClient()));
        var login = Assert.IsType<Student.LoginViewModel>(main.CurrentPage);

        // Parolası olmayan öğrenci yönlendirilir.
        login.StudentNumber = number;
        login.Password = "herhangi-bir-parola";
        await login.LoginCommand.ExecuteAsync(null);
        Assert.Contains("İlk kez giriyorum", login.ErrorMessage);

        login.GoToFirstLoginCommand.Execute(null);
        var first = Assert.IsType<Student.FirstLoginViewModel>(main.CurrentPage);

        first.StudentNumber = number;
        first.Email = "yanlis@ogr.demo.local";
        first.NewPassword = first.NewPasswordRepeat = "Ogrenci-Parola-1";
        await first.SubmitCommand.ExecuteAsync(null);
        Assert.Equal("Bilgiler yanlış. Lütfen kontrol edip tekrar deneyin.", first.ErrorMessage);

        first.Email = "2026900001@ogr.demo.local";
        first.NewPasswordRepeat = "Farkli";
        await first.SubmitCommand.ExecuteAsync(null);
        Assert.Equal("Parolalar birbiriyle aynı değil.", first.ErrorMessage);

        first.NewPasswordRepeat = first.NewPassword;
        await first.SubmitCommand.ExecuteAsync(null);
        var lobby = Assert.IsType<Student.LobbyViewModel>(main.CurrentPage);
        Assert.Equal("Hoş geldiniz, Demo Öğrenci 1.", lobby.Greeting);
        Assert.Equal("Demo Öğrenci 1", main.UserName);

        main.LogoutCommand.Execute(null);
        var again = Assert.IsType<Student.LoginViewModel>(main.CurrentPage);

        again.StudentNumber = number;
        again.Password = "Ogrenci-Parola-1";
        await again.LoginCommand.ExecuteAsync(null);
        Assert.IsType<Student.LobbyViewModel>(main.CurrentPage);
        main.LogoutCommand.Execute(null); // Lobinin aralıklı yenilemesini durdurur.
    }

    [Fact]
    public async Task UnreachableServer_ShowsConnectionMessage()
    {
        var http = new HttpClient { BaseAddress = new Uri("http://localhost:1/"), Timeout = TimeSpan.FromSeconds(5) };
        var main = new Student.MainWindowViewModel(new ApiClient(http));
        var login = Assert.IsType<Student.LoginViewModel>(main.CurrentPage);
        login.StudentNumber = "2026900002";
        login.Password = "herhangi-bir-parola";

        await login.LoginCommand.ExecuteAsync(null);

        Assert.Contains("Sunucuya ulaşılamadı", login.ErrorMessage);
    }
}
