using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourceSharp;

public interface IEventSourceClient
{
    event Action? OnConnect;
    event Action? OnDisconnect;
    event Action<ServerSentEvent>? OnMessage;

    Task ConnectAsync(Uri url);
    Task ConnectAsync(Uri url, CancellationToken cancellationToken);
    Task ProcessEventStream(Stream stream, CancellationToken cancellationToken);
    void Disconnect();
}
