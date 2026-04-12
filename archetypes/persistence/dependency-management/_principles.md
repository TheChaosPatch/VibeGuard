---
schema_version: 1
archetype: persistence/dependency-management
title: Dependency Management
summary: Supply chain security through lockfiles, audits, pinning, and verified sources.
applies_to: [csharp, python, go]
status: draft
keywords:
  - supply-chain
  - dependency
  - lockfile
  - audit
  - pinning
  - vulnerability
  - sca
  - transitive
  - checksum
  - reproducible
  - npm-audit
  - govulncheck
related_archetypes:
  - persistence/secrets-handling
references:
  owasp_asvs: V14.2
  owasp_cheatsheet: Vulnerable Dependency Management Cheat Sheet
  cwe: "1395"
---

# Dependency Management — Principles

## When this applies
Every project that pulls code from a package registry — NuGet, PyPI, Go module proxies, npm, Maven Central. The moment you add a dependency, you inherit that package's bugs, vulnerabilities, and transitive dependency tree. Supply chain attacks are not theoretical: event-stream, ua-parser-js, codecov, colors.js, and the xz-utils backdoor all exploited the gap between "I trust this package" and "I verified this package." This archetype applies from the first `dotnet add package` to the CI pipeline that builds the release artifact.

## Architectural placement
Dependency decisions are explicit, version-pinned, and machine-verifiable. A lockfile records exact resolved versions and integrity hashes, and it is committed to version control. CI validates that the lockfile is fresh and that no known vulnerabilities exist in the resolved tree. Direct dependencies are chosen deliberately; transitive dependencies are audited when they change. Private feeds, if used, are authenticated and scoped so that public-feed typosquats cannot shadow internal package names.

## Principles
1. **Commit lockfiles to version control.** The lockfile (`packages.lock.json`, `poetry.lock`, `go.sum`, `package-lock.json`) is the only artifact that makes builds reproducible. Without it, `dotnet restore` on Tuesday might resolve a different patch than Monday, and you cannot diff what changed. Treat the lockfile as source code.
2. **Pin exact versions for direct dependencies.** Floating ranges (`>=1.0`, `^1.2`, `~=1.3`) let a compromised patch release into your build without a code review. Pin to exact versions (`1.2.3`) and update deliberately through a PR that shows the diff.
3. **Run vulnerability scanning in CI on every PR.** `dotnet list package --vulnerable`, `pip-audit`, `govulncheck`, `npm audit` — the specific tool depends on the ecosystem but the requirement is the same: the build fails if a known vulnerability exists in the resolved dependency graph. Do not gate on severity alone; review every finding.
4. **Verify integrity hashes.** NuGet verifies package signatures and hashes by default. Go modules verify against `go.sum` and the checksum database. pip supports hash-checking mode. Enable these — they are the only defense against a registry compromise or MITM that serves a tampered package.
5. **Minimize transitive dependency surface.** Every transitive dependency is an unreviewed trust decision. Prefer libraries with fewer dependencies. When evaluating two libraries with similar functionality, the one with a smaller dependency tree is more secure by construction.
6. **Use private feeds with namespace reservation.** If your organization publishes internal packages, configure your package manager to resolve internal namespaces from your private feed only. This blocks dependency confusion attacks where an attacker publishes a higher-version public package with your internal package name.
7. **Automate dependency updates with review.** Tools like Dependabot, Renovate, or `pip-audit --fix` propose updates as PRs. The update is reviewed, tested, and merged — not auto-merged. The PR diff includes the lockfile change, which is the reviewable artifact.
8. **SCA is a CI gate, not a quarterly report.** Software Composition Analysis belongs in the pull-request pipeline, not in a dashboard someone checks monthly. A vulnerability that lands in `main` is a vulnerability in production.

## Anti-patterns
- Not committing the lockfile because "it creates merge conflicts." The conflicts are the point — they surface when two developers changed dependencies in parallel.
- Using floating version ranges in production applications. Libraries that are dependencies of other libraries may use ranges; applications that are deployed should pin.
- Running `pip install` or `dotnet restore` in CI without `--locked-mode` or equivalent — the build silently resolves newer versions than what the developer tested.
- Ignoring `npm audit` output because "most of them are low severity." Low-severity findings in a transitive dependency of your auth library are not low-severity in context.
- Publishing internal packages to a public registry by accident. Configure feed scoping and namespace reservation.
- Disabling NuGet package signature verification to "speed up restore." The verification is the only thing standing between you and a tampered package.
- Copying a dependency's source code into your repo ("vendoring") without tracking its version or provenance — you now have an unpatched fork that no scanner can find.
- Auto-merging Dependabot PRs without test coverage to validate the upgrade.

## References
- OWASP ASVS V14.2 — Dependency
- OWASP Vulnerable Dependency Management Cheat Sheet
- CWE-1395 — Dependency on Vulnerable Third-Party Component
