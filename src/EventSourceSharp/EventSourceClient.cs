using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourceSharp;

public class EventSourceClient : IEventSourceClient
{
    private readonly HttpClient _httpClient = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private TimeSpan _retryInterval = TimeSpan.FromSeconds(3);
    private bool _running;
    private string _lastEventId = string.Empty;

    public event Action? OnConnect;
    public event Action? OnDisconnect;
    public event Action<Exception>? OnError;
    public event Action<ServerSentEvent>? OnMessage;

    public Task ConnectAsync(Uri url)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        return ConnectAsync(url, _cancellationTokenSource.Token);
    }

    public async Task ConnectAsync(Uri url, CancellationToken cancellationToken)
    {
        if (_running)
        {
            var onError = OnError;
            onError?.Invoke(new EventSourceException("The client is already connected."));
            return;
        }

        _running = true;

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        while (_running && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                request.Headers.Remove("Last-Event-ID");
                if (_lastEventId != string.Empty)
                {
                    request.Headers.Add("Last-Event-ID", _lastEventId);
                }

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                var onConnect = OnConnect;
                onConnect?.Invoke();

                var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await ProcessEventStreamAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var exception = new EventSourceException("There was an error while connecting to the event source.", e);

                var onError = OnError;
                onError?.Invoke(exception);

                await Task.Delay(_retryInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task ProcessEventStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, false);

        var currentEvent = new ServerSentEvent();
        string? idBuffer = null;
        var dataBuffer = new StringBuilder();

        while (_running && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(line))
            {
                switch (idBuffer)
                {
                    case null:
                        break;
                    case "":
                        _lastEventId = string.Empty;
                        break;
                    default:
                        _lastEventId = idBuffer;
                        currentEvent.Id = idBuffer;
                        idBuffer = null;
                        break;
                }

                if (dataBuffer.Length > 0)
                {
                    if (dataBuffer[dataBuffer.Length - 1] == '\n')
                    {
                        dataBuffer.Remove(dataBuffer.Length - 1, 1);
                    }

                    currentEvent.Data = dataBuffer.ToString();

                    var onMessage = OnMessage;
                    onMessage?.Invoke(currentEvent);

                    dataBuffer.Clear();
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

                    if (value.Length > 0 && value[0] == ' ')
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
                    dataBuffer.Append(value);
                    dataBuffer.Append('\n');
                    break;
                case "id":
                    idBuffer = string.IsNullOrWhiteSpace(value) ? string.Empty : value;
                    break;
                case "retry":
                    if (int.TryParse(value, out var retry))
                    {
                        _retryInterval = TimeSpan.FromMilliseconds(Math.Max(retry, 0));
                    }

                    break;
            }
        }
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
