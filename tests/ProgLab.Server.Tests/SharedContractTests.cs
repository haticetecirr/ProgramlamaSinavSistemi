using System.Reflection;
using ProgLab.Shared;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Enum değerleri veritabanına yazılır; tasarımdaki değerlerden kaymamalıdır.</summary>
public sealed class SharedContractTests
{
    [Theory]
    [InlineData(UserRole.Instructor, 1)]
    [InlineData(UserRole.Student, 2)]
    [InlineData(QuestionType.MultipleChoice, 1)]
    [InlineData(QuestionType.Classic, 2)]
    [InlineData(QuestionType.Coding, 3)]
    [InlineData(ExamKind.Exam, 1)]
    [InlineData(ExamKind.Assignment, 2)]
    [InlineData(ExamStatus.Draft, 1)]
    [InlineData(ExamStatus.Published, 2)]
    [InlineData(ExamStatus.Completed, 3)]
    [InlineData(SubmissionStatus.InProgress, 1)]
    [InlineData(SubmissionStatus.Submitted, 2)]
    [InlineData(SubmissionStatus.AutoSubmitted, 3)]
    [InlineData(GradedBy.Auto, 1)]
    [InlineData(GradedBy.Manual, 2)]
    public void EnumValue_MatchesDesign(Enum value, int expected)
    {
        Assert.Equal(expected, Convert.ToInt32(value));
    }

    [Theory]
    [InlineData(typeof(UserRole))]
    [InlineData(typeof(QuestionType))]
    [InlineData(typeof(ExamKind))]
    [InlineData(typeof(ExamStatus))]
    [InlineData(typeof(SubmissionStatus))]
    [InlineData(typeof(GradedBy))]
    public void Enum_IsStoredAsSmallint(Type enumType)
    {
        Assert.Equal(typeof(short), Enum.GetUnderlyingType(enumType));
    }

    [Fact]
    public void ErrorCodes_AreUnique_AndUpperSnakeCase()
    {
        var codes = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(codes);
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, code => Assert.Matches("^[A-Z]+(_[A-Z]+)*$", code));
    }
}
