# Dragonfire.Torch

**Generate a typed, production-ready C# HTTP client from a Postman v2.1 collection or an HTTP Archive (HAR) file — in one command.**

Torch reads your collection, infers C# types from JSON example bodies, and writes a complete client library with a clean interface, per-call hooks, pluggable signing, a global error handler, and a `Microsoft.Extensions.DependencyInjection` registration helper.

> **Part of the Dragonfire suite.** See [`outboxnet/Dragonfire`](https://github.com/outboxnet/Dragonfire) for the full set of packages.

---

## Install

```bash
dotnet tool install -g Dragonfire.Torch
```

---

## Quick start

```bash
# From a Postman v2.1 collection
dragonfire-torch \
  --input     my-api.postman_collection.json \
  --output    src/MyApi.Client \
  --namespace MyCompany.MyApi.Client \
  --client-name MyApi

# From a HAR archive (browser DevTools → Network → Save all as HAR)
dragonfire-torch \
  --input     capture.har \
  --output    src/MyApi.Client \
  --namespace MyCompany.MyApi.Client \
  --client-name MyApi
```

Both produce the same output structure. The input format is detected automatically from the file extension.

---

## Input formats

| Format | Extension | How to export |
|---|---|---|
| Postman v2.1 collection | `.json` | Postman → Export → Collection v2.1 |
| HTTP Archive 1.2 | `.har` | Chrome/Firefox DevTools → Network → Save all as HAR |

When ingesting a HAR file, Torch:

- picks the base URL from the first entry's origin
- skips cross-origin entries with a warning
- turns UUID-like, all-digit, and opaque path segments into `{paramId}` path-template variables
- deduplicates entries by `(method, path-template)` — first occurrence wins

> **Tip:** Use [Dragonfire.Spark](../Dragonfire.Spark/) (`dragonfire-spark`) to convert a HAR or Charles Proxy export to a clean Postman collection first — this lets you rename operations and organise them into folders before running Torch.

---

## CLI reference

| Flag | Short | Default | Description |
|---|---|---|---|
| `--input` | `-i` | required | Postman `.json` or `.har` file |
| `--output` | `-o` | required | Directory to write the generated project |
| `--namespace` | `-n` | required | Root namespace (e.g. `Acme.BillingClient`) |
| `--client-name` | `-c` | required | Class-name prefix — `Billing` → `BillingClient`, `IBillingClient` |
| `--target-framework` | `-t` | `net8.0` | TFM for the generated `.csproj` |
| `--base-url` | | auto-detected | Override the base URL |
| `--response-examples` | | — | JSON file mapping operation names to override response bodies |
| `--clean` | | `false` | Delete the output directory before writing |
| `--dry-run` | | `false` | Print the plan without writing files |
| `--floats-as-double` | | `false` | Use `double` instead of `decimal` for JSON numbers |

---

## Generated files

For `--namespace Acme.BillingClient --client-name Billing`:

```
Acme.BillingClient/
├── Acme.BillingClient.csproj                  # net8.0, Microsoft.Extensions.* refs
├── IBillingClient.cs                          # typed interface — one method per operation
├── BillingClient.cs                           # HttpClient-backed implementation
├── BillingClientOptions.cs                    # BaseUrl, Timeout, common headers
├── BillingLoggingOptions.cs                   # per-level log toggles
├── BillingServiceCollectionExtensions.cs      # AddBillingClient(...)
├── ApiResponse.cs                             # StatusCode, Body, Headers, Elapsed, RawBody
├── Endpoints.cs                               # const string per URL path
├── Constants.cs                               # header / media-type constants
├── IBillingRequestSigner.cs                   # signing contract + NoOp default
├── IBillingHttpLogger.cs                      # logging contract + default structured logger
├── IBillingErrorHandler.cs                    # error-handler contract + NoOp default
└── Models/
    ├── CreateTenantRequest.cs
    ├── CreateTenantResponse.cs
    └── ...                                    # one file per inferred type
```

---

## Interface — one method per operation

Every request in the collection becomes a strongly-typed async method:

```csharp
Task<ApiResponse<CreateTenantResponse>> CreateTenantAsync(
    CreateTenantRequest body,
    Action<HttpResponseMessage>? onResponse = null,
    IBillingRequestSigner?       signerOverride = null,
    CancellationToken            cancellationToken = default);

Task<ApiResponse<GetTenantResponse>> GetTenantAsync(
    string id,                            // path parameter
    Action<HttpResponseMessage>? onResponse = null,
    IBillingRequestSigner?       signerOverride = null,
    CancellationToken            cancellationToken = default);

Task<ApiResponse<ListTenantsResponse>> ListTenantsAsync(
    string? page,                         // query parameters
    string? pageSize,
    Action<HttpResponseMessage>? onResponse = null,
    IBillingRequestSigner?       signerOverride = null,
    CancellationToken            cancellationToken = default);
```

Path parameters, query parameters, and request bodies are all first-class method arguments. There is also a generic escape hatch for ad-hoc calls:

```csharp
Task<ApiResponse<TResponse>> SendJsonAsync<TRequest, TResponse>(
    HttpMethod method, string path, TRequest? body, ...);
```

---

## Pluggable signing — `IBillingRequestSigner`

Implement the interface to sign every request (HMAC, AWS SigV4, OAuth 1.0a, or any custom scheme). The signer runs *after* the request body is attached, so HMAC-over-payload schemes work correctly.

```csharp
public interface IBillingRequestSigner
{
    Task SignAsync(HttpRequestMessage request, string operationName, CancellationToken ct);
}
```

**Global** — register once and it applies to all calls:

```csharp
services.AddSingleton<IBillingRequestSigner, MyHmacSigner>();
```

**Per-call override** — pass a different signer to a specific method without changing the global configuration:

```csharp
await client.GetTenantAsync(id, signerOverride: adminSigner);
```

---

## Global error handler — `IBillingErrorHandler`

Invoked automatically whenever a call returns a non-2xx status or throws at the transport layer. Use it for centralised logging, metrics, alerting, or exception translation.

```csharp
public interface IBillingErrorHandler
{
    Task HandleAsync(BillingErrorContext context, CancellationToken ct = default);
}
```

`BillingErrorContext` carries: `OperationName`, `StatusCode`, `RawBody`, `ErrorCode`, `ErrorMessage`, `Headers`, `Elapsed`, `Exception`.

```csharp
services.AddSingleton<IBillingErrorHandler, DatadogErrorReporter>();
```

---

## Per-call response hook — `onResponse`

Capture response headers, cookies, or timing data on individual calls without touching the global configuration:

```csharp
string? sessionCookie = null;

await client.ExchangeTokenAsync(
    new ExchangeTokenRequest { ... },
    onResponse: resp =>
    {
        sessionCookie = resp.Headers
            .GetValues("Set-Cookie")
            .FirstOrDefault();
    });
```

---

## `ApiResponse<T>` — rich result wrapper

```csharp
public sealed class ApiResponse<T>
{
    public int      StatusCode { get; init; }
    public bool     IsSuccess  => StatusCode is >= 200 and < 300;
    public T?       Body       { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; }
    public TimeSpan Elapsed    { get; init; }
    public string?  RawBody    { get; init; }
    public string?  ErrorCode  { get; init; }
    public string?  ErrorMessage { get; init; }
}
```

No exceptions on non-2xx responses. Transport-level exceptions surface as `ApiResponse<T> { ErrorCode = "transport.error" }`. Only `OperationCanceledException` propagates.

---

## Dependency injection

```csharp
builder.Services.AddBillingClient(
    configureClient: opt =>
    {
        opt.BaseUrl = "https://api.acme.com";
        opt.Timeout = TimeSpan.FromSeconds(30);
    },
    configureLogging: log =>
    {
        log.LogRequests  = true;
        log.LogResponses = true;
    });

// Optional — no-ops are registered by default via TryAddSingleton:
builder.Services.AddSingleton<IBillingRequestSigner, HmacSigner>();
builder.Services.AddSingleton<IBillingErrorHandler,  DatadogErrorHandler>();
```

---

## Type inference

Torch inspects the JSON body of the first saved response example for each operation and infers C# `record` types:

- JSON objects → `record` with `{ get; init; }` properties
- Nested objects → separate named `record` types
- Arrays → `List<T>`
- ISO-8601 strings → `DateTimeOffset`; UUID strings → `Guid`
- Non-integer numbers → `decimal` (override with `--floats-as-double`)
- `null` or absent fields → nullable type
- Shared shapes across multiple operations are de-duplicated
- Common properties on sibling types are promoted into a generated `abstract` base class

Override inferred types via `--response-examples`:

```json
{
  "CreateTenant": "{ \"id\": \"uuid\", \"plan\": \"enterprise\" }"
}
```

---

## Form bodies

URL-encoded and multipart form bodies generate a dedicated DTO instead of a raw string:

```csharp
// urlencoded
public record ExchangeTokenRequest
{
    public string? GrantType    { get; init; }
    public string? ClientId     { get; init; }
    public string? ClientSecret { get; init; }
}

// multipart — file fields use Stream?
public record UploadReceiptRequest
{
    public string? InvoiceId { get; init; }
    public Stream? File      { get; init; }
}
```

---

## Workflow with Dragonfire.Spark

```bash
# Step 1 — convert HAR or Charles Proxy export to a clean Postman collection
dragonfire-spark \
  --input  capture.har \
  --output my-api.postman_collection.json \
  --name   "My API"

# Step 2 — generate the typed client
dragonfire-torch \
  --input     my-api.postman_collection.json \
  --output    src/MyApi.Client \
  --namespace MyCompany.MyApi.Client \
  --client-name MyApi
```

---

## NuGet packages

| Package | Description |
|---|---|
| `Dragonfire.Torch` | The `dragonfire-torch` global tool |
| `Dragonfire.Torch.Core` | Core engine — embed in your own build pipeline |
