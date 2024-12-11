using System;

namespace EventSourceSharp;

public class ServerSentEventArgs : EventArgs
{
    public string? Id { get; set; }
    public string Event { get; set; } = "message";
    public string? Data { get; set; }
}
