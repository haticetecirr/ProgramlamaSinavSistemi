using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProgLab.Shared;

namespace ProgLab.Server.Errors;

/// <summary>İstek hattının en dışında durur; servislerden gelen hataları tek biçimde cevaba çevirir.</summary>
public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException ex)
        {
            await ApiProblem.WriteAsync(context, ex.StatusCode, ex.Code, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            // xmin kontrolü: aynı sınav bu sırada başka bir pencereden değişti.
            await ApiProblem.WriteAsync(context, StatusCodes.Status409Conflict, ErrorCodes.InvalidState, "Bu kayıt başka bir pencerede değişti.");
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            logger.LogError(ex, "Veritabanına ulaşılamadı.");
            await ApiProblem.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, ErrorCodes.DatabaseUnavailable, "Veritabanına ulaşılamıyor.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Beklenmeyen sunucu hatası.");
            await ApiProblem.WriteAsync(context, StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Sunucuda beklenmeyen bir hata oluştu.");
        }
    }

    // PostgresException de NpgsqlException'dır ama sunucudan gelen bir SQL hatasıdır; bağlantı sorunu değildir.
    private static bool IsDatabaseUnavailable(Exception ex) =>
        ex is NpgsqlException and not PostgresException || ex.InnerException is NpgsqlException and not PostgresException;
}
