using ProgLab.Client.Core;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests.Infrastructure;

/// <summary>Testlerde hazır oturum açmak için yardımcılar. Her test gerçek API üzerinden giriş yapar.</summary>
public static class TestAccounts
{
    public const string InstructorPassword = "Yeni-Egitmen-Parola-1";
    public const string StudentPassword = "Ogrenci-Parola-1";

    public static string StudentNumber(int index) => $"20269{index:D5}";

    public static string StudentEmail(int index) => $"{StudentNumber(index)}@ogr.demo.local";

    /// <summary>Eğitmen olarak girer; geçici parola hâlâ geçerliyse önce değiştirir.</summary>
    public static async Task<ApiClient> InstructorAsync(ProgLabServerFactory server)
    {
        var api = new ApiClient(server.CreateClient());
        try
        {
            await api.LoginAsync("egitmen", InstructorPassword, UserRole.Instructor);
        }
        catch (ApiException)
        {
            await api.LoginAsync("egitmen", ProgLabServerFactory.InstructorTemporaryPassword, UserRole.Instructor);
            await api.ChangePasswordAsync(ProgLabServerFactory.InstructorTemporaryPassword, InstructorPassword);
        }

        return api;
    }

    /// <summary>Demo öğrencinin ilk girişini tamamlar ve oturum açık istemciyi döner.</summary>
    public static async Task<ApiClient> StudentAsync(ProgLabServerFactory server, int index)
    {
        var api = new ApiClient(server.CreateClient());
        await api.FirstLoginAsync(StudentNumber(index), StudentEmail(index), StudentPassword);
        return api;
    }
}
