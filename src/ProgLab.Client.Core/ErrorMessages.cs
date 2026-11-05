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
        [ApiException.ConnectionFailed] = "Sunucuya ulaşılamadı. Sunucunun açık olduğunu ve ağ bağlantınızı kontrol edin."
    };

    public static string For(string? code, string? serverTitle = null) =>
        code is not null && Messages.TryGetValue(code, out var message)
            ? message
            : serverTitle ?? "Beklenmeyen bir hata oluştu.";
}
