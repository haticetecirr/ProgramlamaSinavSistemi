using ProgLab.Shared;

namespace ProgLab.Client.Core;

/// <summary>Sunucunun hata kodlarını kullanıcıya gösterilecek Türkçe mesajlara çevirir.</summary>
public static class ErrorMessages
{
    private static readonly Dictionary<string, string> Messages = new()
    {
        [ErrorCodes.InvalidCredentials] = "Bilgiler yanlış. Lütfen kontrol edip tekrar deneyin.",
        [ErrorCodes.TokenExpired] = "Oturum süreniz doldu. Lütfen yeniden giriş yapın.",
        [ErrorCodes.TokenInvalid] = "Oturumunuz geçersiz. Lütfen yeniden giriş yapın.",
        [ErrorCodes.WrongAppRole] = "Bu hesapla bu uygulamaya giriş yapılamaz.",
        [ErrorCodes.AccountDisabled] = "Hesabınız pasif durumda. Eğitmeninizle görüşün.",
        [ErrorCodes.PasswordChangeRequired] = "Devam etmek için parolanızı değiştirmelisiniz.",
        [ErrorCodes.FirstLoginRequired] = "Henüz parolanız yok. \"İlk kez giriyorum\" bağlantısını kullanın.",
        [ErrorCodes.AlreadyVerified] = "Bu hesap için ilk giriş zaten yapılmış. Parolanızla giriş yapın.",
        [ErrorCodes.RateLimit] = "Çok fazla deneme yaptınız. Bir dakika sonra tekrar deneyin.",
        [ErrorCodes.PasswordTooShort] = "Parola en az 8 karakter olmalıdır.",
        [ErrorCodes.Forbidden] = "Bu işlem için yetkiniz yok.",
        [ErrorCodes.InvalidRequest] = "Girilen bilgiler eksik veya hatalı.",
        [ErrorCodes.DatabaseUnavailable] = "Sunucu veritabanına ulaşamıyor. Eğitmene haber verin.",
        [ErrorCodes.UsernameExists] = "Bu öğrenci numarası zaten kayıtlı.",
        [ErrorCodes.EmailExists] = "Bu e-posta zaten kayıtlı.",
        [ErrorCodes.UserNotFound] = "Öğrenci bulunamadı.",
        [ErrorCodes.ClassNotFound] = "Şube bulunamadı.",
        [ErrorCodes.ClassNameExists] = "Bu dönemde aynı adlı bir şube var.",
        [ErrorCodes.ClassHasExams] = "İçinde sınav olan şube silinemez.",
        [ErrorCodes.InvalidStudent] = "Listede öğrenci olmayan bir kullanıcı var.",
        [ErrorCodes.CategoryNotEmpty] = "İçinde soru veya alt kategori bulunan kategori silinemez. Silinmiş soruların geçmiş kayıtları da sayılır.",
        [ErrorCodes.CategoryNameExists] = "Aynı seviyede bu adda bir kategori var.",
        [ErrorCodes.CategoryNotFound] = "Kategori bulunamadı. Listeyi yenileyin.",
        [ErrorCodes.CategoryCycle] = "Kategori kendi alt kategorisinin altına taşınamaz.",
        [ErrorCodes.QuestionNotFound] = "Soru bulunamadı. Listeyi yenileyin.",
        [ErrorCodes.McqOptionCount] = "Çoktan seçmeli soruda 2 ile 6 arasında seçenek olmalıdır.",
        [ErrorCodes.McqNoCorrectOption] = "Doğru seçeneği işaretleyin.",
        [ErrorCodes.McqMultipleCorrect] = "Yalnızca bir doğru seçenek olabilir.",
        [ErrorCodes.ClassicNoAnswerKey] = "Klasik soruda en az bir kabul edilen cevap olmalıdır.",
        [ErrorCodes.DuplicateAnswerKey] = "Aynı kabul edilen cevap iki kez yazılmış (büyük/küçük harf ve sondaki noktalama fark etmez).",
        [ErrorCodes.InvalidPoints] = "Puan 0'dan büyük olmalıdır.",
        [ErrorCodes.UnsupportedQuestionType] = "Bu soru tipi desteklenmiyor.",
        [ErrorCodes.InvalidState] = "Bu kayıt başka bir pencerede değişti. Listeyi yenileyin.",
        [ErrorCodes.ExamNotFound] = "Sınav bulunamadı. Listeyi yenileyin.",
        [ErrorCodes.ExamNotDraft] = "Bu sınav yayınlanmış; yalnızca taslak sınav değiştirilebilir.",
        [ErrorCodes.DurationRequired] = "Sınav süresini dakika olarak girin.",
        [ErrorCodes.InvalidTimeWindow] = "Başlangıç bitişten önce, bitiş de gelecekte bir zaman olmalıdır.",
        [ErrorCodes.ClassRequired] = "Hedef şubeyi seçin.",
        [ErrorCodes.ClassHasNoStudents] = "Seçilen şubede öğrenci yok. Önce şubeye öğrenci ekleyin.",
        [ErrorCodes.ExamHasNoQuestions] = "Yayınlamak için sınava en az bir soru ekleyin.",
        [ErrorCodes.ExamHasDeletedQuestion] = "Sınavda bankadan silinmiş bir soru var. Soruyu listeden çıkarın.",
        [ErrorCodes.DuplicateQuestion] = "Bir soru sınava bir kez eklenebilir.",
        [ErrorCodes.DuplicateOrderNo] = "Soru sıraları benzersiz olmalıdır.",
        [ErrorCodes.NotInClass] = "Bu sınav sizin şubenize açık değil.",
        [ErrorCodes.ExamNotOpen] = "Sınav henüz başlamadı.",
        [ErrorCodes.ExamClosed] = "Sınav süresi doldu.",
        [ErrorCodes.AlreadySubmitted] = "Bu sınavı zaten teslim ettiniz.",
        [ErrorCodes.NotOwner] = "Bu gönderim size ait değil.",
        [ApiException.ConnectionFailed] = "Sunucuya ulaşılamadı. Sunucunun açık olduğunu, adresi ve ağ bağlantınızı kontrol edin; güvenlik duvarı engelliyor olabilir."
    };

    public static string For(string? code, string? serverTitle = null) =>
        code is not null && Messages.TryGetValue(code, out var message)
            ? message
            : serverTitle ?? "Beklenmeyen bir hata oluştu.";
}
