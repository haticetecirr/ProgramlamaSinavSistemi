using System.Net;
using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Logging;
using ProgLab.Server.Errors;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Grading;

/// <summary>
/// Gönderim puanlama. Tekrar çalıştırılması aynı sonucu verir; eğitmenin elle verdiği puana dokunmaz.
/// Teslimde, otomatik teslimde ve elle düzeltmeden sonra, çağıranın işlemi içinde çalışır.
/// </summary>
public sealed class GradingService(AppDbContext db, IEnumerable<IGrader> graders, ActivityRecorder recorder)
{
    private const int MaxNoteLength = 300;

    private readonly Dictionary<QuestionType, IGrader> _graders = graders.ToDictionary(g => g.Type);

    // Süre sonunda bir sınavın tüm gönderimleri puanlanır: cevap anahtarı bir kez okunur.
    private readonly Dictionary<int, List<ExamQuestion>> _answerKeys = [];

    /// <summary>Cevapların puanını ve gönderimin toplamını yazar. Kaydetmek çağıranın işidir.</summary>
    public async Task<decimal> GradeSubmissionAsync(Submission submission, CancellationToken ct)
    {
        var questions = await GetAnswerKeyAsync(submission.ExamId, ct);
        var answers = await db.Answers.Where(a => a.SubmissionId == submission.Id).ToListAsync(ct);

        var total = 0m;
        foreach (var question in questions)
        {
            var answer = answers.SingleOrDefault(a => a.ExamQuestionId == question.Id);
            if (answer?.GradedBy == GradedBy.Manual)
            {
                total += answer.Score ?? 0m;
                continue;
            }

            var result = _graders[question.Question!.Type].Grade(question, answer);
            total += result.Score;

            // Cevap satırı yoksa soru boş bırakılmıştır: toplama 0 olarak girer.
            if (answer is not null)
            {
                answer.Score = result.Score;
                answer.GradedBy = GradedBy.Auto;
            }
        }

        submission.TotalScore = total;
        submission.GradedAt = DateTimeOffset.UtcNow;
        return total;
    }

    /// <summary>Elle düzeltme. Yalnızca klasik sorularda; toplam yeniden hesaplanır ve loglanır.</summary>
    public async Task<OverrideScoreResponse> OverrideScoreAsync(long answerId, OverrideScoreRequest request, int instructorId, IPAddress? ip, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var submissionId = await db.Answers.AsNoTracking()
            .Where(a => a.Id == answerId)
            .Select(a => (long?)a.SubmissionId)
            .SingleOrDefaultAsync(ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.AnswerNotFound, "Cevap bulunamadı.");

        // Aynı anda gelen teslimle çakışmasın.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM submissions WHERE id = {submissionId} FOR UPDATE", ct);

        var submission = await db.Submissions.Include(s => s.Student).SingleAsync(s => s.Id == submissionId, ct);
        var answer = await db.Answers
            .Include(a => a.ExamQuestion).ThenInclude(q => q!.Question)
            .SingleAsync(a => a.Id == answerId, ct);

        if (answer.ExamQuestion!.Question!.Type != QuestionType.Classic)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.NotClassicQuestion, "Elle düzeltme yalnızca klasik sorularda yapılır.");
        }

        if (submission.Status == SubmissionStatus.InProgress)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.InvalidState, "Teslim edilmemiş cevabın puanı düzeltilemez.");
        }

        var points = answer.ExamQuestion.Points;
        if (request.Score < 0 || request.Score > points || request.Score != decimal.Round(request.Score, 2))
        {
            throw new ApiException(StatusCodes.Status422UnprocessableEntity, ErrorCodes.ScoreOutOfRange, $"Puan 0 ile {points:0.##} arasında, en fazla iki ondalıklı olmalıdır.");
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > MaxNoteLength)
        {
            throw new ApiException(StatusCodes.Status422UnprocessableEntity, ErrorCodes.InvalidRequest, $"Not en fazla {MaxNoteLength} karakter olabilir.");
        }

        var oldScore = answer.Score;
        answer.Score = request.Score;
        answer.GradedBy = GradedBy.Manual;
        answer.GradedNote = note;

        var total = await GradeSubmissionAsync(submission, ct);

        recorder.Add(
            ActivityEvents.ScoreOverridden,
            submission.ExamId,
            instructorId,
            submission,
            null,
            ip,
            new { answerId, oldScore, newScore = request.Score, note },
            $"{submission.Student!.Username}: {ExamLogWriter.FormatScore(oldScore ?? 0m)} -> {ExamLogWriter.FormatScore(request.Score)} puan, toplam {ExamLogWriter.FormatScore(total)}");

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await recorder.WriteFilesAsync(ct);

        return new OverrideScoreResponse(request.Score, total);
    }

    private async Task<List<ExamQuestion>> GetAnswerKeyAsync(int examId, CancellationToken ct)
    {
        if (!_answerKeys.TryGetValue(examId, out var questions))
        {
            // Sınav kilitli soru sürümüne bağlıdır: bankadaki yeni sürüm puanı etkilemez.
            questions = await db.ExamQuestions.AsNoTracking()
                .Where(q => q.ExamId == examId)
                .Include(q => q.Question).ThenInclude(q => q!.Options)
                .Include(q => q.Question).ThenInclude(q => q!.AnswerKeys)
                .AsSplitQuery()
                .ToListAsync(ct);
            _answerKeys[examId] = questions;
        }

        return questions;
    }
}
