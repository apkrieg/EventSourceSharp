using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourceSharp;

public interface IEventSourceClient
{
    event EventHandler? OnConnect;
    event EventHandler? OnDisconnect;
    event EventHandler<Exception>? OnError;
    event EventHandler<ServerSentEventArgs>? OnMessage;

    Task ConnectAsync(Uri url, CancellationToken cancellationToken = default);
    Task ProcessEventStreamAsync(Stream stream, CancellationToken cancellationToken);
    void Disconnect();
}
