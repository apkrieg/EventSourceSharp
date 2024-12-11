using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourceSharp;

public class EventSourceClient(TimeSpan? retryInterval = null, int maxRetries = 5) : IEventSourceClient
{
    private readonly HttpClient _httpClient = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private TimeSpan _retryInterval = retryInterval ?? TimeSpan.FromSeconds(3);
    private bool _running;
    private string? _lastEventId;

    public event EventHandler? OnConnect;
    public event EventHandler? OnDisconnect;
    public event EventHandler<Exception>? OnError;
    public event EventHandler<ServerSentEventArgs>? OnMessage;

    public async Task ConnectAsync(Uri url, CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            var onError = OnError;
            onError?.Invoke(this, new EventSourceException("The client is already connected."));
            return;
        }

        if (cancellationToken == default)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            cancellationToken = _cancellationTokenSource.Token;
        }

        _running = true;

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var retryCount = 0;

        while (_running && !cancellationToken.IsCancellationRequested)
        {
            if (retryCount >= maxRetries)
            {
                var onError = OnError;
                onError?.Invoke(this, new EventSourceException("The maximum number of retries has been reached."));
                break;
            }

            try
            {
                request.Headers.Remove("Last-Event-ID");
                if (_lastEventId != null)
                {
                    request.Headers.Add("Last-Event-ID", _lastEventId);
                }

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

                retryCount = 0;
                var onConnect = OnConnect;
                onConnect?.Invoke(this, EventArgs.Empty);

                await ProcessEventStreamAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var exception = new EventSourceException("A possible network error occured.", e);

                var onError = OnError;
                onError?.Invoke(this, exception);

                await Task.Delay(_retryInterval, cancellationToken).ConfigureAwait(false);
            }

            retryCount++;
        }
    }

    public async Task ProcessEventStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, false);

        var currentEvent = new ServerSentEventArgs();
        var dataBuffer = new StringBuilder();

        while (_running && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(line))
            {
                _lastEventId = currentEvent.Id switch
                {
                    null => _lastEventId,
                    "" => null,
                    _ => currentEvent.Id,
                };

                if (dataBuffer.Length > 0)
                {
                    if (dataBuffer[dataBuffer.Length - 1] == '\n')
                    {
                        dataBuffer.Remove(dataBuffer.Length - 1, 1);
                    }

                    currentEvent.Data = dataBuffer.ToString();

                    var onMessage = OnMessage;
                    onMessage?.Invoke(this, currentEvent);

                    dataBuffer.Clear();
                }

                currentEvent = new ServerSentEventArgs();
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
                    currentEvent.Id = string.IsNullOrWhiteSpace(value) ? string.Empty : value;
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
        onDisconnect?.Invoke(this, EventArgs.Empty);
    }
}
