using ProgLab.Server.Errors;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Sınav kuralları: veritabanı gerektirmeyen saf hesaplar. Saat dışarıdan verildiği için birim testle doğrulanır.</summary>
public static class ExamRules
{
    public const decimal MaxPoints = 9999.99m; // numeric(6,2) üst sınırı

    /// <summary>Taslak kaydındaki zorunlu alanlar.</summary>
    public static void ValidateDraft(SaveExamRequest request)
    {
        if (!Enum.IsDefined(request.Kind))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, "Sınav türü geçersiz.");
        }

        if (request.ClassId is null)
        {
            throw Unprocessable(ErrorCodes.ClassRequired, "Hedef şube seçilmelidir.");
        }

        if (request.Kind == ExamKind.Exam && request.DurationMinutes is not > 0)
        {
            throw Unprocessable(ErrorCodes.DurationRequired, "Sınav süresi girilmelidir.");
        }

        if (request.Kind == ExamKind.Assignment
            && (request.StartsAt is not { } start || request.EndsAt is not { } end || start >= end))
        {
            throw Unprocessable(ErrorCodes.InvalidTimeWindow, "Ödevin başlangıcı bitişinden önce olmalıdır.");
        }
    }

    /// <summary>Soru listesi kuralları: aynı soru ve aynı sıra iki kez olamaz, puan geçerli olmalıdır.</summary>
    public static void ValidateQuestionList(IReadOnlyList<ExamQuestionInput> questions)
    {
        if (questions.Select(q => q.QuestionId).Distinct().Count() != questions.Count)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.DuplicateQuestion, "Bir soru sınava bir kez eklenebilir.");
        }

        if (questions.Any(q => q.OrderNo < 1) || questions.Select(q => q.OrderNo).Distinct().Count() != questions.Count)
        {
            throw Unprocessable(ErrorCodes.DuplicateOrderNo, "Sıra numaraları 1'den başlamalı ve benzersiz olmalıdır.");
        }

        if (questions.Any(q => q.Points is <= 0 or > MaxPoints))
        {
            throw Unprocessable(ErrorCodes.InvalidPoints, "Puan 0'dan büyük olmalıdır.");
        }
    }

    /// <summary>
    /// Yayınlama anındaki zaman penceresi. Sınavda yayınlama aynı zamanda başlatmadır;
    /// ödev taslakta belirlenen tarihleri korur, bitişi geçmişte olamaz.
    /// </summary>
    public static (DateTimeOffset StartsAt, DateTimeOffset EndsAt) PublishWindow(
        ExamKind kind, int? durationMinutes, DateTimeOffset? startsAt, DateTimeOffset? endsAt, DateTimeOffset now)
    {
        if (kind == ExamKind.Exam)
        {
            if (durationMinutes is not > 0)
            {
                throw Unprocessable(ErrorCodes.DurationRequired, "Sınav süresi girilmelidir.");
            }

            return (now, now.AddMinutes(durationMinutes.Value));
        }

        if (startsAt is not { } start || endsAt is not { } end || start >= end || end <= now)
        {
            throw Unprocessable(ErrorCodes.InvalidTimeWindow, "Ödevin bitişi gelecekte ve başlangıcından sonra olmalıdır.");
        }

        return (start, end);
    }

    /// <summary>
    /// Erken bitirmede bitiş şimdiye çekilir. Veritabanı kısıtı starts_at &lt; ends_at istediği için
    /// henüz başlamamış ödevde başlangıç da bir saniye öncesine alınır.
    /// </summary>
    public static (DateTimeOffset StartsAt, DateTimeOffset EndsAt) EarlyEndWindow(DateTimeOffset startsAt, DateTimeOffset now) =>
        (startsAt < now ? startsAt : now.AddSeconds(-1), now);

    /// <summary>
    /// Lobideki durum yalnızca sunucu saatinden türetilir. Süresi geçmiş ama henüz tamamlanmamış sınav
    /// da öğrenciye "Tamamlandı" görünür; teslim etmiş öğrenci her zaman "Teslim edildi" görür.
    /// </summary>
    public static StudentExamState StudentState(ExamStatus status, DateTimeOffset startsAt, DateTimeOffset endsAt, SubmissionStatus? submission, DateTimeOffset now)
    {
        if (submission is SubmissionStatus.Submitted or SubmissionStatus.AutoSubmitted)
        {
            return StudentExamState.Submitted;
        }

        if (status == ExamStatus.Completed || now >= endsAt)
        {
            return StudentExamState.Completed;
        }

        return now < startsAt ? StudentExamState.Upcoming : StudentExamState.Open;
    }

    /// <summary>Kalan süre yukarı yuvarlanır: son saniyede bile 0 görünmez, süre bitince 0 olur.</summary>
    public static int RemainingSeconds(DateTimeOffset endsAt, DateTimeOffset now) =>
        Math.Max(0, (int)Math.Ceiling((endsAt - now).TotalSeconds));

    /// <summary>Kopya başlığı; sütun sınırını aşarsa asıl başlık kısaltılır.</summary>
    public static string CopyTitle(string title)
    {
        const string suffix = " (kopya)";
        const int maxLength = 200;
        return (title.Length + suffix.Length > maxLength ? title[..(maxLength - suffix.Length)] : title) + suffix;
    }

    private static ApiException Unprocessable(string code, string title) =>
        new(StatusCodes.Status422UnprocessableEntity, code, title);
}
