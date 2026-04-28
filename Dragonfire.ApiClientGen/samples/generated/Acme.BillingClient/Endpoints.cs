namespace Acme.BillingClient;

public static class Endpoints
{
    public const string CreateTenant = "/tenants";
    public const string GetTenant = "/tenants/{id}";
    public const string ListTenants = "/tenants";
    public const string DeleteTenant = "/tenants/{id}";
    public const string CreateInvoice = "/tenants/{id}/invoices";
    public const string ExchangeToken = "/oauth/token";
    public const string UploadReceipt = "/tenants/{id}/receipts";
}
