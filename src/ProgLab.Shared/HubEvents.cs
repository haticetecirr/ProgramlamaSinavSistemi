namespace ProgLab.Shared;

/// <summary>
/// SignalR hub'ının (/hubs/exam) sunucudan istemciye ittiği olayların adları.
/// Kural: hub yalnızca bildirim iter; iş komutları REST'ten gelir.
/// </summary>
public static class HubEvents
{
    public const string HubPath = "/hubs/exam";

    public const string ExamPublished = nameof(ExamPublished);
    public const string ExamEnded = nameof(ExamEnded);
    public const string TimeSync = nameof(TimeSync);
    public const string StudentConnected = nameof(StudentConnected);
    public const string StudentDisconnected = nameof(StudentDisconnected);
    public const string AnswerProgress = nameof(AnswerProgress);
    public const string SubmissionReceived = nameof(SubmissionReceived);
    public const string SecondDeviceWarning = nameof(SecondDeviceWarning);

    /// <summary>
    /// Dokümanda yok, Aşama 6'da eklendi. Bağlantı kurulduğu an gruplara ekleme henüz bitmemiş olabilir;
    /// sunucu bunu ekleme bitince gönderir. İstemci listesini bundan sonra tazeler, böylece arada yayınlanan sınav kaçmaz.
    /// </summary>
    public const string Ready = nameof(Ready);
}
