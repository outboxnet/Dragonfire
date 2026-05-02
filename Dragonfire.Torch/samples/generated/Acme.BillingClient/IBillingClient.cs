using Acme.BillingClient.Models;

namespace Acme.BillingClient;

public interface IBillingClient
{
    Task<ApiResponse<CreateTenantResponse>> CreateTenantAsync(
        CreateTenantRequest body,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<GetTenantResponse>> GetTenantAsync(
        string id,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<ListTenantsResponse>> ListTenantsAsync(
        string? page,
        string? pageSize,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<DeleteTenantResponse>> DeleteTenantAsync(
        string id,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<CreateInvoiceResponse>> CreateInvoiceAsync(
        string id,
        CreateInvoiceRequest body,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<ExchangeTokenResponse>> ExchangeTokenAsync(
        ExchangeTokenRequest body,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<UploadReceiptResponse>> UploadReceiptAsync(
        string id,
        UploadReceiptRequest body,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generic JSON helper - escape hatch for ad-hoc calls that bypass the
    /// strongly-typed surface. Honours common headers, the request signer,
    /// the error handler, and per-call hooks.
    /// </summary>
    Task<ApiResponse<TResponse>> SendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest? body,
        IDictionary<string, string>? extraHeaders = null,
        string? operationName = null,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default);
}
