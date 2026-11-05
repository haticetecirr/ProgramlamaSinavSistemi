using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Grading;
using ProgLab.Server.Security;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Data.Seed;

/// <summary>
/// Geliştirme ve demo verisi. Her grup yalnızca tablosu boşsa eklenir, bu yüzden
/// sunucu kaç kez açılırsa açılsın veri tekrarlanmaz. Demo verisi gerçek öğrenci verisi değildir.
/// </summary>
public static class DemoDataSeeder
{
    private const string InstructorUsername = "egitmen";
    private const int DemoStudentCount = 30;
    private const decimal DemoQuestionPoints = 10m;

    public static async Task SeedAsync(AppDbContext db, string? instructorTemporaryPassword, CancellationToken ct = default)
    {
        await SeedUsersAsync(db, instructorTemporaryPassword, ct);
        await SeedClassAsync(db, ct);
        await SeedQuestionBankAsync(db, ct);
    }

    private static async Task SeedUsersAsync(AppDbContext db, string? temporaryPassword, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(temporaryPassword))
        {
            throw new InvalidOperationException(
                "Eğitmen hesabı oluşturulamadı: appsettings.Local.json içinde 'Seed:InstructorTemporaryPassword' yok.");
        }

        db.Users.Add(new User
        {
            Username = InstructorUsername,
            FullName = "Demo Eğitmen",
            Role = UserRole.Instructor,
            PasswordHash = PasswordHasher.Hash(temporaryPassword),
            MustChangePassword = true
        });

        for (var i = 1; i <= DemoStudentCount; i++)
        {
            var number = $"20269{i:D5}";
            db.Users.Add(new User
            {
                Username = number,
                FullName = $"Demo Öğrenci {i}",
                Email = $"{number}@ogr.demo.local",
                Role = UserRole.Student
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedClassAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Classes.AnyAsync(ct))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var instructorId = await db.Users.Where(u => u.Role == UserRole.Instructor).Select(u => u.Id).FirstAsync(ct);
        var studentIds = await db.Users.Where(u => u.Role == UserRole.Student).Select(u => u.Id).ToListAsync(ct);

        db.Classes.Add(new SchoolClass
        {
            Name = "Prog. Lab. I – A şubesi",
            Term = "2026-2027 Güz",
            InstructorId = instructorId,
            CreatedAt = now,
            Members = studentIds.Select(id => new ClassMember { StudentId = id, AddedAt = now }).ToList()
        });

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedQuestionBankAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(ct) || await db.Questions.AnyAsync(ct))
        {
            return;
        }

        var instructorId = await db.Users.Where(u => u.Role == UserRole.Instructor).Select(u => u.Id).FirstAsync(ct);
        var basics = new Category { Name = "C Temelleri" };
        var control = new Category { Name = "Kontrol Yapıları" };
        var structures = new Category { Name = "Diziler ve Veri Yapıları" };
        db.Categories.AddRange(basics, control, structures);

        var now = DateTimeOffset.UtcNow;

        Question Mcq(Category category, string title, string text, string correct, params string[] wrong)
        {
            // Doğru seçeneğin yeri soruya göre değişsin diye başlıktan türetilir.
            var options = wrong.ToList();
            options.Insert(title.Length % (wrong.Length + 1), correct);

            return new Question
            {
                Category = category,
                Type = QuestionType.MultipleChoice,
                Title = title,
                Text = text,
                DefaultPoints = DemoQuestionPoints,
                CreatedBy = instructorId,
                CreatedAt = now,
                UpdatedAt = now,
                Options = options.Select((option, index) => new QuestionOption
                {
                    OrderNo = (short)(index + 1),
                    Text = option,
                    IsCorrect = option == correct
                }).ToList()
            };
        }

        Question Classic(Category category, string title, string text, params string[] accepted) => new()
        {
            Category = category,
            Type = QuestionType.Classic,
            Title = title,
            Text = text,
            DefaultPoints = DemoQuestionPoints,
            CreatedBy = instructorId,
            CreatedAt = now,
            UpdatedAt = now,
            AnswerKeys = accepted.Select(answer => new ClassicAnswerKey
            {
                AcceptedText = answer,
                NormalizedText = AnswerNormalizer.Normalize(answer)
            }).ToList()
        };

        db.Questions.AddRange(
            Mcq(basics, "Ondalıklı sayı tipi", "Aşağıdaki veri tiplerinden hangisi C dilinde ondalıklı sayı tutar?", "float", "int", "char", "void"),
            Mcq(basics, "printf başlık dosyası", "printf fonksiyonu hangi başlık dosyasında bildirilmiştir?", "stdio.h", "stdlib.h", "math.h", "string.h"),
            Mcq(control, "Döngüyü sonlandırma", "Bir döngüyü o anda tamamen sonlandıran anahtar sözcük hangisidir?", "break", "continue", "switch", "case"),
            Mcq(control, "do-while çalışma sayısı", "do-while döngüsünün gövdesi en az kaç kez çalışır?", "1", "0", "2", "3"),
            Mcq(structures, "Dizinin ilk indisi", "int d[5]; dizisinin ilk elemanının indisi kaçtır?", "0", "1", "4", "5"),
            Classic(basics, "Adres tutan değişken", "Bir bellek adresini tutan değişkene ne denir?", "işaretçi", "pointer"),
            Classic(basics, "Başlangıç fonksiyonu", "Bir C programının çalışmaya başladığı fonksiyonun adı nedir?", "main"),
            Classic(control, "Adımı atlama", "Döngünün o anki adımını atlayıp sonraki adıma geçiren anahtar sözcük nedir?", "continue"),
            Classic(structures, "Metin uzunluğu", "Bir karakter dizisinin uzunluğunu bulan standart C fonksiyonu hangisidir?", "strlen"),
            Classic(structures, "LIFO veri yapısı", "Son giren ilk çıkar (LIFO) ilkesiyle çalışan veri yapısının adı nedir?", "yığın", "stack", "yığıt"));

        await db.SaveChangesAsync(ct);
    }
}
