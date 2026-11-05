using Microsoft.AspNetCore.Http.Connections;
using ProgLab.Client.Core;

namespace ProgLab.Server.Tests.Infrastructure;

/// <summary>Bellekteki sunucuya gerçek uygulamadaki gibi WebSocket ile bağlanan hub istemcisi.</summary>
public static class TestHubConnection
{
    public static ExamHubClient Create(ProgLabServerFactory server, TokenStore tokens) =>
        new(new Uri("http://localhost/"), tokens, options =>
        {
            options.SkipNegotiation = true;
            options.Transports = HttpTransportType.WebSockets;
            options.WebSocketFactory = async (context, ct) =>
            {
                var client = server.Server.CreateWebSocketClient();
                client.ConfigureRequest = request =>
                {
                    if (tokens.Token is { } token)
                    {
                        request.Headers.Authorization = $"Bearer {token}";
                    }
                };
                return await client.ConnectAsync(context.Uri, ct);
            };
        });
}
