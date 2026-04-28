# Dragonfire.ApiClientGen — Plan

A standalone .NET CLI that consumes a Postman collection (v2.1) and emits a typed
C# client library: project file, `IxxxClient` interface, `XxxClient` class
backed by `HttpClient`, request/response models, an `Endpoints` constant table,
a `Constants` class for shared headers, a wrapper response type, and pluggable
seams for logging and request signing.

**Not a Roslyn source generator.** It is a `dotnet tool` that writes `.cs` files
to disk.

---

## 1. Postman collection — what to expect

Postman v2.1 collection JSON has roughly this shape:

```json
{
  "info": {
    "name": "Acme Billing API",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/"
  },
  "variable": [
    { "key": "baseUrl", "value": "https://api.acme.test" }
  ],
  "item": [
    {
      "name": "Create Tenant",
      "request": {
        "method": "POST",
        "header": [
          { "key": "Content-Type",   "value": "application/json" },
          { "key": "X-Api-Version",  "value": "2024-01" }
        ],
        "body": {
          "mode": "raw",
          "raw":  "{\"name\":\"Acme\",\"plan\":\"pro\"}",
          "options": { "raw": { "language": "json" } }
        },
        "url": {
          "raw":  "{{baseUrl}}/tenants",
          "host": [ "{{baseUrl}}" ],
          "path": [ "tenants" ]
        }
      },
      "response": [
        {
          "name": "Created",
          "originalRequest": { "...": "snapshot of request" },
          "status": "Created",
          "code": 201,
          "header": [ { "key": "Content-Type", "value": "application/json" } ],
          "body":   "{\"id\":\"t-1\",\"name\":\"Acme\",\"plan\":\"pro\",\"createdAt\":\"2026-04-29T12:00:00Z\"}"
        }
      ]
    }
  ]
}
```

### 1.1 Things that are always present

- `item[]` — the list of requests (possibly nested through folders, where a
  folder is `{ "name": "...", "item": [ ... ] }` recursively).
- `request.method`, `request.url` (either `raw` string or structured `{host,
  path, query}`), `request.header[]`.
- For requests with bodies: `request.body.mode` is one of
  `raw` (often JSON), `urlencoded`, `formdata`, `file`, `graphql`.

### 1.2 What may or may not be present

- **Saved example responses** (`item.response[]`).
  - Present when the developer explicitly saved an example. Carries `code`,
    `status`, `header[]`, `body` (JSON string), and `originalRequest`.
  - Absent when nobody clicked "Save as example". Many real-world collections
    have zero saved responses.
- **Body for non-JSON requests** — `formdata` / `urlencoded` are common but the
  generator should treat them as a separate code path.
- **Path / query parameters as structured data**.
  - Postman path placeholders show up two ways:
    - `:id` style — `path` array contains the literal `":id"`.
    - `{{id}}` style — `path` array contains `"{{id}}"` (a Postman variable).
  - Query items live in `url.query[]` with `{ key, value, disabled }`. They are
    *not* always present even when the raw URL has a `?…`.
- **Auth** — `auth` block at request, folder, or collection level. We will skip
  auth in v1 and let the user plug a request signer.

### 1.3 What we infer when example responses ARE saved

- Parse the example body as JSON; walk it to a typed schema.
- If multiple examples: pick the first 2xx response as the **primary** response
  type for the operation; emit only that one. (Optionally emit `ErrorXxx`
  classes from non-2xx, but skip in v1 — the wrapper response carries `RawBody`
  for that.)
- The example response gives accurate field names and rough types — but JSON
  is loose: `12.0` may be intended as `decimal` not `double`, ISO date strings
  are strings unless we sniff them. Heuristics in §4.2.

### 1.4 What we do when example responses are NOT saved

Two acceptable behaviours, both supported via a CLI flag:

1. **Stub response DTO.** Emit `{OperationName}Response` with no fields and a
   `// TODO: fill in — no example response in collection` comment. The
   operation method returns `ApiResponse<{OperationName}Response>` and the user
   fills in fields after generation.
2. **Override file.** `--response-examples ./responses.json` lets the user
   provide a JSON dictionary `{ "operationName": { ...example body... } }` that
   the generator consults before falling back to stubs.

For requests with no body (GET/DELETE) we never emit a request DTO — those
operations take individual parameters for path/query.

---

## 2. Generator architecture

A single CLI tool, no Roslyn, no MSBuild integration. The pipeline is:

```
postman.json  ─→  Parse  ─→  IR (operations + types)
                                     │
                                     ├─→  Infer types from JSON examples
                                     ├─→  Promote shared properties to base classes
                                     ├─→  Sanitize + PascalCase names
                                     │
                                     └─→  Emit (templates + Roslyn syntax tree
                                            for trickier files)  ─→  *.cs files
```

### 2.1 Solution layout (the generator itself)

```
Dragonfire.ApiClientGen/
├── src/
│   ├── Dragonfire.ApiClientGen.Cli/              <- the dotnet tool entry point
│   │   └── Program.cs                            <- System.CommandLine root
│   └── Dragonfire.ApiClientGen.Core/
│       ├── Postman/                              <- Postman v2.1 POCOs (System.Text.Json)
│       │   ├── PostmanCollection.cs
│       │   ├── PostmanItem.cs
│       │   ├── PostmanRequest.cs
│       │   └── PostmanResponse.cs
│       ├── Schema/                               <- intermediate representation
│       │   ├── ClientIR.cs                       <- everything the emitter needs
│       │   ├── OperationIR.cs
│       │   ├── ParameterIR.cs
│       │   ├── TypeIR.cs                         <- record-like model description
│       │   └── PropertyIR.cs
│       ├── Inference/
│       │   ├── JsonSchemaInferrer.cs             <- JSON example → TypeIR
│       │   ├── PrimitiveSniffer.cs               <- string→DateTimeOffset/Guid/decimal
│       │   └── BaseClassPromoter.cs              <- finds shared property sets
│       ├── Naming/
│       │   ├── PascalCase.cs
│       │   ├── IdentifierSanitizer.cs            <- strips spaces, $, /, etc.
│       │   └── CollisionResolver.cs
│       ├── Emit/
│       │   ├── CsprojEmitter.cs
│       │   ├── ConstantsEmitter.cs
│       │   ├── EndpointsEmitter.cs
│       │   ├── ModelEmitter.cs
│       │   ├── ClientInterfaceEmitter.cs
│       │   ├── ClientImplementationEmitter.cs
│       │   ├── OptionsEmitter.cs
│       │   ├── LoggingEmitter.cs
│       │   ├── SignerEmitter.cs
│       │   ├── ApiResponseEmitter.cs
│       │   └── DiExtensionsEmitter.cs
│       ├── Templates/                            <- Scriban or raw string templates
│       │   └── *.sbn
│       └── GeneratorPipeline.cs                  <- orchestrates everything
```

### 2.2 Why both templates AND syntax-tree emission?

- Boilerplate files (csproj, Constants, Endpoints, Options) are static enough
  for **Scriban templates** — fastest to author, easy to read.
- The client implementation has per-operation method bodies with conditional
  logic (path params, query, body, signing). Templates work, but a small
  Roslyn `SyntaxFactory`-based emitter for that one file gives us free
  formatting and makes round-trip diffs cleaner. Pick one of the two — defaulting
  to Scriban for v1; promote to Roslyn if formatting becomes painful.

### 2.3 CLI surface

```
dragonfire-apigen \
    --input ./acme-billing.postman_collection.json \
    --output ./generated/Acme.BillingClient \
    --namespace Acme.BillingClient \
    --client-name BillingClient \
    --target-framework net8.0 \
    [--response-examples ./responses.json] \
    [--base-url https://api.acme.test] \
    [--clean]                              # delete output dir before writing
    [--dry-run]                            # print plan, don't write
```

`--client-name BillingClient` controls every `{Placeholder}` in the generated
code: the file `BillingClient.cs`, interface `IBillingClient`, options
`BillingClientOptions`, signer interface `IBillingRequestSigner`, etc.

---

## 3. Intermediate representation

After parsing + inference, we have a flat `ClientIR`:

```csharp
public sealed record ClientIR(
    string Namespace,
    string ClientName,                       // e.g. "Billing" — placeholder
    string BaseUrl,
    IReadOnlyList<HeaderIR> CommonHeaders,   // appear on every request
    IReadOnlyList<TypeIR> Types,             // models, including any base classes
    IReadOnlyList<OperationIR> Operations);

public sealed record OperationIR(
    string Name,                             // PascalCased, "CreateTenant"
    string HttpMethod,                       // GET/POST/...
    string PathTemplate,                     // "/tenants/{id}"
    IReadOnlyList<PathParamIR> PathParams,
    IReadOnlyList<QueryParamIR> QueryParams,
    IReadOnlyList<HeaderIR> ExtraHeaders,    // request-specific only
    TypeIR? RequestBody,                     // null for GET/DELETE
    TypeIR? ResponseBody,                    // null when no example & no override
    int? ExampleStatusCode);                 // 201 etc.
```

`HeaderIR.IsCommon == true` means the same `{key,value}` appears on every
request → enriched into `Constants.Headers` and applied via options, not per
operation.

---

## 4. Generation rules

### 4.1 Naming

- **Operation name** ← `item.name` with these transformations:
  1. Strip leading/trailing whitespace.
  2. Replace runs of non-alphanumeric characters with a single space.
  3. Title-case each word, then concatenate (PascalCase).
  4. If first character is a digit, prepend `Op`.
  5. Append `Async` to method names (interface + class), but NOT to
     `Endpoints.*` constants.
  6. Collisions: append `_2`, `_3`, ….
- **Type names** ← derived from operation name:
  - Request:  `{Operation}Request`
  - Response: `{Operation}Response`
  - Nested objects in JSON: title-cased property name; nested-of-nested: parent
    name as prefix (`Tenant.Address` → `TenantAddress`).

### 4.2 Type inference (JSON example → TypeIR)

For each JSON value:

| JSON shape                          | Inferred C# type        |
|-------------------------------------|-------------------------|
| `null`                              | `string?` (or `object?` if no peers) — see §4.2.1 |
| `true` / `false`                    | `bool`                  |
| Integer in `[-2^31, 2^31)`          | `int`                   |
| Integer outside that range          | `long`                  |
| Float                               | `decimal`               |
| String matching ISO-8601            | `DateTimeOffset`        |
| String matching `^[0-9a-fA-F-]{36}$`| `Guid`                  |
| Other string                        | `string`                |
| Array                               | `List<T>` of element type (unify across elements; if mixed → `List<JsonElement>` and warn) |
| Object                              | New `TypeIR` with properties |

#### 4.2.1 Nullability

- A property is nullable if it is `null` in **any** observed example, OR if
  it is missing from any example body.
- Required when present in every example with non-null value.

User said: **never use `object` or `dynamic`**. We honour that for the happy
path but a JSON array of mixed types has no good C# representation; the fallback
is `List<JsonElement>` with a `// FIXME: heterogeneous array` comment. The
generator emits a non-fatal warning for these cases.

### 4.3 Base class promotion

User requirement: "if request contains same primitive types in all
request[s] or response[s], elevate them into base class."

Algorithm:

1. For each model `M`, compute its set of `(name, type)` primitive pairs.
2. Find the largest property set `S` that appears in **≥3 models AND ≥50% of
   models in the same group** (request-side or response-side).
3. If `|S| ≥ 2`, emit `BaseEntity` (request side: `BaseRequest`) with those
   properties; rewrite the matching models to inherit from it and drop the
   shared props.
4. If both request-side and response-side share an even bigger set across
   both, emit a single shared base. Practically: response-side is where you
   see this most (e.g. `Id`, `CreatedAt`, `UpdatedAt`).

This is a heuristic, not OpenAPI-perfect — generated output will be checked in,
so a one-time hand-edit after generation is fine.

### 4.4 Path parameter handling

- Postman gives placeholders as `:id` or `{{id}}` in `url.path`.
- IR converts both to `{id}` form.
- Endpoints constants store the template verbatim:
  ```csharp
  public const string GetTenant = "/tenants/{id}";
  ```
- The client method does interpolation locally:
  ```csharp
  var path = Endpoints.GetTenant.Replace("{id}", Uri.EscapeDataString(id));
  ```
  (Not `string.Format` — Postman placeholders are *named*, and `Replace` keeps
  the source readable when there are 3+ params.)

### 4.5 Common headers via `IOptions<XxxClientOptions>`

A header is "common" iff it appears on **every** request in the collection.
Its `{key, value}` pair becomes a default in `XxxClientOptions.CommonHeaders`,
and the client enriches every outgoing request with the contents of that
dictionary. The header *key* is exposed as a `const string` on
`Constants.Headers` so other code can reference the same name without typos.

### 4.6 Wrapper response type

```csharp
public sealed class ApiResponse<T>
{
    public bool IsSuccess { get; init; }
    public int StatusCode { get; init; }
    public T? Data { get; init; }
    public string? RawBody { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } = ...;
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Elapsed { get; init; }
}
```

Every operation returns `Task<ApiResponse<T>>`. Operations with no response
body return `Task<ApiResponse<Unit>>` where `Unit` is a generated struct.

### 4.7 Error handling

The client does **not throw** on non-2xx responses. It returns
`ApiResponse<T>` with `IsSuccess=false`, the status code, and an attempt to
parse `code`/`message` out of the JSON error body (configurable later). Only
two things throw out of the client:

- `OperationCanceledException` — propagates user cancellation.
- A transport-level exception caught at the `SendAsync` boundary is *converted*
  into `ApiResponse<T> { IsSuccess=false, ErrorCode="transport.error",
  ErrorMessage=ex.Message }` and the logger's `OnErrorAsync` is invoked. We
  don't rethrow because callers shouldn't need a try/catch around every call —
  they already check `IsSuccess`. (If that's wrong for your use case, a flag
  flips the behaviour to throw.)

### 4.8 Pluggable signing

```csharp
public interface I{Prefix}RequestSigner
{
    Task SignAsync(HttpRequestMessage request, string operationName, CancellationToken ct);
}
public sealed class NoOp{Prefix}RequestSigner : I{Prefix}RequestSigner
{
    public Task SignAsync(...) => Task.CompletedTask;
}
```

Default registration is the no-op. The user supplies their HMAC/JWT/AWS-SigV4
implementation post-generation:

```csharp
services.AddBillingClient();
services.AddSingleton<IBillingRequestSigner, MyHmacSigner>();   // overrides default
```

The client invokes the signer **after** the body is set on the request so
HMACs that hash the payload work correctly.

### 4.9 Pluggable, per-endpoint logging

```csharp
public interface I{Prefix}HttpLogger
{
    Task BeforeRequestAsync(HttpRequestMessage req, string operationName, CancellationToken ct);
    Task AfterResponseAsync(HttpResponseMessage resp, string operationName, TimeSpan elapsed, CancellationToken ct);
    Task OnErrorAsync(Exception ex, string operationName, CancellationToken ct);
}
```

The default `Default{Prefix}HttpLogger` reads
`{Prefix}LoggingOptions.Endpoints[operationName]` to decide level / body
logging / disabled. The user can replace the whole logger to inject custom
redaction:

```csharp
services.AddSingleton<IBillingHttpLogger, MyRedactingLogger>();
```

The redaction itself is *not* generated — the user writes their own logger
that knows which fields are PII.

---

## 5. Output project layout

```
{output}/                           e.g. ./generated/Acme.BillingClient
├── {Namespace}.csproj              Acme.BillingClient.csproj
├── {Prefix}ClientOptions.cs
├── {Prefix}LoggingOptions.cs
├── Constants.cs
├── Endpoints.cs
├── ApiResponse.cs
├── Unit.cs
├── I{Prefix}Client.cs
├── {Prefix}Client.cs
├── I{Prefix}RequestSigner.cs       + NoOp{Prefix}RequestSigner
├── I{Prefix}HttpLogger.cs          + Default{Prefix}HttpLogger
├── {Prefix}ServiceCollectionExtensions.cs
└── Models/
    ├── BaseEntity.cs               (only when promotion fired)
    ├── Tenant.cs
    ├── Invoice.cs
    ├── CreateTenantRequest.cs
    ├── CreateInvoiceRequest.cs
    └── ListTenantsResponse.cs
```

---

## 6. Worked example

### 6.1 Input collection (abbreviated)

5 operations:

| Operation         | Method | Path                       | Body                | Response                                       |
|-------------------|--------|----------------------------|---------------------|------------------------------------------------|
| Create Tenant     | POST   | `/tenants`                 | `{name, plan}`      | `{id, name, plan, createdAt}` (201)            |
| Get Tenant        | GET    | `/tenants/:id`             | —                   | `{id, name, plan, createdAt}` (200)            |
| List Tenants      | GET    | `/tenants?page=&pageSize=` | —                   | `{items:[…], totalCount}` (200)                |
| Delete Tenant     | DELETE | `/tenants/:id`             | —                   | empty (204)                                    |
| Create Invoice    | POST   | `/tenants/:id/invoices`    | `{amount, currency}`| `{id, tenantId, amount, currency, status, createdAt}` (201)|

Common header on every request: `X-Api-Version: 2024-01`.

`Tenant` and `Invoice` both carry `Id` and `CreatedAt` → base class promotion
kicks in (≥2 properties shared by ≥3 models — Tenant, Invoice and the array
elements inside `ListTenantsResponse` count as the same `Tenant` type).

### 6.2 Generated csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Acme.BillingClient</RootNamespace>
    <AssemblyName>Acme.BillingClient</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http"             Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Options"          Version="8.0.2" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.2" />
  </ItemGroup>
</Project>
```

### 6.3 `Constants.cs`

```csharp
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
        public const string ApiVersion     = "X-Api-Version";
        public const string ContentType    = "Content-Type";
    }

    public static class ContentTypes
    {
        public const string Json = "application/json";
    }
}
```

### 6.4 `Endpoints.cs`

```csharp
namespace Acme.BillingClient;

public static class Endpoints
{
    public const string CreateTenant   = "/tenants";
    public const string GetTenant      = "/tenants/{id}";
    public const string ListTenants    = "/tenants";
    public const string DeleteTenant   = "/tenants/{id}";
    public const string CreateInvoice  = "/tenants/{id}/invoices";
}
```

### 6.5 `BillingClientOptions.cs`

```csharp
namespace Acme.BillingClient;

public sealed class BillingClientOptions
{
    public string BaseUrl { get; set; } = "https://api.acme.test";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Headers applied to every outgoing request. Pre-populated from headers
    /// observed on every request in the source Postman collection.
    /// </summary>
    public Dictionary<string, string> CommonHeaders { get; set; } = new()
    {
        { Constants.Headers.ApiVersion, "2024-01" }
    };
}
```

### 6.6 `BillingLoggingOptions.cs`

```csharp
using Microsoft.Extensions.Logging;

namespace Acme.BillingClient;

public sealed class BillingLoggingOptions
{
    public LogLevel DefaultLevel { get; set; } = LogLevel.Information;
    public bool LogRequestBody  { get; set; } = false;
    public bool LogResponseBody { get; set; } = false;

    /// <summary>Per-operation overrides keyed by method name (e.g. "CreateTenantAsync").</summary>
    public Dictionary<string, EndpointLoggingOptions> Endpoints { get; set; } = new();
}

public sealed class EndpointLoggingOptions
{
    public LogLevel? Level { get; set; }
    public bool? LogRequestBody  { get; set; }
    public bool? LogResponseBody { get; set; }
    public bool Disabled { get; set; }
}
```

### 6.7 `ApiResponse.cs` + `Unit.cs`

```csharp
namespace Acme.BillingClient;

public sealed class ApiResponse<T>
{
    public bool IsSuccess { get; init; }
    public int StatusCode { get; init; }
    public T? Data { get; init; }
    public string? RawBody { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>();
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Elapsed { get; init; }
}

public readonly struct Unit
{
    public static readonly Unit Value = default;
}
```

### 6.8 Pluggable seams

```csharp
// IBillingRequestSigner.cs
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
```

```csharp
// IBillingHttpLogger.cs
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.BillingClient;

public interface IBillingHttpLogger
{
    Task BeforeRequestAsync(HttpRequestMessage request, string operationName, CancellationToken cancellationToken);
    Task AfterResponseAsync(HttpResponseMessage response, string operationName, TimeSpan elapsed, CancellationToken cancellationToken);
    Task OnErrorAsync(Exception exception, string operationName, CancellationToken cancellationToken);
}

public sealed class DefaultBillingHttpLogger : IBillingHttpLogger
{
    private readonly ILogger<DefaultBillingHttpLogger> _logger;
    private readonly BillingLoggingOptions _options;

    public DefaultBillingHttpLogger(
        ILogger<DefaultBillingHttpLogger> logger,
        IOptions<BillingLoggingOptions> options)
    {
        _logger  = logger;
        _options = options.Value;
    }

    public Task BeforeRequestAsync(HttpRequestMessage request, string operationName, CancellationToken ct)
    {
        var (level, _) = Resolve(operationName);
        if (level is null) return Task.CompletedTask;

        _logger.Log(level.Value, "→ {Operation} {Method} {Uri}",
            operationName, request.Method, request.RequestUri);
        return Task.CompletedTask;
    }

    public Task AfterResponseAsync(HttpResponseMessage response, string operationName, TimeSpan elapsed, CancellationToken ct)
    {
        var (level, _) = Resolve(operationName);
        if (level is null) return Task.CompletedTask;

        _logger.Log(level.Value, "← {Operation} {Status} in {Elapsed}ms",
            operationName, (int)response.StatusCode, elapsed.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(Exception exception, string operationName, CancellationToken ct)
    {
        _logger.LogError(exception, "✗ {Operation} failed", operationName);
        return Task.CompletedTask;
    }

    private (LogLevel? level, bool logBody) Resolve(string operationName)
    {
        if (_options.Endpoints.TryGetValue(operationName, out var ep))
        {
            if (ep.Disabled) return (null, false);
            return (ep.Level ?? _options.DefaultLevel, ep.LogRequestBody ?? _options.LogRequestBody);
        }
        return (_options.DefaultLevel, _options.LogRequestBody);
    }
}
```

### 6.9 Models

```csharp
// Models/BaseEntity.cs (emitted because Id + CreatedAt are shared)
using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public abstract class BaseEntity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}
```

```csharp
// Models/Tenant.cs
using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class Tenant : BaseEntity
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("plan")]
    public string Plan { get; set; } = "";
}
```

```csharp
// Models/Invoice.cs
using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class Invoice : BaseEntity
{
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}
```

```csharp
// Models/CreateTenantRequest.cs
using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class CreateTenantRequest
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("plan")] public string Plan { get; set; } = "";
}
```

```csharp
// Models/CreateInvoiceRequest.cs
using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class CreateInvoiceRequest
{
    [JsonPropertyName("amount")]   public decimal Amount   { get; set; }
    [JsonPropertyName("currency")] public string  Currency { get; set; } = "";
}
```

```csharp
// Models/ListTenantsResponse.cs
using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class ListTenantsResponse
{
    [JsonPropertyName("items")]
    public List<Tenant> Items { get; set; } = new();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }
}
```

### 6.10 `IBillingClient.cs`

```csharp
using Acme.BillingClient.Models;

namespace Acme.BillingClient;

public interface IBillingClient
{
    Task<ApiResponse<Tenant>> CreateTenantAsync(
        CreateTenantRequest body,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<Tenant>> GetTenantAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<ListTenantsResponse>> ListTenantsAsync(
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<Unit>> DeleteTenantAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<Invoice>> CreateInvoiceAsync(
        string id,
        CreateInvoiceRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generic JSON helper — escape hatch for ad-hoc calls that bypass the
    /// strongly-typed surface. Honours common headers and the request signer.
    /// </summary>
    Task<ApiResponse<TResponse>> SendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest? body,
        IDictionary<string, string>? extraHeaders = null,
        string? operationName = null,
        CancellationToken cancellationToken = default);
}
```

### 6.11 `BillingClient.cs`

```csharp
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
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public BillingClient(
        HttpClient http,
        IOptions<BillingClientOptions> options,
        IBillingRequestSigner signer,
        IBillingHttpLogger logger)
    {
        _http    = http;
        _options = options.Value;
        _signer  = signer;
        _logger  = logger;

        if (_http.BaseAddress is null && !string.IsNullOrEmpty(_options.BaseUrl))
            _http.BaseAddress = new Uri(_options.BaseUrl);
    }

    // ─── POST /tenants ────────────────────────────────────────────────
    public Task<ApiResponse<Tenant>> CreateTenantAsync(
        CreateTenantRequest body,
        CancellationToken cancellationToken = default)
        => SendJsonAsync<CreateTenantRequest, Tenant>(
            HttpMethod.Post,
            Endpoints.CreateTenant,
            body,
            extraHeaders: null,
            operationName: nameof(CreateTenantAsync),
            cancellationToken: cancellationToken);

    // ─── GET /tenants/{id} ────────────────────────────────────────────
    public Task<ApiResponse<Tenant>> GetTenantAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var path = Endpoints.GetTenant.Replace("{id}", Uri.EscapeDataString(id));
        return SendJsonAsync<object, Tenant>(
            HttpMethod.Get, path,
            body: null, extraHeaders: null,
            operationName: nameof(GetTenantAsync),
            cancellationToken: cancellationToken);
    }

    // ─── GET /tenants?page=&pageSize= ─────────────────────────────────
    public Task<ApiResponse<ListTenantsResponse>> ListTenantsAsync(
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var path = Endpoints.ListTenants;
        var qs = new List<string>(2);
        if (page is not null)     qs.Add($"page={page}");
        if (pageSize is not null) qs.Add($"pageSize={pageSize}");
        if (qs.Count > 0) path += "?" + string.Join("&", qs);

        return SendJsonAsync<object, ListTenantsResponse>(
            HttpMethod.Get, path,
            body: null, extraHeaders: null,
            operationName: nameof(ListTenantsAsync),
            cancellationToken: cancellationToken);
    }

    // ─── DELETE /tenants/{id} ─────────────────────────────────────────
    public Task<ApiResponse<Unit>> DeleteTenantAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var path = Endpoints.DeleteTenant.Replace("{id}", Uri.EscapeDataString(id));
        return SendJsonAsync<object, Unit>(
            HttpMethod.Delete, path,
            body: null, extraHeaders: null,
            operationName: nameof(DeleteTenantAsync),
            cancellationToken: cancellationToken);
    }

    // ─── POST /tenants/{id}/invoices ──────────────────────────────────
    public Task<ApiResponse<Invoice>> CreateInvoiceAsync(
        string id,
        CreateInvoiceRequest body,
        CancellationToken cancellationToken = default)
    {
        var path = Endpoints.CreateInvoice.Replace("{id}", Uri.EscapeDataString(id));
        return SendJsonAsync<CreateInvoiceRequest, Invoice>(
            HttpMethod.Post, path,
            body, extraHeaders: null,
            operationName: nameof(CreateInvoiceAsync),
            cancellationToken: cancellationToken);
    }

    // ─── Generic helper ───────────────────────────────────────────────
    public async Task<ApiResponse<TResponse>> SendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest? body,
        IDictionary<string, string>? extraHeaders = null,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        var op = operationName ?? $"{method} {path}";
        var sw = Stopwatch.StartNew();

        using var request = new HttpRequestMessage(method, path);

        // 1. Common headers (from options, sourced from the collection at gen time)
        foreach (var kv in _options.CommonHeaders)
            request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

        // 2. Per-call overrides
        if (extraHeaders is not null)
            foreach (var kv in extraHeaders)
                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

        // 3. Body
        if (body is not null && method != HttpMethod.Get && method != HttpMethod.Delete)
            request.Content = JsonContent.Create(body, options: _json);

        // 4. Pluggable signing — runs AFTER body so HMAC over payload works
        await _signer.SignAsync(request, op, cancellationToken).ConfigureAwait(false);

        await _logger.BeforeRequestAsync(request, op, cancellationToken).ConfigureAwait(false);

        HttpResponseMessage? response = null;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            await _logger.AfterResponseAsync(response, op, sw.Elapsed, cancellationToken).ConfigureAwait(false);

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

            // Non-2xx — try to extract { code, message } from a JSON body.
            string? errCode = null, errMsg = null;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("code",    out var c)) errCode = c.GetString();
                if (doc.RootElement.TryGetProperty("message", out var m)) errMsg  = m.GetString();
            }
            catch { /* not JSON, leave as null */ }

            return new ApiResponse<TResponse>
            {
                IsSuccess    = false,
                StatusCode   = (int)response.StatusCode,
                RawBody      = raw,
                Headers      = headers,
                ErrorCode    = errCode,
                ErrorMessage = errMsg ?? response.ReasonPhrase,
                Elapsed      = sw.Elapsed,
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            await _logger.OnErrorAsync(ex, op, cancellationToken).ConfigureAwait(false);
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
```

### 6.12 DI extension

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Acme.BillingClient;

public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddBillingClient(
        this IServiceCollection services,
        Action<BillingClientOptions>? configureClient = null,
        Action<BillingLoggingOptions>? configureLogging = null)
    {
        if (configureClient  is not null) services.Configure(configureClient);
        else                              services.AddOptions<BillingClientOptions>();

        if (configureLogging is not null) services.Configure(configureLogging);
        else                              services.AddOptions<BillingLoggingOptions>();

        services.TryAddSingleton<IBillingRequestSigner, NoOpBillingRequestSigner>();
        services.TryAddSingleton<IBillingHttpLogger, DefaultBillingHttpLogger>();

        services.AddHttpClient<IBillingClient, BillingClient>((sp, http) =>
        {
            var opt = sp.GetRequiredService<IOptions<BillingClientOptions>>().Value;
            if (!string.IsNullOrEmpty(opt.BaseUrl))
                http.BaseAddress = new Uri(opt.BaseUrl);
            http.Timeout = opt.Timeout;
        });

        return services;
    }
}
```

### 6.13 Usage from the consuming application

```csharp
// Program.cs in the consumer
services.AddBillingClient(
    configureClient: o =>
    {
        o.BaseUrl = "https://api.acme.test";
        o.CommonHeaders[Constants.Headers.ApiVersion] = "2024-02";  // override
    },
    configureLogging: l =>
    {
        l.DefaultLevel = LogLevel.Debug;
        l.Endpoints[nameof(BillingClient.DeleteTenantAsync)] = new()
        {
            Level = LogLevel.Warning,         // delete is rare, log louder
            LogRequestBody = true,
        };
        l.Endpoints[nameof(BillingClient.ListTenantsAsync)] = new()
        {
            Disabled = true,                  // chatty, mute it
        };
    });

services.AddSingleton<IBillingRequestSigner, MyHmacSigner>();   // plug in HMAC
// Optionally swap the logger to add field-level redaction:
services.AddSingleton<IBillingHttpLogger, MyRedactingBillingLogger>();
```

```csharp
// Calling code
public sealed class InvoiceService
{
    private readonly IBillingClient _billing;
    public InvoiceService(IBillingClient billing) => _billing = billing;

    public async Task ChargeAsync(string tenantId, decimal amount, CancellationToken ct)
    {
        var resp = await _billing.CreateInvoiceAsync(
            id: tenantId,
            body: new CreateInvoiceRequest { Amount = amount, Currency = "USD" },
            cancellationToken: ct);

        if (!resp.IsSuccess)
            throw new InvalidOperationException(
                $"Billing call failed: {resp.StatusCode} {resp.ErrorCode} {resp.ErrorMessage}");

        // resp.Data is Invoice
        // resp.Headers has the response headers
        // resp.Elapsed has the wall-clock time
    }
}
```

---

## 7. Edge cases & open decisions

These are the known weak spots — flagging them now so they don't surprise us
mid-implementation.

| # | Problem                                                                 | Default plan                                                                 |
|---|-------------------------------------------------------------------------|------------------------------------------------------------------------------|
| 1 | Postman variable references like `{{baseUrl}}` inside bodies            | Replace with the variable's literal value at gen time; warn if missing.      |
| 2 | Bodies with `formdata` / `urlencoded` / `file` / `graphql`              | v1: skip operation, emit a `// TODO: non-JSON body` stub. v2: support form.  |
| 3 | Response examples that aren't JSON (HTML, plain text)                   | Emit `Task<ApiResponse<string>>` and let the caller parse.                   |
| 4 | Heterogeneous arrays in JSON examples                                   | Fall back to `List<JsonElement>` + warning.                                  |
| 5 | Two operations whose names collide after PascalCase                     | Suffix `_2`, `_3` and emit a warning. User can rename in the collection.    |
| 6 | Property name that is a C# keyword (`event`, `class`)                   | Prefix `@`; keep `[JsonPropertyName]` original.                              |
| 7 | Nested objects inside response                                          | Hoist into a sibling type using `{Parent}{PropertyName}` naming.             |
| 8 | Multiple example responses for the same operation                       | Pick the first `2xx`. Skip non-2xx in v1; later: emit `OneOf<…>`.            |
| 9 | Path/query parameter that has no example value to type-infer from       | Default to `string`.                                                         |
| 10| Operation with same name as the client class                            | Suffix `_Op`. (Rare, but possible: "Billing Client" item.)                  |
| 11| Should the generator support overwriting hand-edited files?             | Default: `--clean` deletes the output directory. Without it, refuse to overwrite files that have changed since last gen (track via a `.dragonfire-apigen.lock` manifest). |
| 12| Decimal vs double for floating-point JSON                                | Default `decimal`. Add `--floats-as=double` flag.                            |
| 13| What about pagination — `Link` headers, cursors?                        | Out of scope for v1; user can hand-edit. The wrapper exposes headers.        |

---

## 8. Implementation order (when we actually start coding)

1. Postman parser + IR (lots of POCOs, well-tested round-trip).
2. JSON-example → TypeIR inferrer with the primitive-sniffing heuristics.
3. Naming + collision resolver.
4. Base-class promoter (pure transformation on IR).
5. The static emitters: csproj, Constants, Endpoints, Options, ApiResponse, Unit, Signer/Logger seams, DI extensions. These never look at IR fields beyond names — easy.
6. Model emitter — walks `TypeIR.Properties` and produces one file per type.
7. Client interface emitter — straightforward signature emission.
8. Client implementation emitter — the only file with branching based on
   per-operation traits (path params, query, body presence). Worth a
   focused investment.
9. CLI front-end (System.CommandLine) gluing it all together.
10. Smoke-tested by running against 3–4 real-world Postman exports and
    eyeballing the output.

---

## 9. Things I am explicitly NOT planning yet

- OpenAPI (Swagger) input.
- gRPC client generation.
- Resilience policies (Polly) — user adds via `services.AddHttpClient(...).AddTransientHttpErrorPolicy(...)` after generation.
- Distributed tracing — same answer; the user wraps with their own
  `DelegatingHandler` or registers via `IHttpClientFactory`.
- Caching — Dragonfire.Caching is one `services.Decorate<IBillingClient>(...)`
  call away if anyone wants it.

These are easy to add post-v1 but should not pollute the core generator.
