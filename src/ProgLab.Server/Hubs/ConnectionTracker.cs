using ProgLab.Shared.Dtos;

namespace ProgLab.Server.Hubs;

/// <summary>
/// Bağlı öğrencilerin bellek içi listesi. Bir öğrencinin birden çok bağlantısı olabilir (ör. yeniden bağlanma):
/// listeden yalnızca son bağlantısı kapanınca düşer. Sunucu yeniden başlayınca istemciler yeniden bağlanıp listeyi doldurur.
/// </summary>
public sealed class ConnectionTracker
{
    private readonly Dictionary<string, StudentConnectionDto> _connections = [];
    private readonly Lock _lock = new();

    /// <summary>Öğrencinin ilk bağlantısıysa true döner.</summary>
    public bool Add(string connectionId, StudentConnectionDto student)
    {
        lock (_lock)
        {
            var isFirst = _connections.Values.All(c => c.StudentId != student.StudentId);
            _connections[connectionId] = student;
            return isFirst;
        }
    }

    /// <summary>Öğrencinin son bağlantısı kapandıysa öğrenciyi döner.</summary>
    public StudentConnectionDto? Remove(string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.Remove(connectionId, out var student))
            {
                return null;
            }

            return _connections.Values.Any(c => c.StudentId == student.StudentId) ? null : student;
        }
    }

    public IReadOnlyList<string> GetConnectionIds(int studentId)
    {
        lock (_lock)
        {
            return _connections.Where(c => c.Value.StudentId == studentId).Select(c => c.Key).ToList();
        }
    }

    public IReadOnlyList<StudentConnectionDto> GetStudents()
    {
        lock (_lock)
        {
            return _connections.Values
                .GroupBy(c => c.StudentId)
                .Select(g => g.Last())
                .OrderBy(c => c.FullName)
                .ToList();
        }
    }
}
