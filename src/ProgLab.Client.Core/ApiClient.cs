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

    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<CategoryDto>>(HttpMethod.Get, "api/categories", null, ct);

    public Task<CategoryDto> CreateCategoryAsync(string name, int? parentId, CancellationToken ct = default) =>
        SendAsync<CategoryDto>(HttpMethod.Post, "api/categories", new SaveCategoryRequest(name, parentId), ct);

    public Task<CategoryDto> UpdateCategoryAsync(int id, string name, int? parentId, CancellationToken ct = default) =>
        SendAsync<CategoryDto>(HttpMethod.Put, $"api/categories/{id}", new SaveCategoryRequest(name, parentId), ct);

    public Task DeleteCategoryAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/categories/{id}", null, ct);

    public Task<PagedResult<QuestionSummaryDto>> GetQuestionsAsync(int? categoryId = null, QuestionType? type = null, string? search = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var path = $"api/questions?page={page}&pageSize={pageSize}";
        if (categoryId is not null)
        {
            path += $"&categoryId={categoryId}";
        }

        if (type is not null)
        {
            path += $"&type={type}";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            path += $"&search={Uri.EscapeDataString(search)}";
        }

        return SendAsync<PagedResult<QuestionSummaryDto>>(HttpMethod.Get, path, null, ct);
    }

    public Task<InstructorQuestionDto> GetQuestionAsync(int id, CancellationToken ct = default) =>
        SendAsync<InstructorQuestionDto>(HttpMethod.Get, $"api/questions/{id}", null, ct);

    public Task<InstructorQuestionDto> CreateQuestionAsync(SaveQuestionRequest request, CancellationToken ct = default) =>
        SendAsync<InstructorQuestionDto>(HttpMethod.Post, "api/questions", request, ct);

    public Task<UpdateQuestionResponse> UpdateQuestionAsync(int id, SaveQuestionRequest request, CancellationToken ct = default) =>
        SendAsync<UpdateQuestionResponse>(HttpMethod.Put, $"api/questions/{id}", request, ct);

    public Task DeleteQuestionAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/questions/{id}", null, ct);

    public Task<PagedResult<ExamSummaryDto>> GetExamsAsync(ExamStatus? status = null, int? classId = null, int page = 1, int pageSize = 200, CancellationToken ct = default)
    {
        var path = $"api/exams?page={page}&pageSize={pageSize}";
        if (status is not null)
        {
            path += $"&status={status}";
        }

        if (classId is not null)
        {
            path += $"&classId={classId}";
        }

        return SendAsync<PagedResult<ExamSummaryDto>>(HttpMethod.Get, path, null, ct);
    }

    public Task<InstructorExamDto> GetExamAsync(int id, CancellationToken ct = default) =>
        SendAsync<InstructorExamDto>(HttpMethod.Get, $"api/exams/{id}", null, ct);

    public Task<InstructorExamDto> CreateExamAsync(SaveExamRequest request, CancellationToken ct = default) =>
        SendAsync<InstructorExamDto>(HttpMethod.Post, "api/exams", request, ct);

    public Task<InstructorExamDto> UpdateExamAsync(int id, SaveExamRequest request, CancellationToken ct = default) =>
        SendAsync<InstructorExamDto>(HttpMethod.Put, $"api/exams/{id}", request, ct);

    public Task DeleteExamAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/exams/{id}", null, ct);

    public Task<InstructorExamDto> ReplaceExamQuestionsAsync(int id, IReadOnlyList<ExamQuestionInput> questions, CancellationToken ct = default) =>
        SendAsync<InstructorExamDto>(HttpMethod.Put, $"api/exams/{id}/questions", questions, ct);

    public Task<InstructorExamDto> PublishExamAsync(int id, CancellationToken ct = default) =>
        SendAsync<InstructorExamDto>(HttpMethod.Post, $"api/exams/{id}/publish", null, ct);

    public Task<InstructorExamDto> EndExamAsync(int id, CancellationToken ct = default) =>
        SendAsync<InstructorExamDto>(HttpMethod.Post, $"api/exams/{id}/end", null, ct);

    public Task<InstructorExamDto> CopyExamAsync(int id, CancellationToken ct = default) =>
        SendAsync<InstructorExamDto>(HttpMethod.Post, $"api/exams/{id}/copy", null, ct);

    public Task<IReadOnlyList<StudentExamSummaryDto>> GetLobbyAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<StudentExamSummaryDto>>(HttpMethod.Get, "api/student/exams", null, ct);

    public Task<JoinExamResponse> JoinExamAsync(int examId, CancellationToken ct = default) =>
        SendAsync<JoinExamResponse>(HttpMethod.Post, $"api/student/exams/{examId}/join", null, ct);

    public Task<StudentPaperDto> GetPaperAsync(int examId, CancellationToken ct = default) =>
        SendAsync<StudentPaperDto>(HttpMethod.Get, $"api/student/exams/{examId}/paper", null, ct);

    public Task SaveAnswerAsync(long submissionId, int examQuestionId, SaveAnswerRequest answer, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/student/submissions/{submissionId}/answers/{examQuestionId}", answer, ct);

    public Task<SubmitResponse> SubmitAsync(long submissionId, CancellationToken ct = default) =>
        SendAsync<SubmitResponse>(HttpMethod.Post, $"api/student/submissions/{submissionId}/submit", null, ct);

    public Task<OverrideScoreResponse> OverrideScoreAsync(long answerId, OverrideScoreRequest request, CancellationToken ct = default) =>
        SendAsync<OverrideScoreResponse>(HttpMethod.Put, $"api/answers/{answerId}/score", request, ct);

    public Task<LiveExamDto> GetLiveExamAsync(int examId, CancellationToken ct = default) =>
        SendAsync<LiveExamDto>(HttpMethod.Get, $"api/exams/{examId}/live", null, ct);

    public Task<IReadOnlyList<StudentConnectionDto>> GetConnectedStudentsAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<StudentConnectionDto>>(HttpMethod.Get, "api/connections/students", null, ct);

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
