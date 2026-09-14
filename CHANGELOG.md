# Changelog

## 0.2.82 — Parity with Python claude-agent-sdk v0.2.82

### Added
- Full `ISessionStore` subsystem: `InMemorySessionStore`, `FileSessionStore`,
  `TranscriptMirrorBatcher`, `SessionStoreConformance` harness, and the
  `ClaudeSessions` free-function surface (`ListSessionsAsync`,
  `GetSessionMessagesAsync`, `RenameSessionAsync`, `TagSessionAsync`,
  `DeleteSessionAsync`, `ImportSessionToStoreAsync`).
- Resume materialization (`SessionResume.MaterializeResumeSessionAsync`) +
  `SessionStoreFlushMode.Eager` / `Batched` flush controls.
- `ContextUsageResponse` / `ContextUsageCategory` (Python commit `ac900bd`).
- `McpStatusResponse` / `McpServerStatus` (Python commit `28f9b4b`).
- v0.2.82 message-parser additions (rate-limit events, new system subtypes,
  new content blocks).
- New examples:
  - `examples/SessionStores/JsonlPartFileStore/` — reference adapter that
    mirrors the Python S3 part-file pattern using the local filesystem.
  - `examples/extensions/SessionStoreUsage/` — exercises the
    `ClaudeSessions` free-function surface against `InMemorySessionStore`.
  - `examples/extensions/SessionResume/` — end-to-end resume materialization
    round-trip without a running CLI.
  - `examples/extensions/ContextUsage/` — renders a fabricated
    `ContextUsageResponse` so callers can build dashboards.
  - `examples/extensions/HookEvents/` — registers callbacks on every
    `HookEvent` value.
- Integration test suite `tests/Claude.AgentSdk.Tests/Integration/`
  covering session-resume round-trip and cross-feature (hooks + session
  store + context-usage) scenarios.

### Changed
- Package version bumped to `0.2.82` (assembly + file version `0.2.82.0`).
