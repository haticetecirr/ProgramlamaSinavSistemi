using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Client.Core;

/// <summary>
/// Masaüstü uygulamalarının sunucuyla konuştuğu tek sınıf. ViewModel'ler doğrudan HttpClient kullanmaz.
/// Hatalar Türkçe mesajlı <see cref="ApiException"/> olarak fırlatılır.
/// </summary>
public sealed class ApiClient(HttpClient http, TokenStore? tokens = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TokenStore _tokens = tokens ?? new TokenStore();

    /// <summary>Token geçersiz veya süresi dolmuşsa tetiklenir; uygulama giriş ekranına döner.</summary>
    public event Action? SessionExpired;

    public Task<HealthResponse> GetHealthAsync(CancellationToken ct = default) =>
        SendAsync<HealthResponse>(HttpMethod.Get, "api/health", null, ct);

    public async Task<LoginResponse> LoginAsync(string username, string password, UserRole app, CancellationToken ct = default)
    {
        var response = await SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/login", new LoginRequest(username, password, app), ct);
        _tokens.Set(response.Token, response.ExpiresAt);
        return response;
    }

    public async Task<LoginResponse> FirstLoginAsync(string username, string email, string newPassword, CancellationToken ct = default)
    {
        var response = await SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/first-login", new FirstLoginRequest(username, email, newPassword), ct);
        _tokens.Set(response.Token, response.ExpiresAt);
        return response;
    }

    public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/auth/change-password", new ChangePasswordRequest(currentPassword, newPassword), ct);

    public Task<MeResponse> GetMeAsync(CancellationToken ct = default) =>
        SendAsync<MeResponse>(HttpMethod.Get, "api/auth/me", null, ct);

    public void Logout() => _tokens.Clear();

    public Task<PagedResult<UserDto>> GetStudentsAsync(int? classId = null, string? search = null, int page = 1, int pageSize = 200, CancellationToken ct = default)
    {
        var path = $"api/users?role=Student&page={page}&pageSize={pageSize}";
        if (classId is not null)
        {
            path += $"&classId={classId}";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            path += $"&search={Uri.EscapeDataString(search)}";
        }

        return SendAsync<PagedResult<UserDto>>(HttpMethod.Get, path, null, ct);
    }

    public Task<UserDto> CreateStudentAsync(string username, string fullName, string email, CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Post, "api/users", new CreateUserRequest(username, fullName, email), ct);

    public Task<UserDto> UpdateStudentAsync(int id, string fullName, string email, bool isActive, CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Put, $"api/users/{id}", new UpdateUserRequest(fullName, email, isActive), ct);

    public Task ResetFirstLoginAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/users/{id}/reset-first-login", null, ct);

    public Task<IReadOnlyList<ClassDto>> GetClassesAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<ClassDto>>(HttpMethod.Get, "api/classes", null, ct);

    public Task<ClassDetailDto> GetClassAsync(int id, CancellationToken ct = default) =>
        SendAsync<ClassDetailDto>(HttpMethod.Get, $"api/classes/{id}", null, ct);

    public Task<ClassDto> CreateClassAsync(string name, string term, CancellationToken ct = default) =>
        SendAsync<ClassDto>(HttpMethod.Post, "api/classes", new SaveClassRequest(name, term), ct);

    public Task DeleteClassAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/classes/{id}", null, ct);

    public Task<ClassDetailDto> ReplaceClassMembersAsync(int id, IReadOnlyList<int> studentIds, CancellationToken ct = default) =>
        SendAsync<ClassDetailDto>(HttpMethod.Put, $"api/classes/{id}/members", new UpdateMembersRequest(studentIds), ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, path, body, ct);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return result ?? throw new ApiException("EMPTY_RESPONSE", "Sunucu boş bir cevap döndürdü.");
    }

    private async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, path, body, ct);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        if (_tokens.Token is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ApiException(ApiException.ConnectionFailed, ErrorMessages.For(ApiException.ConnectionFailed));
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw await ToExceptionAsync(response, ct);
        }
    }

    private async Task<ApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        ProblemBody? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Gövde JSON değilse durum kodu yine de bilinir.
        }

        if (problem?.Code is ErrorCodes.TokenExpired or ErrorCodes.TokenInvalid)
        {
            _tokens.Clear();
            SessionExpired?.Invoke();
        }

        return new ApiException(problem?.Code ?? "UNKNOWN", ErrorMessages.For(problem?.Code, problem?.Title), (int)response.StatusCode);
    }

    private sealed record ProblemBody(string? Code, string? Title);
}
