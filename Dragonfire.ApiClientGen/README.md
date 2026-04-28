# Dragonfire.ApiClientGen

A standalone .NET CLI tool that consumes a **Postman v2.1 collection** and emits
a typed C# HTTP client library — interface, implementation, request/response
models, an `Endpoints` constants table, a `Constants` class for shared headers,
a wrapper response type, and pluggable seams for **request signing** and
**per-endpoint logging**.

Not a Roslyn source generator. Just a CLI that writes `.cs` files to disk.

---

## Install (once published)

```bash
dotnet tool install --global Dragonfire.ApiClientGen
```

## Build & run from this repo

```bash
dotnet run --project Dragonfire.ApiClientGen/src/Dragonfire.ApiClientGen.Cli -- \
    --input  ./acme-billing.postman_collection.json \
    --output ./generated/Acme.BillingClient \
    --namespace Acme.BillingClient \
    --client-name Billing
```

## CLI surface

| Flag                         | Required | Default     | What it does |
|------------------------------|----------|-------------|--------------|
| `--input` / `-i`             | yes      |             | Postman v2.1 collection JSON. |
| `--output` / `-o`            | yes      |             | Where to write the generated project. |
| `--namespace` / `-n`         | yes      |             | Root namespace, e.g. `Acme.BillingClient`. |
| `--client-name` / `-c`       | yes      |             | Class-name prefix — `Billing` → `BillingClient`, `IBillingClient`, etc. |
| `--target-framework` / `-t`  |          | `net8.0`    | TFM for the generated csproj. |
| `--response-examples`        |          |             | JSON file overriding response example bodies for operations that lack a saved Postman example. Map keyed by operation name. |
| `--base-url`                 |          |             | Override the base URL detected from the collection's `{{baseUrl}}` variable. |
| `--clean`                    |          | off         | Delete the output directory before writing. |
| `--dry-run`                  |          | off         | Print the plan without touching disk. |
| `--floats-as-double`         |          | off         | Use `double` for non-integer JSON numbers (default `decimal`). |

## What gets generated

```
{output}/
├── {Namespace}.csproj
├── Constants.cs                       — shared header / content-type names
├── Endpoints.cs                       — public const string per operation
├── ApiResponse.cs                     — generic wrapper + Unit struct
├── {Prefix}ClientOptions.cs           — IOptions<…>, common-headers dictionary
├── {Prefix}LoggingOptions.cs          — per-endpoint level / body / disabled
├── I{Prefix}RequestSigner.cs          + NoOp{Prefix}RequestSigner
├── I{Prefix}HttpLogger.cs             + Default{Prefix}HttpLogger
├── I{Prefix}Client.cs
├── {Prefix}Client.cs
├── {Prefix}ServiceCollectionExtensions.cs
└── Models/
    ├── BaseEntity.cs                  (only when promotion fires)
    └── …
```

## Conventions the generator applies

- **PascalCase** identifiers; spaces and special characters in Postman item
  names are stripped. Method names get an `Async` suffix; `Endpoints.*`
  constants do not.
- **Type inference** from saved JSON examples: ISO-8601 strings → `DateTimeOffset`,
  hex-formatted UUIDs → `Guid`, integer ranges → `int`/`long`, non-integers
  → `decimal` (toggle with `--floats-as-double`).
- **Nullable** when a value is `null` or missing in any observed example.
- **Base-class promotion** — if ≥3 models share ≥2 primitive `(name, type)`
  pairs covering ≥50% of models in the group, those pairs lift into an
  `abstract BaseEntity` and matching models inherit + drop the props.
- **Common headers** (present on every request with the same value) become
  defaults in `{Prefix}ClientOptions.CommonHeaders`. Their names live as
  `const string`s on `Constants.Headers`. `Content-Type` is excluded —
  `JsonContent` sets it.
- **Path parameters** (`:id` or `{{id}}` in Postman) become `{id}` in
  `Endpoints.*` and are interpolated client-side via `Replace` +
  `Uri.EscapeDataString`.
- **Wrapper response** — every method returns `Task<ApiResponse<T>>` with
  `IsSuccess`, `StatusCode`, `Data`, `RawBody`, `Headers`, `ErrorCode`,
  `ErrorMessage`, `Elapsed`. Operations with no response body return
  `ApiResponse<Unit>`.
- **No exceptions on non-2xx.** Transport-level exceptions are converted to
  `ApiResponse<T> { ErrorCode = "transport.error" }`. Only
  `OperationCanceledException` propagates.
- **Pluggable signing** — `I{Prefix}RequestSigner` runs *after* the body is
  attached so HMAC-over-payload schemes work. Default is no-op.
- **Pluggable logging** — `I{Prefix}HttpLogger` with per-operation overrides.
  Replace the whole logger to inject your own field-level redaction.

## What's out of scope (v1)

- OpenAPI / Swagger input.
- gRPC client generation.
- Resilience policies (Polly), tracing, caching — all reachable post-generation
  via `IHttpClientFactory` and standard DI decoration.
- Non-JSON request bodies (`formdata`, `urlencoded`, `file`, `graphql`) — the
  affected operation is skipped with a warning.
