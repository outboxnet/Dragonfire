namespace Acme.BillingClient;

public interface IBillingRequestSigner
{
    Task SignAsync(HttpRequestMessage request, string operationName, CancellationToken cancellationToken);
}

public sealed class NoOpBillingRequestSigner : IBillingRequestSigner
{
    public Task SignAsync(HttpRequestMessage request, string operationName, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
