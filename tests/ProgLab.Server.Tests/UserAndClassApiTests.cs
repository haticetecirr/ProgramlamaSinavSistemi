using ProgLab.Client.Core;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Öğrenci ve şube yönetimi uç noktaları.</summary>
public sealed class UserAndClassApiTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    [Fact]
    public async Task ListStudents_FiltersByRoleSearchClass_AndPages()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var classId = (await api.GetClassesAsync()).First(c => c.Name.Contains("A şubesi")).Id;

        var page = await api.GetStudentsAsync(pageSize: 10);
        var search = await api.GetStudentsAsync(search: "Öğrenci 12");
        var byNumber = await api.GetStudentsAsync(search: "2026900030");
        var byClass = await api.GetStudentsAsync(classId: classId);

        Assert.True(page.Total >= 30);
        Assert.Equal(10, page.Items.Count);
        Assert.All(page.Items, u => Assert.Equal(UserRole.Student, u.Role));
        Assert.Contains(search.Items, u => u.FullName == "Demo Öğrenci 12");
        Assert.Equal("2026900030", Assert.Single(byNumber.Items).Username);
        Assert.Contains(byClass.Items, u => u.Username == "2026900001");
    }

    [Fact]
    public async Task CreatedStudent_HasNoPassword_AndCanCompleteFirstLogin()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);

        var created = await api.CreateStudentAsync("2026000001", "Yeni Öğrenci", "yeni1@ogr.demo.local");

        Assert.Equal(UserRole.Student, created.Role);
        Assert.Null(created.VerifiedAt);
        var student = new ApiClient(fixture.Server.CreateClient());
        var login = await student.FirstLoginAsync("2026000001", "yeni1@ogr.demo.local", "Parola-12345");
        Assert.Equal("Yeni Öğrenci", login.FullName);
    }

    [Fact]
    public async Task DuplicateStudentNumber_OrEmail_Returns409()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);

        var number = await Assert.ThrowsAsync<ApiException>(() => api.CreateStudentAsync("2026900001", "Kopya", "kopya1@ogr.demo.local"));
        var email = await Assert.ThrowsAsync<ApiException>(() => api.CreateStudentAsync("2026000002", "Kopya", "2026900001@OGR.DEMO.LOCAL"));

        Assert.Equal(ErrorCodes.UsernameExists, number.Code);
        Assert.Equal(409, number.StatusCode);
        Assert.Equal(ErrorCodes.EmailExists, email.Code);
    }

    [Fact]
    public async Task InvalidEmail_Returns400()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.CreateStudentAsync("2026000003", "Ad", "e-posta-degil"));

        Assert.Equal(ErrorCodes.InvalidRequest, error.Code);
    }

    [Fact]
    public async Task UpdateStudent_ChangesNameEmailAndActivity()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var created = await api.CreateStudentAsync("2026000004", "Eski Ad", "eski4@ogr.demo.local");

        var updated = await api.UpdateStudentAsync(created.Id, "Yeni Ad", "yeni4@ogr.demo.local", isActive: false);

        Assert.Equal("Yeni Ad", updated.FullName);
        Assert.Equal("yeni4@ogr.demo.local", updated.Email);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task InstructorAccount_CannotBeChangedThroughUsersEndpoints()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var instructorId = (await api.GetMeAsync()).Id;

        var update = await Assert.ThrowsAsync<ApiException>(() => api.UpdateStudentAsync(instructorId, "x", "x@y.z", false));
        var reset = await Assert.ThrowsAsync<ApiException>(() => api.ResetFirstLoginAsync(instructorId));

        Assert.Equal(ErrorCodes.UserNotFound, update.Code);
        Assert.Equal(ErrorCodes.UserNotFound, reset.Code);
    }

    [Fact]
    public async Task ClassCrud_AndDuplicateName()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);

        var created = await api.CreateClassAsync("B şubesi", "2026-2027 Güz");
        Assert.Equal(0, created.MemberCount);
        Assert.Contains(await api.GetClassesAsync(), c => c.Id == created.Id);

        var duplicate = await Assert.ThrowsAsync<ApiException>(() => api.CreateClassAsync("B şubesi", "2026-2027 Güz"));
        Assert.Equal(ErrorCodes.ClassNameExists, duplicate.Code);

        // Aynı ad başka dönemde serbesttir.
        await api.CreateClassAsync("B şubesi", "2026-2027 Bahar");

        await api.DeleteClassAsync(created.Id);
        var missing = await Assert.ThrowsAsync<ApiException>(() => api.GetClassAsync(created.Id));
        Assert.Equal(ErrorCodes.ClassNotFound, missing.Code);
        Assert.Equal(404, missing.StatusCode);
    }

    [Fact]
    public async Task ReplaceMembers_ReplacesTheWholeList_AndKeepsFirstAddedDate()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var schoolClass = await api.CreateClassAsync("Üyelik şubesi", "Test");
        var students = (await api.GetStudentsAsync(pageSize: 3)).Items.Select(s => s.Id).ToList();

        await api.ReplaceClassMembersAsync(schoolClass.Id, students);
        var firstAdded = await AddedAtAsync(schoolClass.Id, students[0]);

        var detail = await api.ReplaceClassMembersAsync(schoolClass.Id, [students[0], students[2]]);

        Assert.Equal([students[0], students[2]], detail.Members.Select(m => m.Id).Order().ToArray());
        Assert.Equal(firstAdded, await AddedAtAsync(schoolClass.Id, students[0]));
        Assert.Equal(2, (await api.GetClassesAsync()).Single(c => c.Id == schoolClass.Id).MemberCount);
    }

    [Fact]
    public async Task ReplaceMembers_WithInstructorOrUnknownId_Returns422()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var schoolClass = await api.CreateClassAsync("Geçersiz üye şubesi", "Test");

        var instructorId = (await api.GetMeAsync()).Id;

        var instructor = await Assert.ThrowsAsync<ApiException>(() => api.ReplaceClassMembersAsync(schoolClass.Id, [instructorId]));
        var unknown = await Assert.ThrowsAsync<ApiException>(() => api.ReplaceClassMembersAsync(schoolClass.Id, [999_999]));

        Assert.Equal(ErrorCodes.InvalidStudent, instructor.Code);
        Assert.Equal(422, instructor.StatusCode);
        Assert.Equal(ErrorCodes.InvalidStudent, unknown.Code);
    }

    [Fact]
    public async Task ClassWithExam_CannotBeDeleted()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var schoolClass = await api.CreateClassAsync("Sınavlı şube", "Test");
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            db.Exams.Add(new Exam
            {
                Title = "Sınav",
                Kind = ExamKind.Exam,
                ClassId = schoolClass.Id,
                DurationMinutes = 10,
                CreatedBy = (await api.GetMeAsync()).Id,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<ApiException>(() => api.DeleteClassAsync(schoolClass.Id));

        Assert.Equal(ErrorCodes.ClassHasExams, error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    private async Task<DateTimeOffset> AddedAtAsync(int classId, int studentId)
    {
        await using var db = fixture.CreateDbContext();
        return db.ClassMembers.Single(m => m.ClassId == classId && m.StudentId == studentId).AddedAt;
    }
}
