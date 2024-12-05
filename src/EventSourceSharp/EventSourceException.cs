using System;

namespace EventSourceSharp;

public class EventSourceException : Exception
{
    public EventSourceException(string message) : base(message)
    {
    }

    public EventSourceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
