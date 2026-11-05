using ProgLab.Server.Hubs;
using ProgLab.Server.Services;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Lobideki durumların türetilmesi ve bağlantı takibi; saf birim testleri.</summary>
public sealed class StudentStateRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddMinutes(40);

    [Theory]
    [InlineData(-60, StudentExamState.Upcoming)]
    [InlineData(0, StudentExamState.Open)]
    [InlineData(2399, StudentExamState.Open)]
    [InlineData(2400, StudentExamState.Completed)] // Bitiş anında kapanır; zamanlayıcı henüz çalışmamış olsa da.
    public void PublishedExam_FollowsClock(int secondsFromStart, StudentExamState expected)
    {
        Assert.Equal(expected, ExamRules.StudentState(ExamStatus.Published, Start, End, null, Start.AddSeconds(secondsFromStart)));
    }

    [Fact]
    public void CompletedExam_IsCompleted_EvenInsideWindow()
    {
        Assert.Equal(StudentExamState.Completed, ExamRules.StudentState(ExamStatus.Completed, Start, End, null, Start.AddMinutes(1)));
    }

    [Theory]
    [InlineData(SubmissionStatus.Submitted)]
    [InlineData(SubmissionStatus.AutoSubmitted)]
    public void SubmittedStudent_SeesSubmitted_DuringAndAfterExam(SubmissionStatus submission)
    {
        Assert.Equal(StudentExamState.Submitted, ExamRules.StudentState(ExamStatus.Published, Start, End, submission, Start.AddMinutes(1)));
        Assert.Equal(StudentExamState.Submitted, ExamRules.StudentState(ExamStatus.Completed, Start, End, submission, End.AddHours(1)));
    }

    [Fact]
    public void InProgressSubmission_IsStillOpen()
    {
        Assert.Equal(StudentExamState.Open, ExamRules.StudentState(ExamStatus.Published, Start, End, SubmissionStatus.InProgress, Start.AddMinutes(1)));
    }

    [Theory]
    [InlineData(0, 2400)]
    [InlineData(2399.5, 1)] // Son yarım saniye de 1 görünür.
    [InlineData(2400, 0)]
    [InlineData(9999, 0)]   // Eksiye düşmez.
    public void RemainingSeconds_RoundsUp_AndNeverGoesNegative(double elapsed, int expected)
    {
        Assert.Equal(expected, ExamRules.RemainingSeconds(End, Start.AddSeconds(elapsed)));
    }

    [Fact]
    public void Tracker_ReportsFirstAndLastConnectionPerStudent()
    {
        var tracker = new ConnectionTracker();
        var ali = new StudentConnectionDto(1, "Ali", "10.0.0.1");
        var veli = new StudentConnectionDto(2, "Veli", "10.0.0.2");

        Assert.True(tracker.Add("c1", ali));
        Assert.False(tracker.Add("c2", ali with { Ip = "10.0.0.9" }));
        Assert.True(tracker.Add("c3", veli));
        Assert.Equal(2, tracker.GetStudents().Count);

        Assert.Null(tracker.Remove("c1"));                // Ali'nin bir bağlantısı daha var.
        Assert.Equal(2, tracker.GetStudents().Count);
        Assert.Equal(1, tracker.Remove("c2")!.StudentId); // Son bağlantısı kapandı.
        Assert.Null(tracker.Remove("bilinmeyen"));
        Assert.Equal(["Veli"], tracker.GetStudents().Select(s => s.FullName));
    }
}
