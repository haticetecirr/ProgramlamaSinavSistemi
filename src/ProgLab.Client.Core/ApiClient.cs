using System.Net.Http.Json;
using ProgLab.Shared.Dtos;

namespace ProgLab.Client.Core;

/// <summary>
/// Masaüstü uygulamalarının sunucuyla konuştuğu tek sınıf. ViewModel'ler doğrudan HttpClient kullanmaz.
/// Şimdilik yalnızca sağlık kontrolü vardır; diğer uç noktalar ilgili aşamalarda eklenir.
/// </summary>
public sealed class ApiClient(HttpClient http)
{
    public async Task<HealthResponse> GetHealthAsync(CancellationToken ct = default)
    {
        var health = await http.GetFromJsonAsync<HealthResponse>("api/health", ct);
        return health ?? throw new InvalidOperationException("Sunucu boş bir cevap döndürdü.");
    }
}
