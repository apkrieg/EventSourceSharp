namespace EventSourceSharp;

public record ServerSentEvent
{
    public string? Id { get; set; }
    public string Event { get; set; } = "message";
    public string? Data { get; set; }
}
