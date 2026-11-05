using System.Collections.Concurrent;
using System.Diagnostics;
using ProgLab.Client.Core;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;

namespace ProgLab.Server.Tests;

/// <summary>Kayıt kuyruğu: VT-24'ün otomatik kısmı. Ağ davranışı sahte gönderici ile elle yönetilir.</summary>
public sealed class AnswerSaveQueueTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly ConcurrentQueue<(int Id, string? Text)> _sent = new();
    private volatile bool _offline;
    private volatile string? _rejectCode;

    private Task Send(int id, SaveAnswerRequest answer, CancellationToken ct)
    {
        if (_offline)
        {
            throw new ApiException(ApiException.ConnectionFailed, "Ağ yok");
        }

        if (_rejectCode is { } code)
        {
            throw new ApiException(code, "Reddedildi", 409);
        }

        _sent.Enqueue((id, answer.TextAnswer));
        return Task.CompletedTask;
    }

    private AnswerSaveQueue NewQueue() => new(Send, Debounce, Retry);

    private static SaveAnswerRequest Text(string text) => new(null, text);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < Timeout, "Beklenen durum zamanında oluşmadı.");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task Answer_IsSentAfterDebounce_AndStateBecomesSaved()
    {
        await using var queue = NewQueue();
        Assert.Equal(AnswerSaveState.Saved, queue.State);

        queue.Enqueue(1, Text("a"));
        Assert.Equal(AnswerSaveState.Saving, queue.State);

        await WaitUntilAsync(() => queue.State == AnswerSaveState.Saved);
        Assert.Equal((1, "a"), Assert.Single(_sent));
    }

    [Fact]
    public async Task RapidChanges_SendOnlyTheLastValue()
    {
        await using var queue = new AnswerSaveQueue(Send, TimeSpan.FromMilliseconds(300), Retry);

        foreach (var value in new[] { "y", "yı", "yığ", "yığın" })
        {
            queue.Enqueue(1, Text(value));
        }

        await WaitUntilAsync(() => queue.State == AnswerSaveState.Saved);
        Assert.Equal((1, "yığın"), Assert.Single(_sent));
    }

    [Fact]
    public async Task DifferentQuestions_AreAllSent()
    {
        await using var queue = NewQueue();

        queue.Enqueue(1, Text("bir"));
        queue.Enqueue(2, Text("iki"));
        queue.Enqueue(3, Text("üç"));

        await WaitUntilAsync(() => queue.State == AnswerSaveState.Saved);
        Assert.Equal(3, _sent.Count);
    }

    /// <summary>VT-24: ağ kopukken verilen cevap kaybolmaz, bağlantı gelince kendiliğinden gider.</summary>
    [Fact]
    public async Task VT24_WhenOffline_AnswersWait_AndAreSentWhenConnectionReturns()
    {
        await using var queue = NewQueue();
        _offline = true;

        queue.Enqueue(1, Text("kopukken yazıldı"));
        await WaitUntilAsync(() => queue.State == AnswerSaveState.Offline);
        Assert.Empty(_sent);
        Assert.Equal(1, queue.PendingCount);

        // Kopukken değişen cevabın son hâli gider.
        queue.Enqueue(1, Text("kopukken değişti"));
        queue.Enqueue(2, Text("ikinci soru"));
        await Task.Delay(100);
        Assert.Equal(AnswerSaveState.Offline, queue.State);
        Assert.Empty(_sent);

        _offline = false;

        await WaitUntilAsync(() => queue.State == AnswerSaveState.Saved);
        Assert.Equal(0, queue.PendingCount);
        Assert.Contains((1, "kopukken değişti"), _sent);
        Assert.Contains((2, "ikinci soru"), _sent);
        Assert.DoesNotContain((1, "kopukken yazıldı"), _sent);
    }

    [Fact]
    public async Task Flush_SendsImmediately_WithoutWaitingDebounce()
    {
        await using var queue = new AnswerSaveQueue(Send, TimeSpan.FromMinutes(5), Retry);
        queue.Enqueue(1, Text("hemen"));
        Assert.Empty(_sent);

        Assert.True(await queue.FlushAsync().WaitAsync(Timeout));

        Assert.Equal((1, "hemen"), Assert.Single(_sent));
    }

    [Fact]
    public async Task Flush_WithNothingPending_ReturnsTrueImmediately()
    {
        await using var queue = NewQueue();

        Assert.True(await queue.FlushAsync());
    }

    [Fact]
    public async Task Flush_WhenOffline_ReturnsFalse_AndKeepsAnswers()
    {
        await using var queue = NewQueue();
        _offline = true;
        queue.Enqueue(1, Text("bekliyor"));

        Assert.False(await queue.FlushAsync().WaitAsync(Timeout));
        Assert.Equal(1, queue.PendingCount);

        _offline = false;
        await WaitUntilAsync(() => queue.PendingCount == 0);
        Assert.Contains((1, "bekliyor"), _sent);
    }

    [Fact]
    public async Task ServerRejection_IsNotRetried_AndIsReported()
    {
        await using var queue = NewQueue();
        ApiException? rejected = null;
        queue.Rejected += ex => rejected = ex;
        _rejectCode = ErrorCodes.ExamClosed;

        queue.Enqueue(1, Text("geç kaldı"));

        await WaitUntilAsync(() => rejected is not null);
        Assert.Equal(ErrorCodes.ExamClosed, rejected!.Code);
        Assert.Equal(0, queue.PendingCount);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task ChangeDuringSend_IsNotLost()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = new ConcurrentQueue<string?>();
        await using var queue = new AnswerSaveQueue(
            async (_, answer, _) =>
            {
                values.Enqueue(answer.TextAnswer);
                if (values.Count == 1)
                {
                    entered.TrySetResult();
                    await release.Task;
                }
            },
            TimeSpan.FromMilliseconds(5),
            Retry);

        queue.Enqueue(1, Text("ilk"));
        await entered.Task.WaitAsync(Timeout);

        queue.Enqueue(1, Text("gönderim sırasında"));
        release.SetResult();

        await WaitUntilAsync(() => queue.State == AnswerSaveState.Saved && queue.PendingCount == 0);
        Assert.Equal(["ilk", "gönderim sırasında"], values);
    }
}
