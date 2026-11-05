namespace ProgLab.Shared;

/// <summary>
/// API hata cevaplarındaki "code" alanının değerleri (tasarım dokümanı: "Hata kodları").
/// Sunucu bu kodları üretir, istemci bunları Türkçe mesaja çevirir.
/// </summary>
public static class ErrorCodes
{
    // Kimlik ve giriş
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string TokenExpired = "TOKEN_EXPIRED";
    public const string TokenInvalid = "TOKEN_INVALID";
    public const string WrongAppRole = "WRONG_APP_ROLE";
    public const string AccountDisabled = "ACCOUNT_DISABLED";
    public const string PasswordChangeRequired = "PASSWORD_CHANGE_REQUIRED";
    public const string FirstLoginRequired = "FIRST_LOGIN_REQUIRED";
    public const string AlreadyVerified = "ALREADY_VERIFIED";
    public const string RateLimit = "RATE_LIMIT";

    // Öğrenci yetkisi
    public const string NotInClass = "NOT_IN_CLASS";
    public const string NotOwner = "NOT_OWNER";

    // Sınav durumu ve zaman penceresi
    public const string ExamNotDraft = "EXAM_NOT_DRAFT";
    public const string InvalidState = "INVALID_STATE";
    public const string ExamNotOpen = "EXAM_NOT_OPEN";
    public const string ExamClosed = "EXAM_CLOSED";
    public const string AlreadySubmitted = "ALREADY_SUBMITTED";

    // Çakışmalar
    public const string CategoryNotEmpty = "CATEGORY_NOT_EMPTY";
    public const string CategoryNameExists = "CATEGORY_NAME_EXISTS";
    public const string DuplicateQuestion = "DUPLICATE_QUESTION";

    // Soru doğrulama
    public const string McqOptionCount = "MCQ_OPTION_COUNT";
    public const string McqNoCorrectOption = "MCQ_NO_CORRECT_OPTION";
    public const string McqMultipleCorrect = "MCQ_MULTIPLE_CORRECT";
    public const string ClassicNoAnswerKey = "CLASSIC_NO_ANSWER_KEY";
    public const string InvalidPoints = "INVALID_POINTS";

    // Sınav doğrulama
    public const string DurationRequired = "DURATION_REQUIRED";
    public const string InvalidTimeWindow = "INVALID_TIME_WINDOW";
    public const string ClassRequired = "CLASS_REQUIRED";
    public const string ExamHasNoQuestions = "EXAM_HAS_NO_QUESTIONS";
    public const string ClassHasNoStudents = "CLASS_HAS_NO_STUDENTS";

    // Cevap doğrulama
    public const string OptionNotInQuestion = "OPTION_NOT_IN_QUESTION";
    public const string AnswerTooLong = "ANSWER_TOO_LONG";

    // Puan düzeltme
    public const string ScoreOutOfRange = "SCORE_OUT_OF_RANGE";
    public const string NotClassicQuestion = "NOT_CLASSIC_QUESTION";

    // Bulunamadı (dokümanın vize özellikleri bölümünde geçer)
    public const string QuestionNotFound = "QUESTION_NOT_FOUND";
    public const string ExamNotFound = "EXAM_NOT_FOUND";
    public const string LogFileNotFound = "LOG_FILE_NOT_FOUND";

    // Dokümanın hata tablosunda karşılığı olmayan durumlar için Aşama 3'te eklendi
    public const string Forbidden = "FORBIDDEN";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string DatabaseUnavailable = "DATABASE_UNAVAILABLE";
    public const string PasswordTooShort = "PASSWORD_TOO_SHORT";
    public const string UsernameExists = "USERNAME_EXISTS";
    public const string EmailExists = "EMAIL_EXISTS";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string ClassNotFound = "CLASS_NOT_FOUND";
    public const string ClassNameExists = "CLASS_NAME_EXISTS";
    public const string ClassHasExams = "CLASS_HAS_EXAMS";
    public const string InvalidStudent = "INVALID_STUDENT";

    // Dokümanın hata tablosunda karşılığı olmayan durumlar için Aşama 4'te eklendi
    public const string UnsupportedQuestionType = "UNSUPPORTED_QUESTION_TYPE";
    public const string DuplicateAnswerKey = "DUPLICATE_ANSWER_KEY";
    public const string CategoryNotFound = "CATEGORY_NOT_FOUND";
    public const string CategoryCycle = "CATEGORY_CYCLE";
}
