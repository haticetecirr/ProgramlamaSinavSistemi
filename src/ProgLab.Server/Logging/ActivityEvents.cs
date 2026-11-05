namespace ProgLab.Server.Logging;

/// <summary>activity_logs.event_type değerleri. Sınav olayları ilgili aşamalarda eklenir.</summary>
public static class ActivityEvents
{
    public const string Login = "LOGIN";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string FirstLogin = "FIRST_LOGIN";
}
