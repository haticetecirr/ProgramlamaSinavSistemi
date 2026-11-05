using System.Collections.ObjectModel;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Kategori ağacındaki düğüm.</summary>
public sealed class CategoryNode(CategoryDto category)
{
    public CategoryDto Category { get; } = category;

    public string Name => Category.Name;

    public ObservableCollection<CategoryNode> Children { get; } =
        new(category.Children.Select(child => new CategoryNode(child)));
}

/// <summary>Açılır listelerdeki kategori seçeneği; boş Id "üst kategori yok" demektir.</summary>
public sealed record CategoryOption(int? Id, string Label)
{
    /// <summary>Ağacı "Üst / Alt" yollu düz listeye çevirir.</summary>
    public static IEnumerable<CategoryOption> Flatten(IEnumerable<CategoryDto> categories, string prefix = "") =>
        categories.SelectMany(c => new[] { new CategoryOption(c.Id, prefix + c.Name) }
            .Concat(Flatten(c.Children, $"{prefix}{c.Name} / ")));
}

/// <summary>Tip filtresi seçeneği; boş değer "tüm tipler" demektir.</summary>
public sealed record QuestionTypeFilter(QuestionType? Value, string Label);

/// <summary>Soru listesindeki satır.</summary>
public sealed record QuestionRow(QuestionSummaryDto Question)
{
    public string Title => Question.Title;

    public string Details =>
        $"{(Question.Type == QuestionType.MultipleChoice ? "Çoktan seçmeli" : "Klasik")} · {Question.CategoryName} · {Question.DefaultPoints:0.##} puan · Sürüm {Question.VersionNo}{(Question.IsLocked ? " · Kilitli" : "")}";
}
