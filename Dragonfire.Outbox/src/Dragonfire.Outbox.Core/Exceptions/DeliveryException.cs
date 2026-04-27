namespace Dragonfire.Outbox.Exceptions;

public class DeliveryException : OutboxException
{
    public int? HttpStatusCode { get; }

    public DeliveryException(string message, int? httpStatusCode = null)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
    }

    public DeliveryException(string message, Exception innerException, int? httpStatusCode = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
    }
}
