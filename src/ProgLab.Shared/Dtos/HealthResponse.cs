namespace ProgLab.Shared.Dtos;

/// <summary>GET /api/health cevabı.</summary>
/// <param name="Status">Sunucunun durumu, ör. "ok".</param>
/// <param name="Database">Veritabanı durumu. Aşama 2'ye kadar "not_configured" döner.</param>
/// <param name="Version">Sunucu sürümü.</param>
public sealed record HealthResponse(string Status, string Database, string Version);
