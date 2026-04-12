---
schema_version: 1
archetype: persistence/dependency-management
language: go
principles_file: _principles.md
libraries:
  preferred: Go modules (go.mod + go.sum)
  acceptable:
    - govulncheck
  avoid:
    - name: GONOSUMCHECK or GONOSUMDB
      reason: Disables checksum verification. The checksum database is your tamper-evidence layer.
    - name: replace directives left in production go.mod
      reason: Bypasses the module proxy and checksum database. Use only during local development.
minimum_versions:
  go: "1.22"
---

# Dependency Management — Go

## Library choice
Go modules are the only dependency management system for Go. `go.mod` declares direct dependencies with exact versions (Go does not support version ranges for applications). `go.sum` records cryptographic hashes of every module version and is verified against the Go checksum database (`sum.golang.org`). This is a strong supply-chain defense by default — the ecosystem got lockfile-equivalent integrity verification from day one. The remaining work is vulnerability scanning (`govulncheck`), minimizing the dependency tree, and securing private module access.

## Reference implementation
```bash
# go.mod — application
module github.com/myorg/myservice

go 1.22

require (
    gorm.io/gorm v1.25.12
    gorm.io/driver/postgres v1.5.11
    github.com/go-playground/validator/v10 v10.23.0
)
```
```bash
# CI pipeline
go mod verify          # checks go.sum hashes against downloaded modules
go mod tidy -diff      # fails if go.mod/go.sum would change (Go 1.23+)
govulncheck ./...      # checks for known vulns in called code paths
```
```go
// GOFLAGS in CI environment to enforce verification
// export GOFLAGS="-mod=readonly"
// export GONOSUMCHECK=""   // never set this
// export GONOSUMDB=""      // never set this
```

## Language-specific gotchas
- `go mod tidy` adds and removes dependencies but does not fail if the tree changes. Use `go mod tidy -diff` (Go 1.23+) in CI to detect uncommitted dependency changes. For older Go versions, run `go mod tidy` then `git diff --exit-code go.mod go.sum`.
- `go mod verify` checks that the modules in the local cache match the hashes in `go.sum`. Run this in CI before building. If the cache was tampered with or the download was corrupted, this catches it.
- `GONOSUMCHECK` and `GONOSUMDB` disable checksum verification for matching modules. Never set these in CI. For private modules, use `GONOSUMCHECK` scoped to your private module path only: `GONOSUMCHECK=github.com/myorg/*`.
- `GOPRIVATE=github.com/myorg/*` tells Go to fetch these modules directly (skipping the module proxy and checksum database). This is necessary for private repos but means those modules do not get the tamper-evidence benefit of the checksum database. Compensate by pinning exact commit hashes or tags and running `go mod verify`.
- `replace` directives in `go.mod` point a module to a local path or alternate URL, bypassing the proxy and checksum database. Use them only during development and strip them before merging to `main`. A CI check that greps for `replace` in `go.mod` and fails is cheap insurance.
- `govulncheck` analyzes call graphs, not just the dependency list. A vulnerable function that is never called does not produce a finding. This reduces noise but means `govulncheck` is not a substitute for reading advisory reports on your dependencies.
- Vendor mode (`go mod vendor`) copies dependencies into the repo. This makes builds reproducible without network access but means you must re-vendor on every update. `go mod verify` does not check vendored code — use `go mod vendor` then `git diff --exit-code vendor/` in CI.

## Tests to write
- Checksum verification: `go mod verify` exits 0 in CI.
- Tidy check: `go mod tidy -diff` (or `go mod tidy && git diff --exit-code go.mod go.sum`) exits 0 — no uncommitted dependency changes.
- Vulnerability scan: `govulncheck ./...` exits 0 with no findings.
- No replace directives: `grep -c '^replace' go.mod` returns 0 on the main branch.
- Private module scoping: assert `GOPRIVATE` is set only to the organization's module path prefix, not to `*`.
