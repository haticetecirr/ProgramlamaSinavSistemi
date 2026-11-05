using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Errors;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Soru bankası: ekleme, düzenleme, sürümleme ve yumuşak silme.</summary>
public sealed class QuestionService(AppDbContext db)
{
    private const int MaxPageSize = 200;

    public async Task<PagedResult<QuestionSummaryDto>> ListAsync(int? categoryId, QuestionType? type, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // Bankada yalnızca güncel ve silinmemiş sürümler görünür.
        var query = db.Questions.AsNoTracking().Where(q => q.IsCurrent && !q.IsDeleted);
        if (categoryId is not null)
        {
            query = query.Where(q => q.CategoryId == categoryId);
        }

        if (type is not null)
        {
            query = query.Where(q => q.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(q => EF.Functions.ILike(q.Title, pattern) || EF.Functions.ILike(q.Text, pattern));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(q => q.Title).ThenBy(q => q.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => new QuestionSummaryDto(q.Id, q.CategoryId, q.Category!.Name, q.Type, q.Title, q.DefaultPoints, q.VersionNo, q.IsLocked))
            .ToListAsync(ct);

        return new PagedResult<QuestionSummaryDto>(items, total, page, pageSize);
    }

    /// <summary>Eski sürümler de okunabilir: yayınlanmış sınavlar ve sonuç ekranları onlara bakar.</summary>
    public async Task<InstructorQuestionDto> GetAsync(int id, CancellationToken ct)
    {
        var question = await db.Questions.AsNoTracking()
            .Include(q => q.Options)
            .Include(q => q.AnswerKeys)
            .SingleOrDefaultAsync(q => q.Id == id, ct);

        return ToDto(question ?? throw NotFound());
    }

    public async Task<InstructorQuestionDto> CreateAsync(SaveQuestionRequest request, int userId, CancellationToken ct)
    {
        QuestionValidator.Validate(request);
        await EnsureCategoryExistsAsync(request.CategoryId, ct);

        var now = DateTimeOffset.UtcNow;
        var question = new Question
        {
            Title = request.Title.Trim(),
            Text = request.Text.Trim(),
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyContent(question, request);
        db.Questions.Add(question);
        await db.SaveChangesAsync(ct);

        return ToDto(question);
    }

    /// <summary>
    /// Kilitli olmayan soru yerinde güncellenir. Kilitli soru (yayınlanmış sınavda kullanılmış) değişmez:
    /// değişiklikler yeni bir sürüme yazılır, eski sürüm bankadan gizlenir ve sınavlar onu kullanmaya devam eder.
    /// </summary>
    public async Task<UpdateQuestionResponse> UpdateAsync(int id, SaveQuestionRequest request, int userId, CancellationToken ct)
    {
        QuestionValidator.Validate(request);

        var question = await db.Questions
            .Include(q => q.Options)
            .Include(q => q.AnswerKeys)
            .SingleOrDefaultAsync(q => q.Id == id && q.IsCurrent && !q.IsDeleted, ct)
            ?? throw NotFound();

        await EnsureCategoryExistsAsync(request.CategoryId, ct);

        return question.IsLocked
            ? await CreateNewVersionAsync(question, request, userId, ct)
            : await UpdateInPlaceAsync(question, request, ct);
    }

    /// <summary>Yumuşak silme: satır ve içeriği korunur, yalnızca bankadan kalkar.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var question = await db.Questions.SingleOrDefaultAsync(q => q.Id == id && q.IsCurrent && !q.IsDeleted, ct) ?? throw NotFound();

        question.IsDeleted = true;
        question.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<UpdateQuestionResponse> UpdateInPlaceAsync(Question question, SaveQuestionRequest request, CancellationToken ct)
    {
        question.Title = request.Title.Trim();
        question.Text = request.Text.Trim();
        question.UpdatedAt = DateTimeOffset.UtcNow;

        // Kilitli olmayan sorunun seçeneklerine bağlı cevap olamaz; alt satırlar güvenle yenilenir.
        db.QuestionOptions.RemoveRange(question.Options);
        db.ClassicAnswerKeys.RemoveRange(question.AnswerKeys);
        await db.SaveChangesAsync(ct);

        ApplyContent(question, request);
        await db.SaveChangesAsync(ct);

        return new UpdateQuestionResponse(null, ToDto(question));
    }

    private async Task<UpdateQuestionResponse> CreateNewVersionAsync(Question old, SaveQuestionRequest request, int userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var newVersion = new Question
        {
            Title = request.Title.Trim(),
            Text = request.Text.Trim(),
            VersionNo = old.VersionNo + 1,
            PreviousVersion = old,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyContent(newVersion, request);
        db.Questions.Add(newVersion);

        old.IsCurrent = false;
        old.UpdatedAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı sürümden iki yeni sürüm oluşamaz (previous_version_id benzersizdir).
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.InvalidState, "Bu soru başka bir pencerede güncellendi. Listeyi yenileyin.");
        }

        return new UpdateQuestionResponse(newVersion.Id, ToDto(newVersion));
    }

    private static void ApplyContent(Question question, SaveQuestionRequest request)
    {
        question.CategoryId = request.CategoryId;
        question.Type = request.Type;
        question.DefaultPoints = request.DefaultPoints;
        question.Options = [];
        question.AnswerKeys = [];

        if (request.Type == QuestionType.MultipleChoice)
        {
            question.Options = request.Options!
                .Select((option, index) => new QuestionOption
                {
                    OrderNo = (short)(index + 1),
                    Text = option.Text.Trim(),
                    IsCorrect = option.IsCorrect
                })
                .ToList();
        }
        else
        {
            question.AnswerKeys = QuestionValidator.NormalizeAnswerKeys(request.AcceptedAnswers!)
                .Select(key => new ClassicAnswerKey { AcceptedText = key.Accepted, NormalizedText = key.Normalized })
                .ToList();
        }
    }

    private async Task EnsureCategoryExistsAsync(int categoryId, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
        {
            throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.CategoryNotFound, "Kategori bulunamadı.");
        }
    }

    private static InstructorQuestionDto ToDto(Question q) => new(
        q.Id,
        q.CategoryId,
        q.Type,
        q.Title,
        q.Text,
        q.DefaultPoints,
        q.VersionNo,
        q.PreviousVersion?.Id ?? q.PreviousVersionId,
        q.IsCurrent,
        q.IsLocked,
        q.IsDeleted,
        q.Options.OrderBy(o => o.OrderNo).Select(o => new InstructorOptionDto(o.Id, o.OrderNo, o.Text, o.IsCorrect)).ToList(),
        q.AnswerKeys.OrderBy(k => k.Id).Select(k => new InstructorAnswerKeyDto(k.Id, k.AcceptedText)).ToList());

    private static ApiException NotFound() =>
        new(StatusCodes.Status404NotFound, ErrorCodes.QuestionNotFound, "Soru bulunamadı.");
}
