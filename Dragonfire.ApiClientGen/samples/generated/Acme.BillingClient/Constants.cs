namespace Acme.BillingClient;

/// <summary>
/// Shared header / content-type names used across the generated client.
/// Reference these constants from custom signers or loggers to stay in sync
/// with the client.
/// </summary>
public static class Constants
{
    public static class Headers
    {
        public const string ApiVersion = "X-Api-Version";
        public const string ContentType = "Content-Type";
    }

    public static class ContentTypes
    {
        public const string Json = "application/json";
    }
}
