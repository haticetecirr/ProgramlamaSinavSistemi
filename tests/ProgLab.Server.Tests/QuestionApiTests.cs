using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ProgLab.Client.Core;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Soru bankası uç noktaları: VT-10, VT-11, VT-13, VT-14 ve kurallar.</summary>
public sealed class QuestionApiTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly QuestionOptionInput[] FourOptions =
    [
        new("A seçeneği", false),
        new("B seçeneği", true),
        new("C seçeneği", false),
        new("D seçeneği", false)
    ];

    private async Task<int> FirstCategoryIdAsync(ApiClient api) => (await api.GetCategoriesAsync())[0].Id;

    private async Task<(ApiClient Api, SaveQuestionRequest Request)> McqAsync(string title)
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        return (api, new SaveQuestionRequest(await FirstCategoryIdAsync(api), QuestionType.MultipleChoice, title, "Soru metni", 10, FourOptions, null));
    }

    private async Task<(ApiClient Api, SaveQuestionRequest Request)> ClassicAsync(string title)
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        return (api, new SaveQuestionRequest(await FirstCategoryIdAsync(api), QuestionType.Classic, title, "Klasik metin", 5, null, ["  Yığın. ", "stack"]));
    }

    [Fact]
    public async Task VT10_McqWithFourOptions_IsSaved_AndListed()
    {
        var (api, request) = await McqAsync("VT10 çoktan seçmeli");

        var created = await api.CreateQuestionAsync(request);

        Assert.Equal(1, created.VersionNo);
        Assert.True(created.IsCurrent);
        Assert.False(created.IsLocked);
        Assert.Equal(["A seçeneği", "B seçeneği", "C seçeneği", "D seçeneği"], created.Options.Select(o => o.Text));
        Assert.Equal([(short)1, (short)2, (short)3, (short)4], created.Options.Select(o => o.OrderNo));
        Assert.Equal("B seçeneği", created.Options.Single(o => o.IsCorrect).Text);

        var listed = await api.GetQuestionsAsync(search: "VT10 çoktan");
        var row = Assert.Single(listed.Items);
        Assert.Equal(created.Id, row.Id);
        Assert.Equal(QuestionType.MultipleChoice, row.Type);
        Assert.Equal(10m, row.DefaultPoints);
    }

    [Fact]
    public async Task ClassicQuestion_StoresOriginalAndNormalizedAnswers()
    {
        var (api, request) = await ClassicAsync("Klasik kayıt");

        var created = await api.CreateQuestionAsync(request);

        Assert.Equal(["Yığın.", "stack"], created.AnswerKeys.Select(k => k.AcceptedText));
        Assert.Empty(created.Options);

        await using var db = fixture.CreateDbContext();
        var keys = await db.ClassicAnswerKeys.Where(k => k.QuestionId == created.Id).OrderBy(k => k.Id).ToListAsync();
        Assert.Equal(["yığın", "stack"], keys.Select(k => k.NormalizedText));
    }

    [Fact]
    public async Task VT11_McqWithoutCorrectOption_Returns422_AndIsNotSaved()
    {
        var (api, request) = await McqAsync("VT11 geçersiz");

        var error = await Assert.ThrowsAsync<ApiException>(() => api.CreateQuestionAsync(request with
        {
            Options = FourOptions.Select(o => o with { IsCorrect = false }).ToArray()
        }));

        Assert.Equal(ErrorCodes.McqNoCorrectOption, error.Code);
        Assert.Equal(422, error.StatusCode);
        Assert.Equal(0, (await api.GetQuestionsAsync(search: "VT11 geçersiz")).Total);
    }

    [Fact]
    public async Task UnknownCategory_Returns404()
    {
        var (api, request) = await McqAsync("Kategorisiz");

        var error = await Assert.ThrowsAsync<ApiException>(() => api.CreateQuestionAsync(request with { CategoryId = 999_999 }));

        Assert.Equal(ErrorCodes.CategoryNotFound, error.Code);
        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task VT13_UnlockedQuestion_IsUpdatedInPlace_AndOptionsAreReplaced()
    {
        var (api, request) = await McqAsync("VT13 eski başlık");
        var created = await api.CreateQuestionAsync(request);

        var response = await api.UpdateQuestionAsync(created.Id, request with
        {
            Title = "VT13 yeni başlık",
            DefaultPoints = 20,
            Options = [new("Yeni A", true), new("Yeni B", false), new("Yeni C", false)]
        });

        Assert.Null(response.NewVersionId);
        Assert.Equal(created.Id, response.Question.Id);
        Assert.Equal(1, response.Question.VersionNo);

        var stored = await api.GetQuestionAsync(created.Id);
        Assert.Equal("VT13 yeni başlık", stored.Title);
        Assert.Equal(20m, stored.DefaultPoints);
        Assert.Equal(["Yeni A", "Yeni B", "Yeni C"], stored.Options.Select(o => o.Text));
        Assert.Equal("Yeni A", stored.Options.Single(o => o.IsCorrect).Text);
    }

    [Fact]
    public async Task UnlockedQuestion_CanChangeType()
    {
        var (api, request) = await McqAsync("Tip değişimi");
        var created = await api.CreateQuestionAsync(request);

        var response = await api.UpdateQuestionAsync(created.Id, request with
        {
            Type = QuestionType.Classic,
            Options = null,
            AcceptedAnswers = ["cevap"]
        });

        Assert.Equal(QuestionType.Classic, response.Question.Type);
        Assert.Empty((await api.GetQuestionAsync(created.Id)).Options);
        Assert.Single((await api.GetQuestionAsync(created.Id)).AnswerKeys);
    }

    /// <summary>
    /// VT-14: Yayınlama Aşama 5'te yazılacağı için burada kilit ve sınav satırı doğrudan veritabanında hazırlanır.
    /// Senaryonun yayınlama üzerinden tam hâli Aşama 5'te çalıştırılacak.
    /// </summary>
    [Fact]
    public async Task VT14_LockedQuestion_GetsNewVersion_AndExamKeepsOldContent()
    {
        var (api, request) = await McqAsync("VT14 kilitli");
        var original = await api.CreateQuestionAsync(request);
        var examQuestionId = await PutIntoExamAndLockAsync(original.Id, points: 7);

        var response = await api.UpdateQuestionAsync(original.Id, request with
        {
            Title = "VT14 yeni sürüm",
            Text = "Değişmiş metin",
            DefaultPoints = 99,
            Options = [new("Değişmiş A", false), new("Değişmiş B", true)]
        });

        // Yeni sürüm oluşur.
        Assert.NotNull(response.NewVersionId);
        Assert.NotEqual(original.Id, response.NewVersionId);
        Assert.Equal(2, response.Question.VersionNo);
        Assert.Equal(original.Id, response.Question.PreviousVersionId);
        Assert.True(response.Question.IsCurrent);
        Assert.False(response.Question.IsLocked);

        // Eski sürüm içeriği birebir korunur ve bankadan gizlenir.
        var old = await api.GetQuestionAsync(original.Id);
        Assert.False(old.IsCurrent);
        Assert.True(old.IsLocked);
        Assert.Equal("VT14 kilitli", old.Title);
        Assert.Equal("Soru metni", old.Text);
        Assert.Equal(10m, old.DefaultPoints);
        Assert.Equal(original.Options.Select(o => (o.Id, o.Text, o.IsCorrect)), old.Options.Select(o => (o.Id, o.Text, o.IsCorrect)));

        // Sınav eski sürüme ve kendi puanına bakmaya devam eder.
        await using (var db = fixture.CreateDbContext())
        {
            var examQuestion = await db.ExamQuestions.SingleAsync(e => e.Id == examQuestionId);
            Assert.Equal(original.Id, examQuestion.QuestionId);
            Assert.Equal(7m, examQuestion.Points);
        }

        // Bankada yalnızca yeni sürüm görünür.
        Assert.Empty((await api.GetQuestionsAsync(search: "VT14 kilitli")).Items);
        Assert.Equal(response.NewVersionId, Assert.Single((await api.GetQuestionsAsync(search: "VT14 yeni sürüm")).Items).Id);

        // Yeni sürüm kilitli olmadığı için yerinde düzenlenir, zincir uzamaz.
        var again = await api.UpdateQuestionAsync(response.NewVersionId!.Value, request with { Title = "VT14 yeni sürüm 2" });
        Assert.Null(again.NewVersionId);
        Assert.Equal(2, again.Question.VersionNo);

        // Eski sürüm doğrudan düzenlenemez.
        var error = await Assert.ThrowsAsync<ApiException>(() => api.UpdateQuestionAsync(original.Id, request));
        Assert.Equal(ErrorCodes.QuestionNotFound, error.Code);
    }

    [Fact]
    public async Task ClassicLockedQuestion_KeepsOldAnswerKeys_InOldVersion()
    {
        var (api, request) = await ClassicAsync("Kilitli klasik");
        var original = await api.CreateQuestionAsync(request);
        await PutIntoExamAndLockAsync(original.Id, points: 5);

        var response = await api.UpdateQuestionAsync(original.Id, request with { AcceptedAnswers = ["kuyruk"] });

        Assert.Equal(["Yığın.", "stack"], (await api.GetQuestionAsync(original.Id)).AnswerKeys.Select(k => k.AcceptedText));
        Assert.Equal("kuyruk", Assert.Single(response.Question.AnswerKeys).AcceptedText);
    }

    [Fact]
    public async Task Delete_IsSoft_HidesFromBank_AndKeepsContent()
    {
        var (api, request) = await McqAsync("Silinecek soru");
        var created = await api.CreateQuestionAsync(request);

        await api.DeleteQuestionAsync(created.Id);

        Assert.Equal(0, (await api.GetQuestionsAsync(search: "Silinecek soru")).Total);
        var stored = await api.GetQuestionAsync(created.Id);
        Assert.True(stored.IsDeleted);
        Assert.Equal(4, stored.Options.Count);

        Assert.Equal(ErrorCodes.QuestionNotFound, (await Assert.ThrowsAsync<ApiException>(() => api.DeleteQuestionAsync(created.Id))).Code);
        Assert.Equal(ErrorCodes.QuestionNotFound, (await Assert.ThrowsAsync<ApiException>(() => api.UpdateQuestionAsync(created.Id, request))).Code);
    }

    [Fact]
    public async Task LockedQuestion_CanBeSoftDeleted_AndExamRowRemains()
    {
        var (api, request) = await McqAsync("Kilitli silinecek");
        var created = await api.CreateQuestionAsync(request);
        var examQuestionId = await PutIntoExamAndLockAsync(created.Id, points: 3);

        await api.DeleteQuestionAsync(created.Id);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(created.Id, (await db.ExamQuestions.SingleAsync(e => e.Id == examQuestionId)).QuestionId);
        Assert.Equal(4, await db.QuestionOptions.CountAsync(o => o.QuestionId == created.Id));
    }

    [Fact]
    public async Task List_FiltersByCategoryTypeAndSearch_AndPages()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var category = await api.CreateCategoryAsync("Filtre kategorisi", null);
        foreach (var i in Enumerable.Range(1, 3))
        {
            await api.CreateQuestionAsync(new SaveQuestionRequest(category.Id, QuestionType.MultipleChoice, $"Filtre MCQ {i}", "Metin", 1, FourOptions, null));
        }

        await api.CreateQuestionAsync(new SaveQuestionRequest(category.Id, QuestionType.Classic, "Filtre klasik", "Özel aranan metin", 1, null, ["x"]));

        Assert.Equal(4, (await api.GetQuestionsAsync(categoryId: category.Id)).Total);
        Assert.Equal(3, (await api.GetQuestionsAsync(categoryId: category.Id, type: QuestionType.MultipleChoice)).Total);
        Assert.Equal("Filtre klasik", Assert.Single((await api.GetQuestionsAsync(type: QuestionType.Classic, search: "özel ARANAN")).Items).Title);

        var page2 = await api.GetQuestionsAsync(categoryId: category.Id, page: 2, pageSize: 3);
        Assert.Equal(4, page2.Total);
        Assert.Single(page2.Items);
    }

    [Fact]
    public async Task SeedQuestions_AreListed()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);

        Assert.True((await api.GetQuestionsAsync(type: QuestionType.MultipleChoice, search: "printf")).Total >= 1);
        Assert.True((await api.GetQuestionsAsync(type: QuestionType.Classic, search: "LIFO")).Total >= 1);
    }

    [Theory]
    [InlineData("/api/questions")]
    [InlineData("/api/categories")]
    public async Task Student_CannotReachQuestionBank(string path)
    {
        // Cevap anahtarı içeren uç noktalar öğrenciye kapalıdır.
        var student = path.Contains("questions") ? 20 : 21;
        var login = await new ApiClient(fixture.Server.CreateClient()).FirstLoginAsync(TestAccounts.StudentNumber(student), TestAccounts.StudentEmail(student), TestAccounts.StudentPassword);
        var client = fixture.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, new { name = "x", title = "x", text = "x" })).StatusCode);
    }

    private async Task<int> PutIntoExamAndLockAsync(int questionId, decimal points)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var exam = new Exam
        {
            Title = "Kilit sınavı",
            Kind = ExamKind.Exam,
            ClassId = await db.Classes.Select(c => c.Id).FirstAsync(),
            DurationMinutes = 10,
            CreatedBy = await db.Users.Where(u => u.Role == UserRole.Instructor).Select(u => u.Id).FirstAsync(),
            CreatedAt = now,
            UpdatedAt = now,
            Questions = [new ExamQuestion { QuestionId = questionId, OrderNo = 1, Points = points }]
        };
        db.Exams.Add(exam);
        var question = await db.Questions.SingleAsync(q => q.Id == questionId);
        question.IsLocked = true;
        await db.SaveChangesAsync();

        return exam.Questions[0].Id;
    }
}

/// <summary>Kategori kuralları: VT-12.</summary>
public sealed class CategoryApiTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    [Fact]
    public async Task VT12_MoveQuestionToNewCategory_ThenNonEmptyCategoryCannotBeDeleted()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var source = await api.CreateCategoryAsync("VT12 kaynak", null);
        var target = await api.CreateCategoryAsync("VT12 hedef", null);
        var request = new SaveQuestionRequest(source.Id, QuestionType.Classic, "VT12 sorusu", "Metin", 1, null, ["cevap"]);
        var question = await api.CreateQuestionAsync(request);

        await api.UpdateQuestionAsync(question.Id, request with { CategoryId = target.Id });

        Assert.Equal(target.Id, (await api.GetQuestionAsync(question.Id)).CategoryId);
        Assert.Equal(0, (await api.GetQuestionsAsync(categoryId: source.Id)).Total);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.DeleteCategoryAsync(target.Id));
        Assert.Equal(ErrorCodes.CategoryNotEmpty, error.Code);
        Assert.Equal(409, error.StatusCode);

        // Boşalan kaynak silinebilir.
        await api.DeleteCategoryAsync(source.Id);
        Assert.DoesNotContain(await api.GetCategoriesAsync(), c => c.Id == source.Id);
    }

    [Fact]
    public async Task CategoryWithChild_CannotBeDeleted()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var parent = await api.CreateCategoryAsync("Üst", null);
        await api.CreateCategoryAsync("Alt", parent.Id);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.DeleteCategoryAsync(parent.Id));

        Assert.Equal(ErrorCodes.CategoryNotEmpty, error.Code);
    }

    [Fact]
    public async Task CategoryWithOnlySoftDeletedQuestion_StillCannotBeDeleted()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var category = await api.CreateCategoryAsync("Yumuşak silinmiş", null);
        var question = await api.CreateQuestionAsync(new SaveQuestionRequest(category.Id, QuestionType.Classic, "S", "M", 1, null, ["c"]));
        await api.DeleteQuestionAsync(question.Id);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.DeleteCategoryAsync(category.Id));

        Assert.Equal(ErrorCodes.CategoryNotEmpty, error.Code);
    }

    [Fact]
    public async Task Tree_ContainsNestedCategories()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var parent = await api.CreateCategoryAsync("Ağaç üst", null);
        var child = await api.CreateCategoryAsync("Ağaç alt", parent.Id);
        var grandchild = await api.CreateCategoryAsync("Ağaç en alt", child.Id);

        var tree = await api.GetCategoriesAsync();

        var node = tree.Single(c => c.Id == parent.Id);
        Assert.Equal(child.Id, Assert.Single(node.Children).Id);
        Assert.Equal(grandchild.Id, Assert.Single(node.Children[0].Children).Id);
        Assert.Contains(tree, c => c.Name == "C Temelleri");
    }

    [Fact]
    public async Task SameName_AtSameLevel_IsRejected_ButAllowedUnderAnotherParent()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        await api.CreateCategoryAsync("Tekrar ad", null);

        var root = await Assert.ThrowsAsync<ApiException>(() => api.CreateCategoryAsync("Tekrar ad", null));
        Assert.Equal(ErrorCodes.CategoryNameExists, root.Code);
        Assert.Equal(409, root.StatusCode);

        var parent = await api.CreateCategoryAsync("Başka üst", null);
        await api.CreateCategoryAsync("Tekrar ad", parent.Id);
    }

    [Fact]
    public async Task Rename_AndMove_Work()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var category = await api.CreateCategoryAsync("Eski ad", null);
        var parent = await api.CreateCategoryAsync("Yeni üst", null);

        var updated = await api.UpdateCategoryAsync(category.Id, "Yeni ad", parent.Id);

        Assert.Equal("Yeni ad", updated.Name);
        Assert.Equal(parent.Id, updated.ParentId);
    }

    [Fact]
    public async Task MovingUnderItselfOrDescendant_IsRejected()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var top = await api.CreateCategoryAsync("Döngü üst", null);
        var child = await api.CreateCategoryAsync("Döngü alt", top.Id);

        var self = await Assert.ThrowsAsync<ApiException>(() => api.UpdateCategoryAsync(top.Id, "Döngü üst", top.Id));
        var descendant = await Assert.ThrowsAsync<ApiException>(() => api.UpdateCategoryAsync(top.Id, "Döngü üst", child.Id));

        Assert.Equal(ErrorCodes.CategoryCycle, self.Code);
        Assert.Equal(ErrorCodes.CategoryCycle, descendant.Code);
        Assert.Equal(422, descendant.StatusCode);
    }

    [Fact]
    public async Task UnknownParent_OrCategory_Returns404()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);

        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => api.CreateCategoryAsync("Yetim", 999_999))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => api.DeleteCategoryAsync(999_999))).StatusCode);
    }
}
