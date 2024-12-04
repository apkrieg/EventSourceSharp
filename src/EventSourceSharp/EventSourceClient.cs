using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourceSharp;

public class EventSourceClient
{
    private readonly HttpClient _httpClient = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _running;
    private string? _lastEventId;

    public event Action? OnConnect;
    public event Action? OnDisconnect;
    public event Action<ServerSentEvent>? OnMessage;

    public Task ConnectAsync(Uri url)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        return ConnectAsync(url, _cancellationTokenSource.Token);
    }

    public async Task ConnectAsync(Uri url, CancellationToken cancellationToken)
    {
        _running = true;

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (!string.IsNullOrEmpty(_lastEventId))
        {
            request.Headers.Add("Last-Event-ID", _lastEventId);
        }

        var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());

        var onConnect = OnConnect;
        onConnect?.Invoke();

        var currentEvent = new ServerSentEvent();

        while (_running && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentEvent.Id != null)
                {
                    _lastEventId = currentEvent.Id;
                }

                if (currentEvent.Data?.Length > 0)
                {
                    var onMessage = OnMessage;
                    onMessage?.Invoke(currentEvent);
                }

                currentEvent = new ServerSentEvent();
                continue;
            }

            var colonIndex = line.IndexOf(':');

            string field;
            string value;

            switch (colonIndex)
            {
                case 0:
                    continue;
                case -1:
                    field = line;
                    value = string.Empty;
                    break;
                default:
                    field = line.Substring(0, colonIndex);
                    value = line.Substring(colonIndex + 1);

                    if (value.StartsWith(" "))
                    {
                        value = value.Substring(1);
                    }

                    break;
            }

            switch (field)
            {
                case "event":
                    currentEvent.Event = value;
                    break;
                case "data":
                    value += "\n";
                    currentEvent.Data += value;
                    break;
                case "id":
                    currentEvent.Id = value;
                    break;
            }
        }

        Console.WriteLine("No loop");
    }

    public void Disconnect()
    {
        _running = false;
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = null;
        var onDisconnect = OnDisconnect;
        onDisconnect?.Invoke();
    }
}
