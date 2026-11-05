namespace ProgLab.Server.Data.Entities;

/// <summary>Öğrenci–şube üyeliği. Bir öğrenci birden çok şubede olabilir.</summary>
public class ClassMember
{
    public int ClassId { get; set; }
    public int StudentId { get; set; }
    public DateTimeOffset AddedAt { get; set; }

    public SchoolClass? Class { get; set; }
    public User? Student { get; set; }
}
