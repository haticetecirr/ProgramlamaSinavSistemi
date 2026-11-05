using System.Collections.Concurrent;
using System.Diagnostics;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

// VT-37 yük testi: gerçek HTTPS ve SignalR üzerinden sanal öğrenciler.
// Öğrenci ve şube oluşturur, ilk giriş yapar: yalnızca scripts\load-test.ps1 ile, geçici veritabanı şemasına karşı çalıştırılır.
const string StudentPassword = "Yuk-Testi-Parola-1";
const string InstructorNewPassword = "Yuk-Testi-Egitmen-1";
const double SaveTargetMs = 300;
const double PublishTargetMs = 1000;
const double ResultsTargetMs = 1000;

var serverUrl = new Uri(Argument("--server", "https://127.0.0.1:5000"));
var studentCount = int.Parse(Argument("--students", "60"));
var runSeconds = int.Parse(Argument("--seconds", "60"));
var intervalSeconds = int.Parse(Argument("--interval", "10"));
var instructorTemporaryPassword = Environment.GetEnvironmentVariable("PROGLAB_LOAD_INSTRUCTOR_PASSWORD")
    ?? throw new InvalidOperationException("PROGLAB_LOAD_INSTRUCTOR_PASSWORD tanımlı değil. Bu aracı scripts\\load-test.ps1 ile çalıştırın.");

var factory = ServerSessionFactory.Local();
var errors = new ConcurrentBag<string>();
var failed = false;

// --- Hazırlık: öğrenciler, şube, sınav ---
var instructor = factory.Create(serverUrl);
await instructor.Api.LoginAsync("egitmen", instructorTemporaryPassword, UserRole.Instructor);
await instructor.Api.ChangePasswordAsync(instructorTemporaryPassword, InstructorNewPassword);
await instructor.Api.LoginAsync("egitmen", InstructorNewPassword, UserRole.Instructor);

var classes = await instructor.Api.GetClassesAsync();
var seeded = await instructor.Api.GetClassAsync(classes.First(c => c.MemberCount > 0).Id);
var studentIds = seeded.Members.Select(m => m.Id).ToList();
for (var i = studentIds.Count + 1; i <= studentCount; i++)
{
    var created = await instructor.Api.CreateStudentAsync(Number(i), $"Demo Öğrenci {i}", Email(i));
    studentIds.Add(created.Id);
}

var loadClass = await instructor.Api.CreateClassAsync("Yük testi", "Test");
await instructor.Api.ReplaceClassMembersAsync(loadClass.Id, studentIds.Take(studentCount).ToList());

var category = (await instructor.Api.GetCategoriesAsync())[0].Id;
QuestionOptionInput[] options = [new("Yanlış", false), new("Doğru", true), new("Diğer", false)];
var mcq = await instructor.Api.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.MultipleChoice, "Yük şıklı", "Şıklı soru", 60, options, null));
var classic = await instructor.Api.CreateQuestionAsync(new SaveQuestionRequest(category, QuestionType.Classic, "Yük klasik", "Klasik soru", 40, null, ["yığın"]));
var exam = await instructor.Api.CreateExamAsync(new SaveExamRequest("Yük testi", null, ExamKind.Exam, loadClass.Id, 40, null, null));
await instructor.Api.ReplaceExamQuestionsAsync(exam.Id, [new(mcq.Id, 1, null), new(classic.Id, 2, null)]);

// --- Öğrenciler bağlanır (BCrypt yavaştır: giriş sırayla gruplanır, ölçüme girmez) ---
Console.WriteLine($"{studentCount} sanal öğrenci bağlanıyor...");
var students = new VirtualStudent[studentCount];
var published = new ConcurrentBag<double>();
var publishClock = new Stopwatch();
var loginGate = new SemaphoreSlim(8);
await Task.WhenAll(Enumerable.Range(0, studentCount).Select(async i =>
{
    await loginGate.WaitAsync();
    try
    {
        var session = factory.Create(serverUrl);
        await session.Api.FirstLoginAsync(Number(i + 1), Email(i + 1), StudentPassword);
        session.Hub.ExamPublished += e =>
        {
            if (e.ExamId == exam.Id)
            {
                published.Add(publishClock.Elapsed.TotalMilliseconds);
            }
        };
        await session.Hub.StartAsync();
        students[i] = new VirtualStudent(session);
    }
    finally
    {
        loginGate.Release();
    }
}));

// --- Yayınlama bildirimi (NFR-03 / VT-17) ---
publishClock.Start();
await instructor.Api.PublishExamAsync(exam.Id);
var publishDeadline = Stopwatch.StartNew();
while (published.Count < studentCount && publishDeadline.Elapsed < TimeSpan.FromSeconds(10))
{
    await Task.Delay(20);
}

// --- Aynı anda katılım ve kâğıt ---
Console.WriteLine("Hepsi aynı anda katılıyor...");
var joinTimes = new ConcurrentBag<double>();
await Task.WhenAll(students.Select(async s =>
{
    await Measure(joinTimes, "katılım", async () =>
    {
        var joined = await s.Session.Api.JoinExamAsync(exam.Id);
        var paper = await s.Session.Api.GetPaperAsync(exam.Id);
        s.SubmissionId = joined.SubmissionId;
        s.Mcq = paper.Questions[0];
        s.Classic = paper.Questions[1];
    });
}));

// --- Cevap kaydı: her öğrenci ortalama N saniyede bir ---
Console.WriteLine($"{runSeconds} sn boyunca cevap kaydı...");
var saveTimes = new ConcurrentBag<double>();
var runClock = Stopwatch.StartNew();
await Task.WhenAll(students.Select(async (s, index) =>
{
    // Katılamayan öğrenci hata olarak sayıldı; kayıt yapamaz.
    if (s.Mcq is null || s.Classic is null)
    {
        return;
    }

    var random = new Random(index);
    await Task.Delay(random.Next(intervalSeconds * 1000)); // Hepsi aynı anda değil, sınıftaki gibi dağınık.
    var step = 0;
    while (runClock.Elapsed.TotalSeconds < runSeconds)
    {
        var request = step % 2 == 0
            ? (s.Mcq!.ExamQuestionId, new SaveAnswerRequest(s.Mcq.Options[step % 4 == 0 ? 0 : 1].Id, null))
            : (s.Classic!.ExamQuestionId, new SaveAnswerRequest(null, $"ara cevap {step} öğrenci {index}"));
        await Measure(saveTimes, "kayıt", () => s.Session.Api.SaveAnswerAsync(s.SubmissionId, request.Item1, request.Item2));
        step++;
        await Task.Delay(random.Next(intervalSeconds * 500, intervalSeconds * 1500));
    }

    // Son hâl: şıklı doğru; klasik yalnız çift sıradakilerde doğru. Toplam puanlar buna göre denetlenir.
    await Measure(saveTimes, "kayıt", () => s.Session.Api.SaveAnswerAsync(s.SubmissionId, s.Mcq!.ExamQuestionId, new(s.Mcq.Options[1].Id, null)));
    await Measure(saveTimes, "kayıt", () => s.Session.Api.SaveAnswerAsync(s.SubmissionId, s.Classic!.ExamQuestionId, new(null, index % 2 == 0 ? " Yığın. " : "yanlış")));
}));

// --- Eğitmen ekranları öğrenciler sürerken ---
var liveTimes = new ConcurrentBag<double>();
await Measure(liveTimes, "canlı izleme", () => instructor.Api.GetLiveExamAsync(exam.Id));

// --- Aynı anda teslim ---
Console.WriteLine("Hepsi aynı anda teslim ediyor...");
var submitTimes = new ConcurrentBag<double>();
await Task.WhenAll(students.Where(s => s.Mcq is not null).Select(s => Measure(submitTimes, "teslim", () => s.Session.Api.SubmitAsync(s.SubmissionId))));

// --- Sonuç listesi ve veri doğruluğu ---
var resultTimes = new ConcurrentBag<double>();
ExamResultsDto? results = null;
await Measure(resultTimes, "sonuç listesi", async () => results = await instructor.Api.GetExamResultsAsync(exam.Id));

var logTotal = (await instructor.Api.GetExamLogsAsync(exam.Id, "SUBMITTED")).Total;
var wrongScores = 0;
if (results is not null)
{
    foreach (var row in results.Rows)
    {
        var index = int.Parse(row.Username[^5..]) - 1;
        var expected = index % 2 == 0 ? 100m : 60m;
        if (row.Status != SubmissionStatus.Submitted || row.TotalScore != expected)
        {
            wrongScores++;
        }
    }
}

await Task.WhenAll(students.Select(s => s.Session.Hub.DisposeAsync().AsTask()));

// --- Rapor ---
Console.WriteLine();
Console.WriteLine($"Sanal öğrenci: {studentCount}, süre: {runSeconds} sn, ortalama aralık: {intervalSeconds} sn");
Console.WriteLine($"{"İşlem",-16} {"Adet",6} {"Ort",8} {"%50",8} {"%95",8} {"En çok",8}  (ms)");
Report("yayın bildirimi", published);
Report("katılım+kâğıt", joinTimes);
Report("cevap kaydı", saveTimes);
Report("canlı izleme", liveTimes);
Report("teslim", submitTimes);
Report("sonuç listesi", resultTimes);
Console.WriteLine();

Check($"Hata yok ({errors.Count})", errors.IsEmpty);
Check($"Cevap kaydı %95 < {SaveTargetMs} ms ({Percentile(saveTimes, 95):0} ms)", Percentile(saveTimes, 95) < SaveTargetMs);
Check($"Bildirim {published.Count}/{studentCount} öğrenciye", published.Count == studentCount);
Check($"Bildirim en geç {PublishTargetMs} ms içinde ({Max(published):0} ms)", Max(published) <= PublishTargetMs);
Check($"Sonuç listesi < {ResultsTargetMs} ms ({Max(resultTimes):0} ms)", Max(resultTimes) < ResultsTargetMs);
Check($"Tüm teslimler ve puanlar doğru (hatalı: {wrongScores})", results is not null && results.Rows.Count == studentCount && wrongScores == 0);
Check($"Her teslim loglandı ({logTotal})", logTotal == studentCount);

foreach (var group in errors.GroupBy(e => e).OrderByDescending(g => g.Count()).Take(10))
{
    Console.WriteLine($"  {group.Count()} × {group.Key}");
}

return failed ? 1 : 0;

string Argument(string name, string fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

static string Number(int index) => $"20269{index:D5}";

static string Email(int index) => $"{Number(index)}@ogr.demo.local";

async Task Measure(ConcurrentBag<double> times, string label, Func<Task> action)
{
    var clock = Stopwatch.StartNew();
    try
    {
        await action();
        times.Add(clock.Elapsed.TotalMilliseconds);
    }
    catch (Exception ex)
    {
        errors.Add($"{label}: {(ex is ApiException api ? $"{api.Code} {api.StatusCode} {api.Message}" : ex.GetType().Name)}");
    }
}

static double Percentile(IEnumerable<double> values, int percent)
{
    var sorted = values.OrderBy(v => v).ToList();
    return sorted.Count == 0 ? double.NaN : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * percent / 100.0) - 1)];
}

static double Max(IEnumerable<double> values) => values.DefaultIfEmpty(double.NaN).Max();

static void Report(string name, IReadOnlyCollection<double> values) =>
    Console.WriteLine($"{name,-16} {values.Count,6} {values.DefaultIfEmpty(0).Average(),8:0} {Percentile(values, 50),8:0} {Percentile(values, 95),8:0} {Max(values),8:0}");

void Check(string label, bool ok)
{
    Console.WriteLine($"{(ok ? "GEÇTİ" : "KALDI")} : {label}");
    failed |= !ok;
}

internal sealed class VirtualStudent(ServerSession session)
{
    public ServerSession Session { get; } = session;

    public long SubmissionId { get; set; }

    public StudentQuestionDto? Mcq { get; set; }

    public StudentQuestionDto? Classic { get; set; }
}
