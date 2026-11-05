using System.Text.Json;
using Npgsql;

namespace ProgLab.Server.Tests.Infrastructure;

/// <summary>
/// Her test sınıfına proglab_test veritabanı içinde kendi geçici şemasını verir.
/// Böylece "boş veritabanı" senaryoları hiçbir veritabanı silinmeden denenir;
/// test bitince yalnızca bu sınıfın oluşturduğu şema silinir. Gerçek "proglab" veritabanına dokunulmaz.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private const string TestDatabaseName = "proglab_test";

    private readonly string _adminConnectionString;

    private TestDatabase(string baseConnectionString, string schema)
    {
        _adminConnectionString = baseConnectionString;
        Schema = schema;
        ConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schema }.ConnectionString;
    }

    public string Schema { get; }

    /// <summary>Geçici şemaya bakan bağlantı dizesi.</summary>
    public string ConnectionString { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var baseConnectionString = ReadTestConnectionString();
        var schema = $"t_{Guid.NewGuid():N}";

        await using var connection = new NpgsqlConnection(baseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection);
        await command.ExecuteNonQueryAsync();

        return new TestDatabase(baseConnectionString, schema);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA \"{Schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string ReadTestConnectionString()
    {
        var path = Path.Combine(SolutionPaths.Root, "src", "ProgLab.Server", "appsettings.Local.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Test ayarı bulunamadı: {path}. Önce scripts/setup-local-db.ps1 çalıştırılmalı.");
        }

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var connectionString = json.RootElement.GetProperty("ConnectionStrings").GetProperty("Test").GetString()!;

        // Yanlış yapılandırmaya karşı son güvence: testler yalnızca test veritabanında çalışır.
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (database != TestDatabaseName)
        {
            throw new InvalidOperationException($"Testler yalnızca {TestDatabaseName} üzerinde çalışır; ayarda '{database}' yazıyor.");
        }

        return connectionString;
    }
}
