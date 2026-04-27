namespace OutboxNet.Exceptions;

public class OutboxException : Exception
{
    public OutboxException(string message) : base(message) { }
    public OutboxException(string message, Exception innerException) : base(message, innerException) { }
}
