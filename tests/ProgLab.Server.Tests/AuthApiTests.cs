using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using ProgLab.Client.Core;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Security;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>VT-02 – VT-06 ve giriş kuralları. Her test ayrı bir demo öğrenci kullanır.</summary>
public sealed class AuthApiTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    [Fact]
    public async Task VT02_WrongPassword_Returns401_AndLogsFailure()
    {
        var client = fixture.Server.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "egitmen", password = "Gizli-Yanlis-Parola", app = "Instructor" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.InvalidCredentials, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(401, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("title").GetString()));

        await using var db = fixture.CreateDbContext();
        var logs = await db.ActivityLogs.Where(l => l.EventType == "LOGIN_FAILED").ToListAsync();
        Assert.Contains(logs, l => l.Details!.Contains(ErrorCodes.InvalidCredentials) && l.Details.Contains("egitmen"));
        Assert.DoesNotContain(logs, l => l.Details!.Contains("Gizli-Yanlis-Parola"));
    }

    [Fact]
    public async Task UnknownUser_GetsSameAnswerAsWrongPassword()
    {
        var api = new ApiClient(fixture.Server.CreateClient());

        var error = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync("olmayan-kullanici", "herhangi-bir-parola", UserRole.Student));

        Assert.Equal(ErrorCodes.InvalidCredentials, error.Code);
        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task VT03_StudentCannotEnterInstructorApp_AndViceVersa()
    {
        await TestAccounts.StudentAsync(fixture.Server, 1);
        await TestAccounts.InstructorAsync(fixture.Server); // Parolasını sabit değere getirir.
        var api = new ApiClient(fixture.Server.CreateClient());

        var student = await Assert.ThrowsAsync<ApiException>(() =>
            api.LoginAsync(TestAccounts.StudentNumber(1), TestAccounts.StudentPassword, UserRole.Instructor));
        var instructor = await Assert.ThrowsAsync<ApiException>(() =>
            api.LoginAsync("egitmen", TestAccounts.InstructorPassword, UserRole.Student));

        Assert.Equal(ErrorCodes.WrongAppRole, student.Code);
        Assert.Equal(403, student.StatusCode);
        Assert.Equal(ErrorCodes.WrongAppRole, instructor.Code);
    }

    [Fact]
    public async Task StudentWithoutPassword_MustUseFirstLogin()
    {
        var api = new ApiClient(fixture.Server.CreateClient());

        var error = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync(TestAccounts.StudentNumber(2), "herhangi-bir-parola", UserRole.Student));

        Assert.Equal(ErrorCodes.FirstLoginRequired, error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task VT04_FirstLogin_SavesPassword_AndNewPasswordWorks()
    {
        const int student = 3;
        var api = new ApiClient(fixture.Server.CreateClient());

        // E-posta karşılaştırması büyük/küçük harf duyarsızdır.
        var first = await api.FirstLoginAsync(TestAccounts.StudentNumber(student), TestAccounts.StudentEmail(student).ToUpperInvariant(), TestAccounts.StudentPassword);

        Assert.False(string.IsNullOrEmpty(first.Token));
        Assert.Equal(UserRole.Student, first.Role);
        Assert.False(first.MustChangePassword);
        Assert.Equal(TestAccounts.StudentNumber(student), (await api.GetMeAsync()).Username);

        await using var db = fixture.CreateDbContext();
        var stored = await db.Users.SingleAsync(u => u.Username == TestAccounts.StudentNumber(student));
        Assert.NotNull(stored.VerifiedAt);
        Assert.NotEqual(TestAccounts.StudentPassword, stored.PasswordHash);
        Assert.True(await db.ActivityLogs.AnyAsync(l => l.EventType == "FIRST_LOGIN" && l.UserId == stored.Id));

        var fresh = new ApiClient(fixture.Server.CreateClient());
        var login = await fresh.LoginAsync(TestAccounts.StudentNumber(student), TestAccounts.StudentPassword, UserRole.Student);
        Assert.Equal(UserRole.Student, login.Role);

        // İkinci kez ilk giriş yapılamaz.
        var again = await Assert.ThrowsAsync<ApiException>(() =>
            fresh.FirstLoginAsync(TestAccounts.StudentNumber(student), TestAccounts.StudentEmail(student), "Baska-Parola-123"));
        Assert.Equal(ErrorCodes.AlreadyVerified, again.Code);
    }

    [Fact]
    public async Task FirstLogin_WithWrongEmail_IsRejected_AndNoPasswordIsSet()
    {
        const int student = 4;
        var api = new ApiClient(fixture.Server.CreateClient());

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            api.FirstLoginAsync(TestAccounts.StudentNumber(student), "yanlis@ogr.demo.local", TestAccounts.StudentPassword));

        Assert.Equal(ErrorCodes.InvalidCredentials, error.Code);
        await using var db = fixture.CreateDbContext();
        Assert.Null(await db.Users.Where(u => u.Username == TestAccounts.StudentNumber(student)).Select(u => u.PasswordHash).SingleAsync());
    }

    [Fact]
    public async Task FirstLogin_ForInstructorAccount_IsNotPossible()
    {
        var api = new ApiClient(fixture.Server.CreateClient());

        var error = await Assert.ThrowsAsync<ApiException>(() => api.FirstLoginAsync("egitmen", "x@y.z", TestAccounts.StudentPassword));

        Assert.Equal(ErrorCodes.InvalidCredentials, error.Code);
    }

    [Fact]
    public async Task FirstLogin_WithShortPassword_IsRejected()
    {
        var api = new ApiClient(fixture.Server.CreateClient());

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            api.FirstLoginAsync(TestAccounts.StudentNumber(5), TestAccounts.StudentEmail(5), "1234567"));

        Assert.Equal(ErrorCodes.PasswordTooShort, error.Code);
        Assert.Equal(422, error.StatusCode);
    }

    [Fact]
    public async Task DisabledAccount_CannotLogin_AndExistingTokenStopsWorking()
    {
        const int student = 6;
        var session = await TestAccounts.StudentAsync(fixture.Server, student);

        await using (var db = fixture.CreateDbContext())
        {
            await db.Users.Where(u => u.Username == TestAccounts.StudentNumber(student))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        }

        var login = await Assert.ThrowsAsync<ApiException>(() =>
            new ApiClient(fixture.Server.CreateClient()).LoginAsync(TestAccounts.StudentNumber(student), TestAccounts.StudentPassword, UserRole.Student));
        var existing = await Assert.ThrowsAsync<ApiException>(() => session.GetMeAsync());

        Assert.Equal(ErrorCodes.AccountDisabled, login.Code);
        Assert.Equal(ErrorCodes.AccountDisabled, existing.Code);
    }

    [Fact]
    public async Task SuccessfulLogin_IsLogged_WithAppAndUser()
    {
        const int student = 7;
        await TestAccounts.StudentAsync(fixture.Server, student);

        await new ApiClient(fixture.Server.CreateClient()).LoginAsync(TestAccounts.StudentNumber(student), TestAccounts.StudentPassword, UserRole.Student);

        await using var db = fixture.CreateDbContext();
        var id = await db.Users.Where(u => u.Username == TestAccounts.StudentNumber(student)).Select(u => u.Id).SingleAsync();
        var log = await db.ActivityLogs.SingleAsync(l => l.EventType == "LOGIN" && l.UserId == id);
        Assert.Contains("Student", log.Details);
    }

    [Fact]
    public async Task Token_HasDesignedClaims_AndFourHourLifetime()
    {
        const int student = 8;
        await TestAccounts.StudentAsync(fixture.Server, student);
        var api = new ApiClient(fixture.Server.CreateClient());

        var login = await api.LoginAsync(TestAccounts.StudentNumber(student), TestAccounts.StudentPassword, UserRole.Student);

        var jwt = new JsonWebToken(login.Token);
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal((await api.GetMeAsync()).Id.ToString(), jwt.Subject);
        Assert.Equal("Student", jwt.GetClaim("role").Value);
        Assert.Equal($"Demo Öğrenci {student}", jwt.GetClaim("name").Value);
        Assert.InRange(login.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromHours(3.99), TimeSpan.FromHours(4.01));
    }

    [Fact]
    public async Task VT05_RequestWithoutToken_Returns401()
    {
        var response = await fixture.Server.CreateClient().GetAsync("/api/auth/me");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.TokenInvalid);
    }

    [Fact]
    public async Task VT05_ExpiredToken_Returns401_TokenExpired()
    {
        var user = new User { Id = 1, Username = "x", FullName = "x", Role = UserRole.Instructor };
        var expired = new JwtTokenService(Options.Create(new JwtOptions { SigningKey = ProgLabServerFactory.SigningKey, TokenLifetimeHours = -1 })).Create(user);

        var response = await GetMeWithTokenAsync(expired.Token);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.TokenExpired);
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_Returns401_TokenInvalid()
    {
        var otherKey = Convert.ToBase64String(Enumerable.Repeat((byte)7, 64).ToArray());
        var user = new User { Id = 1, Username = "x", FullName = "x", Role = UserRole.Instructor };
        var forged = new JwtTokenService(Options.Create(new JwtOptions { SigningKey = otherKey })).Create(user);

        var response = await GetMeWithTokenAsync(forged.Token);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.TokenInvalid);
    }

    [Theory]
    [InlineData("/api/users")]
    [InlineData("/api/classes")]
    public async Task VT06_StudentToken_OnInstructorEndpoints_Returns403(string path)
    {
        var student = path.Contains("users") ? 9 : 10;
        var login = await new ApiClient(fixture.Server.CreateClient()).FirstLoginAsync(TestAccounts.StudentNumber(student), TestAccounts.StudentEmail(student), TestAccounts.StudentPassword);
        var client = fixture.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        var get = await client.GetAsync(path);
        var post = await client.PostAsJsonAsync(path, new { username = "1", fullName = "x", email = "a@b.c", name = "x", term = "y" });

        await AssertProblemAsync(get, HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await AssertProblemAsync(post, HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ResetFirstLogin_InvalidatesExistingToken_AndAllowsNewFirstLogin()
    {
        const int student = 11;
        var session = await TestAccounts.StudentAsync(fixture.Server, student);
        var instructor = await TestAccounts.InstructorAsync(fixture.Server);
        var id = (await session.GetMeAsync()).Id;

        await instructor.ResetFirstLoginAsync(id);

        var expiredEvents = 0;
        session.SessionExpired += () => expiredEvents++;
        var error = await Assert.ThrowsAsync<ApiException>(() => session.GetMeAsync());
        Assert.Equal(ErrorCodes.TokenInvalid, error.Code);
        Assert.Equal(1, expiredEvents);

        var relogin = await new ApiClient(fixture.Server.CreateClient()).FirstLoginAsync(TestAccounts.StudentNumber(student), TestAccounts.StudentEmail(student), "Yepyeni-Parola-1");
        Assert.False(string.IsNullOrEmpty(relogin.Token));
    }

    [Fact]
    public async Task MalformedRequest_Returns400_WithCode()
    {
        var response = await fixture.Server.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "", password = "" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
    }

    private async Task<HttpResponseMessage> GetMeWithTokenAsync(string token)
    {
        var client = fixture.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync("/api/auth/me");
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }
}

/// <summary>Hız sınırı: bu sınıf gerçek değerle (dakikada 10) kendi sunucusunu başlatır.</summary>
public sealed class AuthRateLimitTests
{
    [Fact]
    public async Task EleventhLoginAttemptInAMinute_Returns429_ButOtherEndpointsKeepWorking()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var server = new ProgLabServerFactory(database.ConnectionString, authPermitPerMinute: 10);
        var api = new ApiClient(server.CreateClient());

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var error = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync("egitmen", "yanlis-parola", UserRole.Instructor));
            Assert.Equal(ErrorCodes.InvalidCredentials, error.Code);
        }

        var limited = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync("egitmen", ProgLabServerFactory.InstructorTemporaryPassword, UserRole.Instructor));
        var limitedFirstLogin = await Assert.ThrowsAsync<ApiException>(() => api.FirstLoginAsync("2026900001", "x@y.z", "Parola-12345"));

        Assert.Equal(ErrorCodes.RateLimit, limited.Code);
        Assert.Equal(429, limited.StatusCode);
        Assert.Equal(ErrorCodes.RateLimit, limitedFirstLogin.Code);
        Assert.Equal("ok", (await api.GetHealthAsync()).Status);
    }
}
