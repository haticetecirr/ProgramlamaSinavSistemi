namespace ProgLab.Shared.Dtos;

/// <summary>GET /api/health cevabı.</summary>
/// <param name="Status">Sunucunun durumu: "ok" veya "unavailable".</param>
/// <param name="Database">Veritabanı durumu: "ok" veya "unreachable".</param>
/// <param name="Version">Sunucu sürümü.</param>
public sealed record HealthResponse(string Status, string Database, string Version);
