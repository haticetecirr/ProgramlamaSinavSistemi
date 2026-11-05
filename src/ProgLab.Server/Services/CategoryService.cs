using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Errors;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;

namespace ProgLab.Server.Services;

/// <summary>Alt kategori destekli soru kategorileri.</summary>
public sealed class CategoryService(AppDbContext db)
{
    public async Task<IReadOnlyList<CategoryDto>> GetTreeAsync(CancellationToken ct)
    {
        var all = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        var childrenOf = all.ToLookup(c => c.ParentId);

        IReadOnlyList<CategoryDto> Build(int? parentId) =>
            childrenOf[parentId].Select(c => new CategoryDto(c.Id, c.Name, c.ParentId, Build(c.Id))).ToList();

        return Build(null);
    }

    public async Task<CategoryDto> CreateAsync(SaveCategoryRequest request, CancellationToken ct)
    {
        await EnsureParentExistsAsync(request.ParentId, ct);

        var category = new Category { Name = request.Name.Trim(), ParentId = request.ParentId };
        db.Categories.Add(category);
        await SaveWithUniqueCheckAsync(ct);

        return new CategoryDto(category.Id, category.Name, category.ParentId, []);
    }

    /// <summary>Yeniden adlandırma ve başka üst kategoriye taşıma.</summary>
    public async Task<CategoryDto> UpdateAsync(int id, SaveCategoryRequest request, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();

        await EnsureParentExistsAsync(request.ParentId, ct);
        await EnsureNoCycleAsync(id, request.ParentId, ct);

        category.Name = request.Name.Trim();
        category.ParentId = request.ParentId;
        await SaveWithUniqueCheckAsync(ct);

        return new CategoryDto(category.Id, category.Name, category.ParentId, []);
    }

    /// <summary>
    /// Alt kategorisi veya sorusu olan kategori silinemez. Eski sürümler ve yumuşak silinmiş sorular
    /// da kategoriye bağlı kaldığı (yayınlanmış sınavlar onları kullanır) için "soru" sayılır.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();

        var hasChildren = await db.Categories.AnyAsync(c => c.ParentId == id, ct);
        var hasQuestions = await db.Questions.AnyAsync(q => q.CategoryId == id, ct);
        if (hasChildren || hasQuestions)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.CategoryNotEmpty, "İçinde soru veya alt kategori bulunan kategori silinemez.");
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureParentExistsAsync(int? parentId, CancellationToken ct)
    {
        if (parentId is not null && !await db.Categories.AnyAsync(c => c.Id == parentId, ct))
        {
            throw NotFound();
        }
    }

    /// <summary>Kategori kendisinin veya kendi alt kategorisinin altına taşınamaz.</summary>
    private async Task EnsureNoCycleAsync(int id, int? newParentId, CancellationToken ct)
    {
        var parents = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentId, ct);

        for (var current = newParentId; current is not null; current = parents[current.Value])
        {
            if (current == id)
            {
                throw new ApiException(StatusCodes.Status422UnprocessableEntity, ErrorCodes.CategoryCycle, "Kategori kendi alt kategorisinin altına taşınamaz.");
            }
        }
    }

    private async Task SaveWithUniqueCheckAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.CategoryNameExists, "Aynı seviyede bu adda bir kategori var.");
        }
    }

    private static ApiException NotFound() =>
        new(StatusCodes.Status404NotFound, ErrorCodes.CategoryNotFound, "Kategori bulunamadı.");
}
