namespace EventSourceSharp;

public class ServerSentEvent
{
    public string Id { get; set; } = string.Empty;
    public string Event { get; set; } = "message";
    public string Data { get; set; } = string.Empty;
}
