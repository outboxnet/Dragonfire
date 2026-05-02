using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Acme.BillingClient.Models;
using Microsoft.Extensions.Options;

namespace Acme.BillingClient;

public sealed class BillingClient : IBillingClient
{
    private readonly HttpClient _http;
    private readonly BillingClientOptions _options;
    private readonly IBillingRequestSigner _signer;
    private readonly IBillingHttpLogger _logger;
    private readonly IBillingErrorHandler _errorHandler;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public BillingClient(
        HttpClient http,
        IOptions<BillingClientOptions> options,
        IBillingRequestSigner signer,
        IBillingHttpLogger logger,
        IBillingErrorHandler errorHandler)
    {
        _http         = http;
        _options      = options.Value;
        _signer       = signer;
        _logger       = logger;
        _errorHandler = errorHandler;

        if (_http.BaseAddress is null && !string.IsNullOrEmpty(_options.BaseUrl))
            _http.BaseAddress = new Uri(_options.BaseUrl);
    }

    // --- POST /tenants ---
    public Task<ApiResponse<CreateTenantResponse>> CreateTenantAsync(
        CreateTenantRequest body,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default)
    {
        const string path = Endpoints.CreateTenant;

        HttpContent? content = JsonContent.Create(body, options: _json);

        return SendAsync<CreateTenantResponse>(
            HttpMethod.Post,
            path,
            content,
            extraHeaders: null,
            operationName: nameof(CreateTenantAsync),
            onResponse,
            signerOverride,
            cancellationToken);
    }

    // --- GET /tenants/{id} ---
    public Task<ApiResponse<GetTenantResponse>> GetTenantAsync(
        string id,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default)
    {
        var path = Endpoints.GetTenant;
        path = path.Replace("{id}", Uri.EscapeDataString(id));

        HttpContent? content = null;

        return SendAsync<GetTenantResponse>(
            HttpMethod.Get,
            path,
            content,
            extraHeaders: null,
            operationName: nameof(GetTenantAsync),
            onResponse,
            signerOverride,
            cancellationToken);
    }

    // --- GET /tenants ---
    public Task<ApiResponse<ListTenantsResponse>> ListTenantsAsync(
        string? page,
        string? pageSize,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default)
    {
        var path = Endpoints.ListTenants;
        var qs = new List<string>(2);
        if (page is not null) qs.Add($"page={page}");
        if (pageSize is not null) qs.Add($"pageSize={pageSize}");
        if (qs.Count > 0) path += "?" + string.Join("&", qs);

        HttpContent? content = null;

        return SendAsync<ListTenantsResponse>(
            HttpMethod.Get,
            path,
            content,
            extraHeaders: null,
            operationName: nameof(ListTenantsAsync),
            onResponse,
            signerOverride,
            cancellationToken);
    }

    // --- DELETE /tenants/{id} ---
    public Task<ApiResponse<DeleteTenantResponse>> DeleteTenantAsync(
        string id,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default)
    {
        var path = Endpoints.DeleteTenant;
        path = path.Replace("{id}", Uri.EscapeDataString(id));

        HttpContent? content = null;

        return SendAsync<DeleteTenantResponse>(
            HttpMethod.Delete,
            path,
            content,
            extraHeaders: null,
            operationName: nameof(DeleteTenantAsync),
            onResponse,
            signerOverride,
            cancellationToken);
    }

    // --- POST /tenants/{id}/invoices ---
    public Task<ApiResponse<CreateInvoiceResponse>> CreateInvoiceAsync(
        string id,
        CreateInvoiceRequest body,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default)
    {
        var path = Endpoints.CreateInvoice;
        path = path.Replace("{id}", Uri.EscapeDataString(id));

        HttpContent? content = JsonContent.Create(body, options: _json);

        return SendAsync<CreateInvoiceResponse>(
            HttpMethod.Post,
            path,
            content,
            extraHeaders: null,
            operationName: nameof(CreateInvoiceAsync),
            onResponse,
            signerOverride,
            cancellationToken);
    }

    // --- POST /oauth/token ---
    public Task<ApiResponse<ExchangeTokenResponse>> ExchangeTokenAsync(
        ExchangeTokenRequest body,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default)
    {
        const string path = Endpoints.ExchangeToken;

        var form = new List<KeyValuePair<string, string>>(4);
        form.Add(new KeyValuePair<string, string>("grant_type", body.GrantType));
        form.Add(new KeyValuePair<string, string>("client_id", body.ClientId));
        form.Add(new KeyValuePair<string, string>("client_secret", body.ClientSecret));
        form.Add(new KeyValuePair<string, string>("scope", body.Scope));
        HttpContent? content = new FormUrlEncodedContent(form);

        return SendAsync<ExchangeTokenResponse>(
            HttpMethod.Post,
            path,
            content,
            extraHeaders: null,
            operationName: nameof(ExchangeTokenAsync),
            onResponse,
            signerOverride,
            cancellationToken);
    }

    // --- POST /tenants/{id}/receipts ---
    public Task<ApiResponse<UploadReceiptResponse>> UploadReceiptAsync(
        string id,
        UploadReceiptRequest body,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default)
    {
        var path = Endpoints.UploadReceipt;
        path = path.Replace("{id}", Uri.EscapeDataString(id));

        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(body.TenantId), "tenantId");
        multipart.Add(new StringContent(body.Note), "note");
        if (body.Receipt is not null) multipart.Add(new StreamContent(body.Receipt), "receipt", "receipt");
        HttpContent? content = multipart;

        return SendAsync<UploadReceiptResponse>(
            HttpMethod.Post,
            path,
            content,
            extraHeaders: null,
            operationName: nameof(UploadReceiptAsync),
            onResponse,
            signerOverride,
            cancellationToken);
    }

    // --- Generic JSON escape hatch ---
    public Task<ApiResponse<TResponse>> SendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest? body,
        IDictionary<string, string>? extraHeaders = null,
        string? operationName = null,
        Action<HttpResponseMessage>? onResponse = null,
        IBillingRequestSigner? signerOverride = null,
        CancellationToken cancellationToken = default)
    {
        HttpContent? content = body is null ? null : JsonContent.Create(body, options: _json);
        return SendAsync<TResponse>(
            method,
            path,
            content,
            extraHeaders,
            operationName ?? $"{method} {path}",
            onResponse,
            signerOverride,
            cancellationToken);
    }

    private async Task<ApiResponse<TResponse>> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        HttpContent? content,
        IDictionary<string, string>? extraHeaders,
        string operationName,
        Action<HttpResponseMessage>? onResponse,
        IBillingRequestSigner? signerOverride,
        CancellationToken cancellationToken)
    {
        var op = operationName;
        var sw = Stopwatch.StartNew();

        using var request = new HttpRequestMessage(method, path);

        // 1. Common headers (from options, sourced from the collection at gen time).
        foreach (var kv in _options.CommonHeaders)
            request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

        // 2. Per-call overrides.
        if (extraHeaders is not null)
            foreach (var kv in extraHeaders)
                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

        // 3. Body.
        if (content is not null && method != HttpMethod.Get && method != HttpMethod.Delete)
            request.Content = content;

        // 4. Pluggable signing -- per-call override wins over DI singleton.
        var signer = signerOverride ?? _signer;
        await signer.SignAsync(request, op, cancellationToken).ConfigureAwait(false);

        await _logger.BeforeRequestAsync(request, op, cancellationToken).ConfigureAwait(false);

        HttpResponseMessage? response = null;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            await _logger.AfterResponseAsync(response, op, sw.Elapsed, cancellationToken).ConfigureAwait(false);

            // Per-call inspection hook -- caller can capture headers, cookies, etc.
            // Swallow callback exceptions so user code can't break the response pipeline.
            if (onResponse is not null)
            {
                try { onResponse(response); }
                catch (Exception cbEx) { await _logger.OnErrorAsync(cbEx, op, cancellationToken).ConfigureAwait(false); }
            }

            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var headers = BuildHeaderDictionary(response);

            if (response.IsSuccessStatusCode)
            {
                TResponse? data = default;
                if (typeof(TResponse) == typeof(Unit))
                    data = (TResponse)(object)Unit.Value;
                else if (!string.IsNullOrWhiteSpace(raw))
                    data = JsonSerializer.Deserialize<TResponse>(raw, _json);

                return new ApiResponse<TResponse>
                {
                    IsSuccess  = true,
                    StatusCode = (int)response.StatusCode,
                    Data       = data,
                    RawBody    = raw,
                    Headers    = headers,
                    Elapsed    = sw.Elapsed,
                };
            }

            // Non-2xx -- try to extract { code, message } from a JSON body.
            string? errCode = null, errMsg = null;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("code",    out var c)) errCode = c.GetString();
                    if (doc.RootElement.TryGetProperty("message", out var m)) errMsg  = m.GetString();
                }
            }
            catch { /* not JSON, leave as null */ }

            var failure = new ApiResponse<TResponse>
            {
                IsSuccess    = false,
                StatusCode   = (int)response.StatusCode,
                RawBody      = raw,
                Headers      = headers,
                ErrorCode    = errCode,
                ErrorMessage = errMsg ?? response.ReasonPhrase,
                Elapsed      = sw.Elapsed,
            };

            await _errorHandler.HandleAsync(new BillingErrorContext
            {
                OperationName = op,
                StatusCode    = failure.StatusCode,
                RawBody       = raw,
                ErrorCode     = errCode,
                ErrorMessage  = failure.ErrorMessage,
                Headers       = headers,
                Elapsed       = sw.Elapsed,
            }, cancellationToken).ConfigureAwait(false);

            return failure;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            await _logger.OnErrorAsync(ex, op, cancellationToken).ConfigureAwait(false);

            await _errorHandler.HandleAsync(new BillingErrorContext
            {
                OperationName = op,
                StatusCode    = 0,
                ErrorCode     = "transport.error",
                ErrorMessage  = ex.Message,
                Elapsed       = sw.Elapsed,
                Exception     = ex,
            }, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<TResponse>
            {
                IsSuccess    = false,
                StatusCode   = 0,
                ErrorCode    = "transport.error",
                ErrorMessage = ex.Message,
                Elapsed      = sw.Elapsed,
            };
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static IReadOnlyDictionary<string, string> BuildHeaderDictionary(HttpResponseMessage response)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers)
            dict[h.Key] = string.Join(",", h.Value);
        foreach (var h in response.Content.Headers)
            dict[h.Key] = string.Join(",", h.Value);
        return dict;
    }
}
