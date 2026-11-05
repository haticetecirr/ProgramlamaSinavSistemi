namespace ProgLab.Server.Logging;

/// <summary>activity_logs.event_type değerleri.</summary>
public static class ActivityEvents
{
    public const string Login = "LOGIN";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string FirstLogin = "FIRST_LOGIN";
    public const string ExamPublished = "EXAM_PUBLISHED";
    public const string ExamEnded = "EXAM_ENDED";
    public const string Joined = "JOINED";
    public const string SecondDevice = "SECOND_DEVICE";
    public const string Submitted = "SUBMITTED";
    public const string AutoSubmitted = "AUTO_SUBMITTED";
    public const string SaveRejected = "SAVE_REJECTED";
    public const string SubmitRejected = "SUBMIT_REJECTED";
    public const string ScoreOverridden = "SCORE_OVERRIDDEN";
}
