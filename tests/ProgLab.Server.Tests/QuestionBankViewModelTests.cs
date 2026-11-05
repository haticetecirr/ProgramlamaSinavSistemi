using ProgLab.InstructorApp.ViewModels;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ProgLab.Server.Tests;

/// <summary>Soru bankası ekranlarının arkasındaki akışlar, gerçek API'ye karşı (pencere açmadan).</summary>
public sealed class QuestionBankViewModelTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private async Task<MainWindowViewModel> LoggedInMainAsync()
    {
        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var main = new MainWindowViewModel(api);
        main.ShowQuestionBankCommand.Execute(null);
        await Assert.IsType<QuestionBankViewModel>(main.CurrentPage).LoadCommand.ExecutionTask!;
        return main;
    }

    private static async Task<QuestionEditorViewModel> OpenNewEditorAsync(MainWindowViewModel main)
    {
        Assert.IsType<QuestionBankViewModel>(main.CurrentPage).NewQuestionCommand.Execute(null);
        var editor = Assert.IsType<QuestionEditorViewModel>(main.CurrentPage);
        await editor.LoadCommand.ExecutionTask!;
        return editor;
    }

    private static async Task<QuestionBankViewModel> BackOnBankAsync(MainWindowViewModel main)
    {
        var bank = Assert.IsType<QuestionBankViewModel>(main.CurrentPage);
        await bank.LoadCommand.ExecutionTask!;
        return bank;
    }

    [Fact]
    public async Task Bank_LoadsSeedCategoriesAndQuestions()
    {
        var bank = Assert.IsType<QuestionBankViewModel>((await LoggedInMainAsync()).CurrentPage);

        Assert.Null(bank.ErrorMessage);
        Assert.Contains(bank.Categories, c => c.Name == "C Temelleri");
        Assert.Contains(bank.Questions, q => q.Title == "printf başlık dosyası");
        Assert.Contains("Çoktan seçmeli", bank.Questions.First(q => q.Title == "printf başlık dosyası").Details);
        Assert.Contains("Klasik", bank.Questions.First(q => q.Title == "LIFO veri yapısı").Details);
    }

    [Fact]
    public async Task NewMcq_ValidatesAndSaves_ThenAppearsInBank()
    {
        var main = await LoggedInMainAsync();
        var editor = await OpenNewEditorAsync(main);

        Assert.Equal(2, editor.Options.Count);
        Assert.NotNull(editor.SelectedCategory);

        // Eksik alanlar.
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Başlık, soru metni ve kategori zorunludur.", editor.ErrorMessage);

        editor.Title = "Arayüz MCQ";
        editor.Text = "Metin";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.StartsWith("Seçenek metinleri boş olamaz", editor.ErrorMessage);

        // Doğru seçenek işaretlenmemiş: sunucu reddeder, Türkçe mesaj görünür, form kapanmaz.
        editor.Options[0].Text = "Bir";
        editor.Options[1].Text = "İki";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Doğru seçeneği işaretleyin.", editor.ErrorMessage);
        Assert.Same(editor, main.CurrentPage);

        editor.Options[1].IsCorrect = true;
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Null(editor.ErrorMessage);

        var bank = await BackOnBankAsync(main);
        Assert.Equal("\"Arayüz MCQ\" eklendi.", bank.StatusMessage);
        Assert.Contains(bank.Questions, q => q.Title == "Arayüz MCQ");
    }

    [Fact]
    public async Task Option_AddAndRemove_RespectsLimit()
    {
        var editor = await OpenNewEditorAsync(await LoggedInMainAsync());

        foreach (var _ in Enumerable.Range(1, 4))
        {
            editor.AddOptionCommand.Execute(null);
        }

        Assert.Equal(6, editor.Options.Count);
        Assert.Null(editor.ErrorMessage);

        editor.AddOptionCommand.Execute(null);
        Assert.Equal(6, editor.Options.Count);
        Assert.Contains("en fazla 6", editor.ErrorMessage);

        editor.Options[0].RemoveCommand.Execute(null);
        Assert.Equal(5, editor.Options.Count);
    }

    [Fact]
    public async Task NewClassic_SavesAlternatives_AndShowsDuplicateError()
    {
        var main = await LoggedInMainAsync();
        var editor = await OpenNewEditorAsync(main);
        editor.IsClassic = true;
        Assert.False(editor.IsMultipleChoice);
        editor.Title = "Arayüz klasik";
        editor.Text = "Metin";
        editor.AnswerKeys[0].Text = "yığın";
        editor.AddAnswerKeyCommand.Execute(null);
        editor.AnswerKeys[1].Text = "YIĞIN.";

        await editor.SaveCommand.ExecuteAsync(null);
        Assert.StartsWith("Aynı kabul edilen cevap", editor.ErrorMessage);

        editor.AnswerKeys[1].Text = "stack";
        await editor.SaveCommand.ExecuteAsync(null);

        var bank = await BackOnBankAsync(main);
        var row = bank.Questions.Single(q => q.Title == "Arayüz klasik");
        Assert.Equal(QuestionType.Classic, row.Question.Type);
    }

    [Fact]
    public async Task Edit_LoadsExistingValues_AndSavesInPlace()
    {
        var main = await LoggedInMainAsync();
        var bank = Assert.IsType<QuestionBankViewModel>(main.CurrentPage);

        // Seçim yokken uyarı verir.
        bank.EditQuestionCommand.Execute(null);
        Assert.Equal("Önce listeden bir soru seçin.", bank.ErrorMessage);

        bank.SelectedQuestion = bank.Questions.First(q => q.Title == "Dizinin ilk indisi");
        bank.EditQuestionCommand.Execute(null);
        var editor = Assert.IsType<QuestionEditorViewModel>(main.CurrentPage);
        await editor.LoadCommand.ExecutionTask!;

        Assert.Equal("Dizinin ilk indisi", editor.Title);
        Assert.True(editor.IsMultipleChoice);
        Assert.False(editor.IsLocked);
        Assert.Equal(4, editor.Options.Count);
        Assert.Equal("0", editor.Options.Single(o => o.IsCorrect).Text);
        Assert.Equal("Diziler ve Veri Yapıları", editor.SelectedCategory!.Label);

        editor.Title = "Dizinin ilk indisi (düzenlendi)";
        await editor.SaveCommand.ExecuteAsync(null);

        var after = await BackOnBankAsync(main);
        Assert.Contains("kaydedildi", after.StatusMessage);
        Assert.Contains(after.Questions, q => q.Title == "Dizinin ilk indisi (düzenlendi)");
        Assert.DoesNotContain(after.Questions, q => q.Title == "Dizinin ilk indisi");
    }

    [Fact]
    public async Task LockedQuestion_ShowsWarning_AndSavingCreatesNewVersion()
    {
        var main = await LoggedInMainAsync();
        var bank = Assert.IsType<QuestionBankViewModel>(main.CurrentPage);
        var row = bank.Questions.First(q => q.Title == "Başlangıç fonksiyonu");
        await using (var db = fixture.CreateDbContext())
        {
            await db.Questions.Where(q => q.Id == row.Question.Id).ExecuteUpdateAsync(s => s.SetProperty(q => q.IsLocked, true));
        }

        await bank.LoadCommand.ExecuteAsync(null);
        bank.SelectedQuestion = bank.Questions.First(q => q.Title == "Başlangıç fonksiyonu");
        Assert.Contains("Kilitli", bank.SelectedQuestion.Details);
        bank.EditQuestionCommand.Execute(null);
        var editor = Assert.IsType<QuestionEditorViewModel>(main.CurrentPage);
        await editor.LoadCommand.ExecutionTask!;

        Assert.True(editor.IsLocked);
        editor.Text = "Yeni sürüm metni";
        await editor.SaveCommand.ExecuteAsync(null);

        var after = await BackOnBankAsync(main);
        Assert.StartsWith("Yeni sürüm oluşturuldu", after.StatusMessage);
        var updated = after.Questions.Single(q => q.Title == "Başlangıç fonksiyonu");
        Assert.Equal(2, updated.Question.VersionNo);
    }

    [Fact]
    public async Task Delete_AsksForConfirmation_ThenRemovesFromList()
    {
        var main = await LoggedInMainAsync();
        var bank = Assert.IsType<QuestionBankViewModel>(main.CurrentPage);
        bank.SelectedQuestion = bank.Questions.First(q => q.Title == "Metin uzunluğu");

        bank.AskDeleteQuestionCommand.Execute(null);
        Assert.True(bank.IsConfirmingDelete);
        bank.CancelDeleteQuestionCommand.Execute(null);
        Assert.False(bank.IsConfirmingDelete);
        Assert.Contains(bank.Questions, q => q.Title == "Metin uzunluğu");

        bank.AskDeleteQuestionCommand.Execute(null);
        await bank.ConfirmDeleteQuestionCommand.ExecuteAsync(null);

        Assert.DoesNotContain(bank.Questions, q => q.Title == "Metin uzunluğu");
        Assert.Contains("silindi", bank.StatusMessage);
    }

    [Fact]
    public async Task Filters_ByCategoryTypeAndSearch()
    {
        var bank = Assert.IsType<QuestionBankViewModel>((await LoggedInMainAsync()).CurrentPage);

        bank.SearchText = "ondalıklı";
        await bank.SearchCommand.ExecuteAsync(null);
        Assert.Equal("Ondalıklı sayı tipi", Assert.Single(bank.Questions).Title);

        bank.SearchText = "";
        bank.SelectedTypeFilter = bank.TypeFilters.Single(f => f.Value == QuestionType.Classic);
        await bank.SearchCommand.ExecuteAsync(null);
        Assert.All(bank.Questions, q => Assert.Equal(QuestionType.Classic, q.Question.Type));

        bank.SelectedTypeFilter = bank.TypeFilters[0];
        bank.SelectedCategory = bank.Categories.Single(c => c.Name == "Kontrol Yapıları");
        await bank.SearchCommand.ExecuteAsync(null);
        Assert.All(bank.Questions, q => Assert.Equal("Kontrol Yapıları", q.Question.CategoryName));

        await bank.ClearCategoryFilterCommand.ExecuteAsync(null);
        Assert.Null(bank.SelectedCategory);
        Assert.Contains(bank.Questions, q => q.Question.CategoryName == "C Temelleri");
    }

    [Fact]
    public async Task CategoryDialog_AddsChild_EditsAndDeletes_WithTurkishErrors()
    {
        var bank = Assert.IsType<QuestionBankViewModel>((await LoggedInMainAsync()).CurrentPage);
        bank.SelectedCategory = bank.Categories.Single(c => c.Name == "C Temelleri");

        // Üst kategori olarak seçili kategori önerilir.
        bank.AddCategoryCommand.Execute(null);
        var dialog = Assert.IsType<CategoryDialogViewModel>(bank.CategoryDialog);
        Assert.Equal("C Temelleri", dialog.SelectedParent!.Label);
        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Kategori adını girin.", dialog.ErrorMessage);

        dialog.Name = "İşaretçiler";
        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.False(bank.IsCategoryDialogOpen);
        await bank.CategoryDialogSaveTask;
        var parent = bank.Categories.Single(c => c.Name == "C Temelleri");
        var child = Assert.Single(parent.Children, c => c.Name == "İşaretçiler");
        Assert.Contains("eklendi", bank.StatusMessage);

        // Aynı adla ikinci kez: pencere açık kalır, Türkçe hata görünür.
        bank.AddCategoryCommand.Execute(null);
        var duplicate = bank.CategoryDialog!;
        duplicate.Name = "İşaretçiler";
        await duplicate.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Aynı seviyede bu adda bir kategori var.", duplicate.ErrorMessage);
        duplicate.CancelCommand.Execute(null);
        Assert.False(bank.IsCategoryDialogOpen);

        // Yeniden adlandır.
        bank.SelectedCategory = child;
        bank.EditCategoryCommand.Execute(null);
        var edit = bank.CategoryDialog!;
        Assert.Equal("İşaretçiler", edit.Name);
        edit.Name = "Göstericiler";
        await edit.SaveCommand.ExecuteAsync(null);
        await bank.CategoryDialogSaveTask;
        Assert.Equal("Göstericiler", bank.SelectedCategory!.Name);

        // Boş alt kategori silinir; soru içeren üst kategori silinmez.
        await bank.DeleteCategoryCommand.ExecuteAsync(null);
        Assert.Contains("silindi", bank.StatusMessage);
        Assert.Empty(bank.Categories.Single(c => c.Name == "C Temelleri").Children);

        bank.SelectedCategory = bank.Categories.Single(c => c.Name == "C Temelleri");
        await bank.DeleteCategoryCommand.ExecuteAsync(null);
        Assert.StartsWith("İçinde soru veya alt kategori", bank.ErrorMessage);
    }
}
