using Microsoft.EntityFrameworkCore;
using ProgLab.Client.Core;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>VT-01: seed eğitmenin geçici parolayla ilk girişi. Sıra önemli olduğu için tek test ve kendi şeması.</summary>
public sealed class InstructorFirstLoginTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private const string Temporary = ProgLabServerFactory.InstructorTemporaryPassword;
    private const string NewPassword = "Yeni-Parola-123";

    [Fact]
    public async Task SeedInstructor_MustChangeTemporaryPassword_BeforeUsingTheSystem()
    {
        var api = new ApiClient(fixture.Server.CreateClient());

        // Token alınır ve parola değiştirme istenir.
        var login = await api.LoginAsync("egitmen", Temporary, UserRole.Instructor);
        Assert.False(string.IsNullOrEmpty(login.Token));
        Assert.True(login.MustChangePassword);
        Assert.Equal(UserRole.Instructor, login.Role);
        Assert.Equal("Demo Eğitmen", login.FullName);

        // Parola değişmeden diğer uç noktalar çalışmaz.
        Assert.Equal(ErrorCodes.PasswordChangeRequired, (await Assert.ThrowsAsync<ApiException>(() => api.GetMeAsync())).Code);
        Assert.Equal(ErrorCodes.PasswordChangeRequired, (await Assert.ThrowsAsync<ApiException>(() => api.GetClassesAsync())).Code);

        // Yanlış mevcut parola ve kısa parola reddedilir.
        Assert.Equal(ErrorCodes.InvalidCredentials, (await Assert.ThrowsAsync<ApiException>(() => api.ChangePasswordAsync("yanlis-parola", NewPassword))).Code);
        Assert.Equal(ErrorCodes.PasswordTooShort, (await Assert.ThrowsAsync<ApiException>(() => api.ChangePasswordAsync(Temporary, "kisa"))).Code);

        // Değiştirince aynı oturum çalışır.
        await api.ChangePasswordAsync(Temporary, NewPassword);
        var me = await api.GetMeAsync();
        Assert.False(me.MustChangePassword);

        await using (var db = fixture.CreateDbContext())
        {
            var stored = await db.Users.SingleAsync(u => u.Username == "egitmen");
            Assert.False(stored.MustChangePassword);
            Assert.NotNull(stored.VerifiedAt);
            Assert.True(BCrypt.Net.BCrypt.Verify(NewPassword, stored.PasswordHash));
        }

        // Eski parola artık geçmez, yenisi zorunluluk olmadan girer.
        var fresh = new ApiClient(fixture.Server.CreateClient());
        Assert.Equal(ErrorCodes.InvalidCredentials, (await Assert.ThrowsAsync<ApiException>(() => fresh.LoginAsync("egitmen", Temporary, UserRole.Instructor))).Code);
        Assert.False((await fresh.LoginAsync("egitmen", NewPassword, UserRole.Instructor)).MustChangePassword);
    }
}
