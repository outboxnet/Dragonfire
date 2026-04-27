# Changelog

All notable changes to this project are documented here. The format is based
on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `ISyncRunner` (with `RunAsync(name)` / `RunAllAsync()` returning a
  `SyncRunSummary`) and `services.AddSyncRunner()` for hosts that drive the
  schedule themselves — Azure Functions timer triggers, console jobs, etc.
- `samples/SyncLib.Sample.AzureFunctions` demonstrating a `[TimerTrigger]`
  fan-out across all registered providers using a caller-supplied repository.
- `ISyncStateStore` / `ISyncStateReader` for tracking per-provider sync state
  (last run time, status, duration, records, error) separately from domain
  entities.
- `SyncLib.EntityFrameworkCore` package with `EfSyncStateStore` and a generic
  EF-Core repository.
- Built-in metrics on the `SyncLib` `Meter` and tracing on the `SyncLib`
  `ActivitySource`.
- Sample app (`samples/SyncLib.Sample.WebApi`) demonstrating a worker that
  syncs from a fake API into SQL Server, plus a minimal API exposing live
  sync state.

### Changed
- `SyncOrchestrator` reads last-sync time from `ISyncStateStore` instead of
  the entity repository.
- Bug fixes: null-deref when no error handler is supplied, multiple
  enumeration of fetched data, async-void timer callbacks.

## [0.1.0] - initial preview
