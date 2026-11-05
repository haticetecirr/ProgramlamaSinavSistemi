namespace ProgLab.Server.Data.Entities;

/// <summary>Şube. "Class" C#'ta anahtar sözcüğe çok yakın olduğu için sınıf adı SchoolClass; tablo adı "classes".</summary>
public class SchoolClass
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Term { get; set; }
    public int InstructorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? Instructor { get; set; }
    public List<ClassMember> Members { get; set; } = [];
}
