# Dragonfire

A suite of focused, production-ready libraries for building reliable distributed
.NET services. Every package targets **net8.0**, ships from this single
repository, and is released at one shared version so nothing ever drifts.

```
                                ┌──────────────────────────────────┐
        ┌── inbound webhooks ─→ │   Dragonfire.Inbox               │ ─→ your handlers
        │                       └──────────────────────────────────┘
        │                       ┌──────────────────────────────────┐
        │   transactional   ──→ │   Dragonfire.Outbox              │ ─→ outbound webhooks
        │     domain write      └──────────────────────────────────┘
your    │
service │   long-running    ──→ Dragonfire.Saga       (sagas + compensation)
        │
        │   fetch on a      ──→ Dragonfire.Sync       (scheduled syncs + circuit breaker)
        │     schedule
        │   poll an API     ──→ Dragonfire.Poller     (backoff + concurrency)
        │
        │   read-through    ──→ Dragonfire.Caching    (memory / Redis / hybrid + tags)
        │     cache
        │   feature flags   ──→ Dragonfire.Features   (gates, percentage rollout, audit)
        │   request-scoped  ──→ Dragonfire.TenantContext  (ambient tenant for B2B2C)
        │     identity
        │   structured     ───→ Dragonfire.Logging    (zero-boilerplate, source-generated)
        │     logging
        │
        │   typed C# client ─→ Dragonfire.ApiClientGen (CLI: postman.json → typed HttpClient)
              from a postman
              collection
```

Every package is independent — pull only what you use. Cross-cutting concerns
(tenant, logging) are designed so the other libraries pick them up automatically
when registered.

---

## Packages in this repo

| Project folder | What it solves |
|---|---|
| **[Dragonfire.Caching](./Dragonfire.Caching)** | Composable read-through caching with tag-based invalidation, stampede protection, and pluggable providers (Memory · Distributed · Hybrid · Redis · Protobuf serialization · gRPC interceptors). |
| **[Dragonfire.Features](./Dragonfire.Features)** | Feature toggles + release gates: `[FeatureGate]` attribute, per-tenant / per-user / percentage rules, periodic refresh from `IConfiguration` or EF Core, audit log for B2B compliance, optional caching decorator. |
| **[Dragonfire.Inbox](./Dragonfire.Inbox)** | Transactional inbox for receiving webhooks. Persists incoming events atomically, deduplicates by provider event ID, dispatches to your handlers with at-least-once delivery + exponential retry + dead-letter. |
| **[Dragonfire.Logging](./Dragonfire.Logging)** | Source-generated structured logging — annotate a service with `[Loggable]`, get compile-time logging proxies with redaction, scrubbing, and one named property per field. ASP.NET, Application Insights, gRPC adapters. |
| **[Dragonfire.Outbox](./Dragonfire.Outbox)** | Transactional outbox for sending webhooks. Solves the dual-write problem: writes outbox row in the same DB transaction as your domain data, then a background processor delivers with HMAC signing, retry, and per-subscription routing. |
| **[Dragonfire.Poller](./Dragonfire.Poller)** | Generic polling framework — exponential backoff, channel-based queue, real-time progress streaming via `IAsyncEnumerable`, pluggable metrics. |
| **[Dragonfire.Saga](./Dragonfire.Saga)** | Workflows + sagas with crash-safe persistence, retries, and compensation. Define multi-step business processes that survive process restarts. |
| **[Dragonfire.Sync](./Dragonfire.Sync)** | Scheduled data-synchronization jobs with retries, circuit breaker, and observability. Each provider fetches data, maps to your entities, persists through your repository. |
| **[Dragonfire.TenantContext](./Dragonfire.TenantContext)** | Composable tenant-context propagation for B2B2C SaaS. Resolver pipeline (header / claim / subdomain / route / API key), middleware, HTTP `DelegatingHandler`, gRPC interceptors, logger enrichment, scope helpers for queues and `Task`s. |
| **[Dragonfire.ApiClientGen](./Dragonfire.ApiClientGen)** | Standalone CLI (`dotnet tool`) that consumes a Postman v2.1 collection and emits a typed C# HTTP client library — interface, implementation, models, `Endpoints` constants, `IOptions`-backed common headers, wrapper response type, and pluggable seams for request signing and per-endpoint logging. Not a Roslyn generator. |

Each folder has its own deep-dive README. The summaries above are intentionally
short — open any package for the full story.

---

## Repository layout

```
Dragonfire/
├── Directory.Build.props          ← single source of truth for <Version>
├── pack-all.ps1                   ← build + pack every library at one version
├── pack-all.sh                    ← bash equivalent
├── artifacts/                     ← (gitignored) pack-all output
│
├── Dragonfire.Caching/
│   ├── Directory.Build.props      ← imports the root, adds package-specific bits
│   ├── Dragonfire.Caching.sln
│   ├── README.md                  ← deep-dive docs for this package
│   ├── src/
│   │   ├── Dragonfire.Caching/                       ← the core library
│   │   ├── Dragonfire.Caching.Memory/
│   │   ├── Dragonfire.Caching.Distributed/
│   │   ├── Dragonfire.Caching.Hybrid/
│   │   ├── Dragonfire.Caching.Redis/
│   │   └── Dragonfire.Caching.Serialization.Protobuf/
│   └── SampleApp/                 ← runnable example (IsPackable=false)
│
├── Dragonfire.Inbox/
│   ├── Directory.Build.props
│   ├── Directory.Packages.props   ← central package version pins
│   ├── Dragonfire.Inbox.slnx
│   ├── README.md
│   ├── src/                       ← Core / EntityFrameworkCore / AspNetCore / Processor / Providers / AzureFunctions
│   ├── tests/
│   └── samples/
│
└── ... (one folder per package, same shape)
```

Every package follows the same convention: `src/` for libraries, `tests/` for
xUnit suites, `samples/` for runnable demos, plus its own `README.md`,
`Directory.Build.props`, and a `.sln` / `.slnx`.

---

## Building everything

```bash
# clone
git clone https://github.com/outboxnet/Dragonfire.git
cd Dragonfire

# restore + build a single package
dotnet build Dragonfire.Inbox/Dragonfire.Inbox.slnx

# run a package's tests
dotnet test Dragonfire.Inbox/Dragonfire.Inbox.slnx
```

There is intentionally no top-level master solution — each package is a
self-contained unit and can be opened, built, tested, and shipped on its own.
The top-level `pack-all` script is the orchestrator when you want to ship
everything together.

---

files as a build artifact. |
| **`release.yml`** | git tag `v*.*.*` **or** manual `workflow_dispatch` | Packs every library at the resolved version and pushes all `.nupkg` + `.snupkg` files to `nuget.org` with `--skip-duplicate`. Creates a GitHub Release on tag push. |

### One-time setup

1. Create a NuGet.org API key at <https://www.nuget.org/account/apikeys>, scoped
   to *Push new packages and package versions* with a glob of `Dragonfire.*`.
2. In the repo, go to **Settings → Secrets and variables → Actions → New
   repository secret** and add:
   - **Name:** `NUGET_API_KEY`
   - **Value:** your key

### Two ways to ship a release

**Option A — git tag (recommended).** The version comes from the tag name:

```bash
# bump the shared version
sed -i 's|<Version>.*</Version>|<Version>8.4.0</Version>|' Directory.Build.props
git commit -am "release: 8.4.0"
git tag v8.4.0
git push --follow-tags
```

The `release` workflow runs, pushes every package at `8.4.0`, and creates a
GitHub Release with the `.nupkg` + `.snupkg` files attached.

**Option B — manual dispatch.** From **Actions → release → Run workflow**, type
the version (e.g. `8.4.0` or `8.4.0-preview.1`). Useful for pre-release
builds or hot-fix re-pushes without retagging.

---

## How shared versioning works under the hood

```
Directory.Build.props            (root)
  ├── <Version>8.3.0</Version>
  ├── <PackageLicenseExpression>MIT</PackageLicenseExpression>
  ├── <RepositoryUrl>...</RepositoryUrl>
  ├── ... shared metadata ...
  │
  └── imported by each child Directory.Build.props via
      <Import Project="$([MSBuild]::GetPathOfFileAbove(
                        'Directory.Build.props',
                        '$(MSBuildThisFileDirectory)../'))" />
```

MSBuild does **not** auto-import a parent `Directory.Build.props` once it has
found one in a child folder. Each child file therefore explicitly imports the
parent at the top, guaranteeing every project in the suite inherits the same
`<Version>` and shared package metadata while still being free to override
per-package fields (PackageId, Description, PackageTags, etc.) locally.

To verify the chain for any project:

```bash
dotnet msbuild Dragonfire.Inbox/src/Dragonfire.Inbox.Core/Dragonfire.Inbox.Core.csproj \
  -getProperty:Version
# → 8.3.0
```

---

## Conventions

- **net8.0** everywhere
- `Nullable enable` everywhere (root prop)
- `TreatWarningsAsErrors` enabled per-project
- All packages are MIT-licensed
- Symbol packages (`*.snupkg`) shipped with every release
- SourceLink + deterministic builds enabled — debugging into a shipped package
  drops you onto the matching commit in this repo
- Tests use xUnit; samples are real runnable apps

---

## Contributing

Each package has its own README with design notes. The overall principle across
the suite:

- **Composable** — every library is small, focused, and depends only on what it
  must. Adapters (`*.AspNetCore`, `*.EntityFrameworkCore`, `*.Grpc`, etc.) are
  separate packages so consumers never pull a transport or persistence stack
  they aren't using.
- **Production-ready by default** — observability (`ActivitySource` + `Meter`)
  is built in, retries and timeouts are configurable, and persistence is
  transactional where it has to be.
- **No magic** — every behaviour is reachable through the public API; resolver
  chains, retry policies, and serialization are configuration, not behaviour
  buried in source generators or attributes.

---

## License

MIT — see [LICENSE](./LICENSE).
