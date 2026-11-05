using ProgLab.Server.Data.Entities;
using ProgLab.Server.Errors;
using ProgLab.Server.Services;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Sınav kuralları ve durum hesapları. Saat dışarıdan verildiği için veritabanı ve bekleme gerekmez.</summary>
public sealed class ExamRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 7, 0, 0, TimeSpan.Zero);

    private static SaveExamRequest Request(ExamKind kind = ExamKind.Exam, int? classId = 1, int? duration = 40, DateTimeOffset? starts = null, DateTimeOffset? ends = null) =>
        new("Başlık", null, kind, classId, duration, starts, ends);

    private static void AssertRejected(Action action, string code, int status = 422)
    {
        var error = Assert.Throws<ApiException>(action);
        Assert.Equal(code, error.Code);
        Assert.Equal(status, error.StatusCode);
    }

    private static Exam PublishedExam() => new()
    {
        Title = "S",
        Status = ExamStatus.Published,
        StartsAt = Now,
        EndsAt = Now.AddMinutes(40)
    };

    [Theory]
    [InlineData(-1, false)]   // başlamadan önce
    [InlineData(0, true)]     // başlangıç anı dahil
    [InlineData(2399, true)]  // son saniye
    [InlineData(2400, false)] // bitiş anı dahil değil: tolerans yok
    [InlineData(2401, false)]
    public void IsOpenAt_FollowsServerClockWindow(int secondsFromStart, bool expected)
    {
        Assert.Equal(expected, PublishedExam().IsOpenAt(Now.AddSeconds(secondsFromStart)));
    }

    [Theory]
    [InlineData(ExamStatus.Draft)]
    [InlineData(ExamStatus.Completed)]
    public void IsOpenAt_IsFalse_WhenNotPublished_EvenInsideWindow(ExamStatus status)
    {
        var exam = PublishedExam();
        exam.Status = status;

        Assert.False(exam.IsOpenAt(Now.AddMinutes(1)));
    }

    [Fact]
    public void IsOpenAt_IsFalse_ForDraftWithoutTimes()
    {
        Assert.False(new Exam { Title = "S" }.IsOpenAt(Now));
    }

    [Fact]
    public void ValidateDraft_AcceptsValidExamAndAssignment()
    {
        ExamRules.ValidateDraft(Request());
        ExamRules.ValidateDraft(Request(ExamKind.Assignment, duration: null, starts: Now, ends: Now.AddDays(1)));
    }

    [Fact]
    public void ValidateDraft_RequiresClass() =>
        AssertRejected(() => ExamRules.ValidateDraft(Request(classId: null)), ErrorCodes.ClassRequired);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void ValidateDraft_RequiresPositiveDuration_ForExam(int? duration) =>
        AssertRejected(() => ExamRules.ValidateDraft(Request(duration: duration)), ErrorCodes.DurationRequired);

    [Fact]
    public void ValidateDraft_RequiresValidWindow_ForAssignment()
    {
        AssertRejected(() => ExamRules.ValidateDraft(Request(ExamKind.Assignment, starts: null, ends: Now)), ErrorCodes.InvalidTimeWindow);
        AssertRejected(() => ExamRules.ValidateDraft(Request(ExamKind.Assignment, starts: Now, ends: null)), ErrorCodes.InvalidTimeWindow);
        AssertRejected(() => ExamRules.ValidateDraft(Request(ExamKind.Assignment, starts: Now, ends: Now)), ErrorCodes.InvalidTimeWindow);
        AssertRejected(() => ExamRules.ValidateDraft(Request(ExamKind.Assignment, starts: Now, ends: Now.AddMinutes(-1))), ErrorCodes.InvalidTimeWindow);
    }

    [Fact]
    public void ValidateDraft_RejectsUnknownKind() =>
        AssertRejected(() => ExamRules.ValidateDraft(Request((ExamKind)9)), ErrorCodes.InvalidRequest, 400);

    [Fact]
    public void PublishWindow_ForExam_StartsNowAndEndsAfterDuration()
    {
        var (start, end) = ExamRules.PublishWindow(ExamKind.Exam, 40, null, null, Now);

        Assert.Equal(Now, start);
        Assert.Equal(Now.AddMinutes(40), end);
    }

    [Fact]
    public void PublishWindow_ForAssignment_KeepsDraftDates_EvenIfStartIsPast()
    {
        var starts = Now.AddHours(-1);
        var ends = Now.AddDays(1);

        Assert.Equal((starts, ends), ExamRules.PublishWindow(ExamKind.Assignment, null, starts, ends, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void PublishWindow_ForAssignment_RejectsEndInPast(int endOffsetMinutes)
    {
        AssertRejected(
            () => ExamRules.PublishWindow(ExamKind.Assignment, null, Now.AddDays(-1), Now.AddMinutes(endOffsetMinutes), Now),
            ErrorCodes.InvalidTimeWindow);
    }

    [Fact]
    public void EarlyEndWindow_KeepsStart_WhenAlreadyStarted()
    {
        var starts = Now.AddMinutes(-10);

        Assert.Equal((starts, Now), ExamRules.EarlyEndWindow(starts, Now));
    }

    [Theory]
    [InlineData(0)]     // aynı anda yayınlanıp bitirilen sınav
    [InlineData(3600)]  // henüz başlamamış ödev
    public void EarlyEndWindow_MovesStartBeforeEnd_SoDatabaseCheckHolds(int startOffsetSeconds)
    {
        var (start, end) = ExamRules.EarlyEndWindow(Now.AddSeconds(startOffsetSeconds), Now);

        Assert.Equal(Now, end);
        Assert.True(start < end);
    }

    [Fact]
    public void ValidateQuestionList_AcceptsValidList() =>
        ExamRules.ValidateQuestionList([new(1, 1, 5), new(2, 2, null)]);

    [Fact]
    public void ValidateQuestionList_RejectsDuplicateQuestion() =>
        AssertRejected(() => ExamRules.ValidateQuestionList([new(1, 1, 5), new(1, 2, 5)]), ErrorCodes.DuplicateQuestion, 409);

    [Fact]
    public void ValidateQuestionList_RejectsDuplicateOrInvalidOrder()
    {
        AssertRejected(() => ExamRules.ValidateQuestionList([new(1, 1, 5), new(2, 1, 5)]), ErrorCodes.DuplicateOrderNo);
        AssertRejected(() => ExamRules.ValidateQuestionList([new(1, 0, 5)]), ErrorCodes.DuplicateOrderNo);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10000")]
    public void ValidateQuestionList_RejectsInvalidPoints(string points) =>
        AssertRejected(() => ExamRules.ValidateQuestionList([new(1, 1, decimal.Parse(points))]), ErrorCodes.InvalidPoints);

    [Fact]
    public void CopyTitle_AddsSuffix_AndStaysWithinColumnLimit()
    {
        Assert.Equal("Vize (kopya)", ExamRules.CopyTitle("Vize"));

        var longTitle = ExamRules.CopyTitle(new string('a', 200));
        Assert.Equal(200, longTitle.Length);
        Assert.EndsWith(" (kopya)", longTitle);
    }
}
