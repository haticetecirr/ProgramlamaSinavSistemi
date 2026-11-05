using Microsoft.EntityFrameworkCore;
using ProgLab.InstructorApp.ViewModels;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Sınav ekranlarının arkasındaki akışlar, gerçek API'ye karşı (pencere açmadan).</summary>
public sealed class ExamViewModelTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private async Task<MainWindowViewModel> MainOnExamsAsync()
    {
        var main = new MainWindowViewModel(await TestAccounts.InstructorAsync(fixture.Server));
        main.ShowExamsCommand.Execute(null);
        await Assert.IsType<ExamListViewModel>(main.CurrentPage).LoadCommand.ExecutionTask!;
        return main;
    }

    private static async Task<ExamEditorViewModel> OpenNewEditorAsync(MainWindowViewModel main)
    {
        Assert.IsType<ExamListViewModel>(main.CurrentPage).NewExamCommand.Execute(null);
        var editor = Assert.IsType<ExamEditorViewModel>(main.CurrentPage);
        await editor.LoadCommand.ExecutionTask!;
        return editor;
    }

    private static async Task<ExamListViewModel> BackOnListAsync(MainWindowViewModel main)
    {
        var list = Assert.IsType<ExamListViewModel>(main.CurrentPage);
        await list.LoadCommand.ExecutionTask!;
        return list;
    }

    private static void AddBankQuestions(ExamEditorViewModel editor, params string[] titles)
    {
        foreach (var title in titles)
        {
            editor.SelectedBankQuestion = editor.BankQuestions.Single(q => q.Title == title);
            editor.AddSelectedQuestionCommand.Execute(null);
        }
    }

    [Fact]
    public async Task NewEditor_HasDefaults_AndLoadsClassesAndBank()
    {
        var editor = await OpenNewEditorAsync(await MainOnExamsAsync());

        Assert.True(editor.IsExam);
        Assert.Equal(40m, editor.DurationMinutes);
        Assert.Contains("A şubesi", editor.SelectedClass!.Name);
        Assert.Equal(10, editor.BankQuestions.Count);
        Assert.Equal("Yayınla ve başlat", editor.PublishText);
        Assert.Equal("0 soru · Toplam 0 puan", editor.TotalText);

        // Saat seçicileri boş kalmaz: Avalonia boş alana İngilizce "hour/minute" yazar.
        Assert.Equal(new TimeSpan(9, 0, 0), editor.StartTime);
        Assert.Equal(new TimeSpan(23, 59, 0), editor.EndTime);
    }

    [Fact]
    public async Task ExamDraft_ReopenedAsAssignment_KeepsTimePickersFilled()
    {
        var main = await MainOnExamsAsync();
        var editor = await OpenNewEditorAsync(main);
        editor.ExamTitle = "Saat alanı sınavı";
        await editor.SaveDraftCommand.ExecuteAsync(null);
        var list = await BackOnListAsync(main);

        list.SelectedExam = list.Exams.Single(e => e.Title == "Saat alanı sınavı");
        list.OpenExamCommand.Execute(null);
        var reopened = Assert.IsType<ExamEditorViewModel>(main.CurrentPage);
        await reopened.LoadCommand.ExecutionTask!;
        reopened.IsAssignment = true;

        Assert.Equal(new TimeSpan(9, 0, 0), reopened.StartTime);
        Assert.Equal(new TimeSpan(23, 59, 0), reopened.EndTime);
    }

    [Fact]
    public async Task QuestionPicker_AddsReordersRemoves_AndKeepsTotal()
    {
        var editor = await OpenNewEditorAsync(await MainOnExamsAsync());

        editor.AddSelectedQuestionCommand.Execute(null);
        Assert.Equal("Önce bankadan bir soru seçin.", editor.ErrorMessage);

        AddBankQuestions(editor, "printf başlık dosyası", "LIFO veri yapısı", "Metin uzunluğu");
        Assert.Null(editor.ErrorMessage);
        Assert.Equal("3 soru · Toplam 30 puan", editor.TotalText);

        // Aynı soru ikinci kez eklenmez.
        AddBankQuestions(editor, "printf başlık dosyası");
        Assert.Equal("Bu soru sınava zaten eklendi.", editor.ErrorMessage);
        Assert.Equal(3, editor.Questions.Count);

        // Puan değişince toplam anında değişir.
        editor.Questions[0].Points = 25;
        Assert.Equal("3 soru · Toplam 45 puan", editor.TotalText);

        // Sıralama; uçlarda taşma olmaz.
        editor.Questions[1].MoveUpCommand.Execute(null);
        Assert.Equal("LIFO veri yapısı", editor.Questions[0].Title);
        editor.Questions[0].MoveUpCommand.Execute(null);
        Assert.Equal("LIFO veri yapısı", editor.Questions[0].Title);
        editor.Questions[2].MoveDownCommand.Execute(null);
        Assert.Equal("Metin uzunluğu", editor.Questions[2].Title);
        editor.Questions[0].MoveDownCommand.Execute(null);
        Assert.Equal("printf başlık dosyası", editor.Questions[0].Title);

        editor.Questions[0].RemoveCommand.Execute(null);
        Assert.Equal("2 soru · Toplam 20 puan", editor.TotalText);
    }

    [Fact]
    public async Task SaveDraft_ValidatesForm_ThenAppearsInDraftTab_AndReopensWithSameContent()
    {
        var main = await MainOnExamsAsync();
        var editor = await OpenNewEditorAsync(main);

        await editor.SaveDraftCommand.ExecuteAsync(null);
        Assert.Equal("Başlık zorunludur.", editor.ErrorMessage);

        editor.ExamTitle = "Arayüz taslağı";
        editor.Description = "Açıklama";
        editor.DurationMinutes = 55;
        AddBankQuestions(editor, "Dizinin ilk indisi", "Başlangıç fonksiyonu");
        editor.Questions[0].Points = 0;
        await editor.SaveDraftCommand.ExecuteAsync(null);
        Assert.Equal("Her sorunun puanı 0'dan büyük olmalıdır.", editor.ErrorMessage);

        editor.Questions[0].Points = 15;
        await editor.SaveDraftCommand.ExecuteAsync(null);

        var list = await BackOnListAsync(main);
        Assert.Contains("taslak olarak kaydedildi", list.StatusMessage);
        var row = list.Exams.Single(e => e.Title == "Arayüz taslağı");
        Assert.Contains("55 dk", row.Details);
        Assert.Contains("2 soru", row.Details);
        Assert.Contains("25 puan", row.Details);

        list.SelectedExam = row;
        list.OpenExamCommand.Execute(null);
        var reopened = Assert.IsType<ExamEditorViewModel>(main.CurrentPage);
        await reopened.LoadCommand.ExecutionTask!;
        Assert.True(reopened.IsDraft);
        Assert.Equal("Arayüz taslağı", reopened.ExamTitle);
        Assert.Equal("Açıklama", reopened.Description);
        Assert.Equal(55m, reopened.DurationMinutes);
        Assert.Equal(["Dizinin ilk indisi", "Başlangıç fonksiyonu"], reopened.Questions.Select(q => q.Title));
        Assert.Equal(15m, reopened.Questions[0].Points);

        // Aynı formdan tekrar kaydetmek sürüm çakışması vermez.
        reopened.ExamTitle = "Arayüz taslağı 2";
        await reopened.SaveDraftCommand.ExecuteAsync(null);
        var again = await BackOnListAsync(main);
        Assert.Contains(again.Exams, e => e.Title == "Arayüz taslağı 2");
    }

    [Fact]
    public async Task TwoOpenEditors_SecondSave_ShowsConflictMessage()
    {
        var main = await MainOnExamsAsync();
        var editor = await OpenNewEditorAsync(main);
        editor.ExamTitle = "Çakışma sınavı";
        await editor.SaveDraftCommand.ExecuteAsync(null);
        var list = await BackOnListAsync(main);
        var row = list.Exams.Single(e => e.Title == "Çakışma sınavı");

        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var first = new ExamEditorViewModel(api, row.Exam.Id, _ => { });
        var second = new ExamEditorViewModel(api, row.Exam.Id, _ => { });
        await first.LoadCommand.ExecuteAsync(null);
        await second.LoadCommand.ExecuteAsync(null);

        first.ExamTitle = "Birinci kayıt";
        await first.SaveDraftCommand.ExecuteAsync(null);
        Assert.Null(first.ErrorMessage);

        second.ExamTitle = "İkinci kayıt";
        await second.SaveDraftCommand.ExecuteAsync(null);
        Assert.Equal("Bu kayıt başka bir pencerede değişti. Listeyi yenileyin.", second.ErrorMessage);
    }

    [Fact]
    public async Task Publish_AsksConfirmation_ThenExamMovesToPublishedTab_ReadOnly_AndEnds_AndCopies()
    {
        var main = await MainOnExamsAsync();
        var editor = await OpenNewEditorAsync(main);
        editor.ExamTitle = "Akış sınavı";
        AddBankQuestions(editor, "Ondalıklı sayı tipi");

        editor.AskPublishCommand.Execute(null);
        Assert.True(editor.IsConfirmingPublish);
        Assert.Contains("şimdi başlayacak", editor.PublishWarning);
        editor.CancelPublishCommand.Execute(null);
        Assert.False(editor.IsConfirmingPublish);
        Assert.Same(editor, main.CurrentPage);

        editor.AskPublishCommand.Execute(null);
        await editor.ConfirmPublishCommand.ExecuteAsync(null);

        var list = await BackOnListAsync(main);
        Assert.Contains("yayınlandı", list.StatusMessage);
        Assert.DoesNotContain(list.Exams, e => e.Title == "Akış sınavı");

        // Yayında sekmesi.
        list.ShowPublished = true;
        await list.TabLoadTask;
        Assert.True(list.ShowPublished);
        Assert.False(list.ShowDrafts);
        list.SelectedExam = list.Exams.Single(e => e.Title == "Akış sınavı");

        // Yayındaki sınav salt okunur açılır.
        list.OpenExamCommand.Execute(null);
        var readOnly = Assert.IsType<ExamEditorViewModel>(main.CurrentPage);
        await readOnly.LoadCommand.ExecutionTask!;
        Assert.False(readOnly.IsDraft);
        Assert.StartsWith("Yayındaki Sınav", readOnly.Heading);
        Assert.StartsWith("Başlangıç:", readOnly.ScheduleText);
        readOnly.CloseCommand.Execute(null);

        // Bitirme onay ister.
        var again = await BackOnListAsync(main);
        again.ShowPublished = true;
        await again.TabLoadTask;
        again.SelectedExam = again.Exams.Single(e => e.Title == "Akış sınavı");
        again.AskEndCommand.Execute(null);
        Assert.True(again.IsConfirmingEnd);
        again.CancelConfirmationCommand.Execute(null);
        Assert.False(again.IsConfirmingEnd);
        Assert.Contains(again.Exams, e => e.Title == "Akış sınavı");

        again.AskEndCommand.Execute(null);
        await again.ConfirmEndCommand.ExecuteAsync(null);
        Assert.Null(again.ErrorMessage);
        Assert.DoesNotContain(again.Exams, e => e.Title == "Akış sınavı");

        // Tamamlandı sekmesinden kopya.
        again.ShowCompleted = true;
        await again.TabLoadTask;
        again.SelectedExam = again.Exams.Single(e => e.Title == "Akış sınavı");
        await again.CopyCommand.ExecuteAsync(null);
        Assert.True(again.ShowDrafts);
        Assert.Contains(again.Exams, e => e.Title == "Akış sınavı (kopya)");
    }

    [Fact]
    public async Task PublishErrors_AreShownInTurkish_AndDraftIsKept()
    {
        var main = await MainOnExamsAsync();
        var editor = await OpenNewEditorAsync(main);
        editor.ExamTitle = "Sorusuz sınav";

        editor.AskPublishCommand.Execute(null);
        await editor.ConfirmPublishCommand.ExecuteAsync(null);

        Assert.Equal("Yayınlamak için sınava en az bir soru ekleyin.", editor.ErrorMessage);
        Assert.Same(editor, main.CurrentPage);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(ExamStatus.Draft, (await db.Exams.SingleAsync(e => e.Title == "Sorusuz sınav")).Status);
    }

    [Fact]
    public async Task Assignment_RequiresDates_ConvertsLocalTime_AndShowsItBack()
    {
        var main = await MainOnExamsAsync();
        var editor = await OpenNewEditorAsync(main);
        editor.ExamTitle = "Arayüz ödevi";
        editor.IsAssignment = true;
        Assert.False(editor.IsExam);
        Assert.Equal("Yayınla", editor.PublishText);
        Assert.Contains("başlangıç saatinde kendiliğinden açılacak", editor.PublishWarning);

        await editor.SaveDraftCommand.ExecuteAsync(null);
        Assert.Equal("Ödev için başlangıç ve bitiş tarih-saatini girin.", editor.ErrorMessage);

        var day = DateTime.Today.AddDays(3);
        editor.StartDate = day;
        editor.StartTime = new TimeSpan(9, 30, 0);
        editor.EndDate = day.AddDays(-1);
        editor.EndTime = new TimeSpan(17, 0, 0);
        await editor.SaveDraftCommand.ExecuteAsync(null);
        Assert.StartsWith("Başlangıç bitişten önce", editor.ErrorMessage);

        editor.EndDate = day.AddDays(2);
        AddBankQuestions(editor, "Adres tutan değişken");
        await editor.SaveDraftCommand.ExecuteAsync(null);

        await using (var db = fixture.CreateDbContext())
        {
            var stored = await db.Exams.SingleAsync(e => e.Title == "Arayüz ödevi");
            Assert.Null(stored.DurationMinutes);
            Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(day.AddHours(9.5), DateTimeKind.Local)).ToUnixTimeSeconds(), stored.StartsAt!.Value.ToUnixTimeSeconds());
        }

        var list = await BackOnListAsync(main);
        Assert.Contains($"{day:dd.MM.yyyy} 09:30", list.Exams.Single(e => e.Title == "Arayüz ödevi").Details);

        list.SelectedExam = list.Exams.Single(e => e.Title == "Arayüz ödevi");
        list.OpenExamCommand.Execute(null);
        var reopened = Assert.IsType<ExamEditorViewModel>(main.CurrentPage);
        await reopened.LoadCommand.ExecutionTask!;
        Assert.True(reopened.IsAssignment);
        Assert.Equal(day, reopened.StartDate);
        Assert.Equal(new TimeSpan(9, 30, 0), reopened.StartTime);
        Assert.Equal(day.AddDays(2), reopened.EndDate);
    }

    [Fact]
    public async Task DeleteDraft_AsksConfirmation()
    {
        var main = await MainOnExamsAsync();
        var editor = await OpenNewEditorAsync(main);
        editor.ExamTitle = "Silinecek taslak";
        await editor.SaveDraftCommand.ExecuteAsync(null);
        var list = await BackOnListAsync(main);

        list.AskDeleteCommand.Execute(null);
        Assert.Equal("Önce listeden bir sınav seçin.", list.ErrorMessage);

        list.SelectedExam = list.Exams.Single(e => e.Title == "Silinecek taslak");
        list.AskDeleteCommand.Execute(null);
        Assert.True(list.IsConfirmingDelete);
        await list.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(list.Exams, e => e.Title == "Silinecek taslak");
        Assert.Contains("silindi", list.StatusMessage);
    }

    [Fact]
    public async Task DeletedQuestionInDraft_IsMarked_AndBlocksPublishUntilRemoved()
    {
        var main = await MainOnExamsAsync();
        var editor = await OpenNewEditorAsync(main);
        editor.ExamTitle = "Silinmiş sorulu taslak";
        AddBankQuestions(editor, "Döngüyü sonlandırma", "Adımı atlama");
        await editor.SaveDraftCommand.ExecuteAsync(null);
        var list = await BackOnListAsync(main);

        var api = await TestAccounts.InstructorAsync(fixture.Server);
        var questionId = (await api.GetQuestionsAsync(search: "Döngüyü sonlandırma")).Items.Single().Id;
        await api.DeleteQuestionAsync(questionId);

        list.SelectedExam = list.Exams.Single(e => e.Title == "Silinmiş sorulu taslak");
        list.OpenExamCommand.Execute(null);
        var reopened = Assert.IsType<ExamEditorViewModel>(main.CurrentPage);
        await reopened.LoadCommand.ExecutionTask!;
        Assert.Contains("BANKADAN SİLİNMİŞ", reopened.Questions[0].Details);

        reopened.AskPublishCommand.Execute(null);
        await reopened.ConfirmPublishCommand.ExecuteAsync(null);
        Assert.StartsWith("Sınavda bankadan silinmiş", reopened.ErrorMessage);

        reopened.Questions[0].RemoveCommand.Execute(null);
        reopened.AskPublishCommand.Execute(null);
        await reopened.ConfirmPublishCommand.ExecuteAsync(null);
        Assert.Contains("yayınlandı", (await BackOnListAsync(main)).StatusMessage);
    }
}
