namespace ProgLab.Server.Data.Entities;

/// <summary>Soru kategorisi; ParentId ile alt kategori desteklenir.</summary>
public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? ParentId { get; set; }

    public Category? Parent { get; set; }
}
