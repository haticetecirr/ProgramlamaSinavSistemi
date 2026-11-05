using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Errors;
using ProgLab.Server.Logging;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Sınav ve ödev yaşam döngüsü: taslak, soru listesi, yayınlama, bitirme, kopyalama.</summary>
public sealed class ExamService(AppDbContext db)
{
    private const int MaxPageSize = 200;

    public async Task<PagedResult<ExamSummaryDto>> ListAsync(ExamStatus? status, int? classId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Exams.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(e => e.Status == status);
        }

        if (classId is not null)
        {
            query = query.Where(e => e.ClassId == classId);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new ExamSummaryDto(
                e.Id,
                e.Title,
                e.Kind,
                e.ClassId,
                e.Class!.Name,
                e.Status,
                e.DurationMinutes,
                e.StartsAt,
                e.EndsAt,
                e.Questions.Count,
                e.Questions.Sum(q => (decimal?)q.Points) ?? 0))
            .ToListAsync(ct);

        return new PagedResult<ExamSummaryDto>(items, total, page, pageSize);
    }

    public async Task<InstructorExamDto> GetAsync(int id, CancellationToken ct)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Class)
            .Include(e => e.Questions).ThenInclude(q => q.Question)
            .SingleOrDefaultAsync(e => e.Id == id, ct);

        return ToDto(exam ?? throw NotFound());
    }

    public async Task<InstructorExamDto> CreateAsync(SaveExamRequest request, int userId, CancellationToken ct)
    {
        ExamRules.ValidateDraft(request);
        await EnsureClassExistsAsync(request.ClassId!.Value, ct);

        var now = DateTimeOffset.UtcNow;
        var exam = new Exam
        {
            Title = request.Title.Trim(),
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyDraftFields(exam, request);
        db.Exams.Add(exam);
        await db.SaveChangesAsync(ct);

        return await GetAsync(exam.Id, ct);
    }

    public async Task<InstructorExamDto> UpdateAsync(int id, SaveExamRequest request, CancellationToken ct)
    {
        var exam = await FindDraftAsync(id, ct);

        ExamRules.ValidateDraft(request);
        await EnsureClassExistsAsync(request.ClassId!.Value, ct);

        if (request.RowVersion is { } rowVersion)
        {
            // İstemcinin okuduğu sürümle karşılaştır: arada başka pencere kaydettiyse 409 döner.
            db.Entry(exam).Property(e => e.RowVersion).OriginalValue = rowVersion;
        }

        exam.Title = request.Title.Trim();
        exam.UpdatedAt = DateTimeOffset.UtcNow;
        ApplyDraftFields(exam, request);
        await db.SaveChangesAsync(ct);

        return await GetFreshAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var exam = await FindDraftAsync(id, ct);
        db.Exams.Remove(exam);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Soru listesini verilen listeyle tümüyle değiştirir (yalnızca taslak).</summary>
    public async Task<InstructorExamDto> ReplaceQuestionsAsync(int id, IReadOnlyList<ExamQuestionInput> input, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var exam = await db.Exams.Include(e => e.Questions).SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw NotFound();
        EnsureDraft(exam);
        ExamRules.ValidateQuestionList(input);

        // Bankadaki güncel sorular eklenebilir. Sınavda zaten olanlar kalabilir: kopyalanan sınav eski sürümleri kullanır.
        var alreadyInExam = exam.Questions.Select(q => q.QuestionId).ToHashSet();
        var ids = input.Select(q => q.QuestionId).ToList();
        var defaults = await db.Questions.AsNoTracking()
            .Where(q => ids.Contains(q.Id) && ((q.IsCurrent && !q.IsDeleted) || alreadyInExam.Contains(q.Id)))
            .ToDictionaryAsync(q => q.Id, q => q.DefaultPoints, ct);
        if (defaults.Count != ids.Count)
        {
            throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.QuestionNotFound, "Listedeki bir soru bankada bulunamadı.");
        }

        // Benzersiz indeksler çakışmasın diye önce eskiler silinir, sonra yeniler eklenir.
        db.ExamQuestions.RemoveRange(exam.Questions);
        await db.SaveChangesAsync(ct);

        exam.Questions = input
            .Select(q => new ExamQuestion
            {
                QuestionId = q.QuestionId,
                OrderNo = q.OrderNo,
                Points = q.Points ?? defaults[q.QuestionId]
            })
            .ToList();
        exam.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await GetFreshAsync(id, ct);
    }

    /// <summary>Tek işlem: doğrula → soruları kilitle → zamanları yaz → logla. Biri başarısızsa hiçbiri olmaz.</summary>
    public async Task<InstructorExamDto> PublishAsync(int id, int userId, IPAddress? ip, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var exam = await db.Exams
            .Include(e => e.Questions).ThenInclude(q => q.Question)
            .SingleOrDefaultAsync(e => e.Id == id, ct)
            ?? throw NotFound();

        EnsureDraft(exam);

        if (exam.Questions.Count == 0)
        {
            throw Unprocessable(ErrorCodes.ExamHasNoQuestions, "Sınavda en az bir soru olmalıdır.");
        }

        if (exam.Questions.Any(q => q.Question!.IsDeleted))
        {
            throw Unprocessable(ErrorCodes.ExamHasDeletedQuestion, "Sınavda bankadan silinmiş bir soru var. Soruyu listeden çıkarın.");
        }

        if (!await db.ClassMembers.AnyAsync(m => m.ClassId == exam.ClassId, ct))
        {
            throw Unprocessable(ErrorCodes.ClassHasNoStudents, "Seçilen şubede öğrenci yok.");
        }

        var now = DateTimeOffset.UtcNow;
        var (startsAt, endsAt) = ExamRules.PublishWindow(exam.Kind, exam.DurationMinutes, exam.StartsAt, exam.EndsAt, now);

        foreach (var examQuestion in exam.Questions)
        {
            examQuestion.Question!.IsLocked = true;
        }

        exam.Status = ExamStatus.Published;
        exam.StartsAt = startsAt;
        exam.EndsAt = endsAt;
        exam.PublishedAt = now;
        exam.UpdatedAt = now;

        Log(ActivityEvents.ExamPublished, exam.Id, userId, ip, new { questionCount = exam.Questions.Count, startsAt, endsAt });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await GetFreshAsync(id, ct);
    }

    /// <summary>
    /// Eğitmenin erken bitirmesi. Açık gönderimlerin otomatik teslimi Aşama 8'de bu akışa eklenir.
    /// </summary>
    public async Task<InstructorExamDto> EndAsync(int id, int userId, IPAddress? ip, CancellationToken ct)
    {
        var exam = await db.Exams.SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw NotFound();

        if (exam.Status != ExamStatus.Published)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.InvalidState, "Yalnızca yayındaki sınav bitirilebilir.");
        }

        var now = DateTimeOffset.UtcNow;
        var (startsAt, endsAt) = ExamRules.EarlyEndWindow(exam.StartsAt!.Value, now);

        exam.Status = ExamStatus.Completed;
        exam.StartsAt = startsAt;
        exam.EndsAt = endsAt;
        exam.CompletedAt = now;
        exam.UpdatedAt = now;

        Log(ActivityEvents.ExamEnded, exam.Id, userId, ip, new { reason = "Manual" });

        await db.SaveChangesAsync(ct);

        return await GetFreshAsync(id, ct);
    }

    /// <summary>Tamamlanmış sınavdan yeni taslak: aynı (kilitli) soru sürümleri ve puanlarla.</summary>
    public async Task<InstructorExamDto> CopyAsync(int id, int userId, CancellationToken ct)
    {
        var source = await db.Exams.AsNoTracking().Include(e => e.Questions).SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw NotFound();

        if (source.Status != ExamStatus.Completed)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.InvalidState, "Yalnızca tamamlanmış sınav kopyalanabilir.");
        }

        var now = DateTimeOffset.UtcNow;
        var copy = new Exam
        {
            Title = ExamRules.CopyTitle(source.Title),
            Description = source.Description,
            Kind = source.Kind,
            ClassId = source.ClassId,
            DurationMinutes = source.DurationMinutes,
            // Sınavın zamanı yayınlamada belirlenir. Ödevin eski tarihleri yeni tarih girilene kadar yayınlanamaz.
            StartsAt = source.Kind == ExamKind.Assignment ? source.StartsAt : null,
            EndsAt = source.Kind == ExamKind.Assignment ? source.EndsAt : null,
            Status = ExamStatus.Draft,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
            Questions = source.Questions
                .Select(q => new ExamQuestion { QuestionId = q.QuestionId, OrderNo = q.OrderNo, Points = q.Points })
                .ToList()
        };
        db.Exams.Add(copy);
        await db.SaveChangesAsync(ct);

        return await GetAsync(copy.Id, ct);
    }

    private static void ApplyDraftFields(Exam exam, SaveExamRequest request)
    {
        exam.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        exam.Kind = request.Kind;
        exam.ClassId = request.ClassId!.Value;

        if (request.Kind == ExamKind.Exam)
        {
            exam.DurationMinutes = request.DurationMinutes;
            exam.StartsAt = null;
            exam.EndsAt = null;
        }
        else
        {
            exam.DurationMinutes = null;
            exam.StartsAt = request.StartsAt!.Value.ToUniversalTime();
            exam.EndsAt = request.EndsAt!.Value.ToUniversalTime();
        }
    }

    private void Log(string eventType, int examId, int userId, IPAddress? ip, object details) =>
        db.ActivityLogs.Add(new ActivityLog
        {
            EventType = eventType,
            ExamId = examId,
            UserId = userId,
            Ip = ip,
            Details = JsonSerializer.Serialize(details)
        });

    private async Task<Exam> FindDraftAsync(int id, CancellationToken ct)
    {
        var exam = await db.Exams.SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw NotFound();
        EnsureDraft(exam);
        return exam;
    }

    private static void EnsureDraft(Exam exam)
    {
        if (exam.Status != ExamStatus.Draft)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.ExamNotDraft, "Yalnızca taslak sınav değiştirilebilir.");
        }
    }

    private async Task EnsureClassExistsAsync(int classId, CancellationToken ct)
    {
        if (!await db.Classes.AnyAsync(c => c.Id == classId, ct))
        {
            throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.ClassNotFound, "Şube bulunamadı.");
        }
    }

    /// <summary>İzlenen eski nesneler yerine veritabanındaki son hâli okur (yeni xmin dahil).</summary>
    private Task<InstructorExamDto> GetFreshAsync(int id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        return GetAsync(id, ct);
    }

    private static InstructorExamDto ToDto(Exam e)
    {
        var questions = e.Questions.OrderBy(q => q.OrderNo).ToList();
        return new InstructorExamDto(
            e.Id,
            e.Title,
            e.Description,
            e.Kind,
            e.ClassId,
            e.Class!.Name,
            e.Status,
            e.DurationMinutes,
            e.StartsAt,
            e.EndsAt,
            e.PublishedAt,
            e.CompletedAt,
            e.RowVersion,
            questions.Sum(q => q.Points),
            questions.Select(q => new ExamQuestionDto(
                q.Id,
                q.QuestionId,
                q.OrderNo,
                q.Points,
                q.Question!.Title,
                q.Question.Type,
                q.Question.VersionNo,
                q.Question.IsCurrent,
                q.Question.IsDeleted)).ToList());
    }

    private static ApiException Unprocessable(string code, string title) =>
        new(StatusCodes.Status422UnprocessableEntity, code, title);

    private static ApiException NotFound() =>
        new(StatusCodes.Status404NotFound, ErrorCodes.ExamNotFound, "Sınav bulunamadı.");
}
