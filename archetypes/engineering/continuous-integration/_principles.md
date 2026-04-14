---
schema_version: 1
archetype: engineering/continuous-integration
title: Continuous Integration
summary: Every commit builds and tests on a shared pipeline; main stays green; broken builds are the team's highest-priority fix.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - ci
  - continuous-integration
  - green-main
  - fast-feedback
  - automated-build
  - pipeline
  - trunk-based-development
  - merge-to-main
  - pull-request
  - build-health
  - pipeline-speed
  - test-automation
related_archetypes:
  - engineering/testing-strategy
  - engineering/walking-skeleton
  - engineering/commit-hygiene
  - engineering/deployment-discipline
  - architecture/secure-ci-cd
references:
  book: "Continuous Delivery — Humble & Farley"
  article: "Martin Fowler — Continuous Integration"
  article: "Paul Hammant — Trunk-Based Development"
---

# Continuous Integration -- Principles

## When this applies
From the first commit of any project that is not a throwaway script. CI is the feedback loop that catches integration problems within minutes of their introduction rather than days later. It applies whenever code is being committed by more than one person, or even by one person across multiple branches or machines. The discipline also applies when an existing CI pipeline has decayed -- tests skipped, warnings ignored, builds taking hours -- because a pipeline no one trusts is a pipeline that fails silently.

## Architectural placement
CI sits between the developer's commit and the deploy pipeline. It is the enforcement layer for every rule the team has adopted: tests pass, code compiles on every target, style and lint checks hold, security scans complete, dependencies are not vulnerable. A healthy CI pipeline is the foundation under continuous-delivery: you cannot deploy confidently what you have not integrated confidently. It is the operational counterpart to testing-strategy: tests define the checks, CI runs them on every change.

## Principles
1. **Every commit runs the full checked pipeline.** Build, unit tests, integration tests, linters, type checkers, security scanners -- all of it, on every commit, on every branch. Selective pipelines ("only run integration tests nightly") hide regressions until the next full run, which is too late.
2. **Main is always green.** A red main is an emergency, not a backlog item. The team stops forward work until main is green, because every commit on a red main compounds the problem. "Don't Break the Build" is a team culture, not a tool.
3. **Fast feedback is a feature.** Under 10 minutes is a good target for the pipeline; under 30 minutes is tolerable. Slower pipelines get batched changes, get skipped checks, get bypassed through `-ff` or `--no-verify`. Speed is invested in with caching, parallelization, and pruning -- not postponed until "someday."
4. **Integrate early, integrate often.** Merge to main at least daily. Long-lived branches accumulate divergence that CI cannot catch -- the merge is the integration, and it fails. Trunk-based development, feature flags for in-progress features, and small PRs all serve this end.
5. **Build once, promote the artifact.** Compile the binary, tag it, and promote the same artifact through test → staging → production. Recompiling per environment introduces drift; promotion ensures what was tested is what deploys.
6. **Identical pipeline for every branch and PR.** The pipeline that runs on a PR is the pipeline that runs on main. PR-only shortcuts (skipping integration tests on PRs) create a two-tier system where integration problems reach main and surprise the team.
7. **Flaky tests block merging.** A flaky test retries on the same commit and gives false passes or false failures -- both destroy the signal. Either stabilize the test or delete it; do not "rerun until green."
8. **No manual steps in the pipeline.** Every step is code in version control. Manual deploy scripts, manual test runs, manual config edits -- each becomes tribal knowledge, each becomes a single point of failure. If a human has to remember something, so will the pipeline.
9. **Reproducible from scratch.** A fresh checkout on a fresh machine produces the same build. No "it works because my local has node 16 installed" or "our CI runner happens to have Postgres 14 configured." Version the toolchain, the runtimes, the services -- everything a build needs.
10. **Visibility by default.** Every merged commit's build status, every PR's test results, every nightly build's outcome is visible on a dashboard the team actually watches. Silent failures are the worst kind; make them loud.

## Anti-patterns
- "Our pipeline takes an hour, we only run it on main" -- PRs merge untested, main is perpetually half-green, regressions land constantly.
- Tests marked `@Ignore` or `.skip` for months because "we'll fix them later" -- they rot, and now the suite lies about coverage.
- A `nightly` test run that no one looks at, failing every night for a year, silently masking real failures under the noise.
- PR-approval bots that check "has at least one approval" but not "all checks passing", letting broken changes land.
- Caching so aggressive that CI shows green against stale dependencies, hiding real integration bugs.
- Long-lived feature branches (weeks or months) that catastrophically diverge from main and swallow a sprint to merge.
- "Works on my machine" as an accepted answer to a CI failure -- if CI disagrees with local, CI is the source of truth.
- Recompiling the binary for each environment instead of promoting the tested artifact, so the binary that reached staging is not the binary that reaches production.
- A build that takes 40 minutes and gives zero insight when it fails, forcing re-runs to gather detail.

## References
- Jez Humble & David Farley -- *Continuous Delivery*
- Martin Fowler -- "Continuous Integration" (martinfowler.com/articles/continuousIntegration.html)
- Paul Hammant -- *Trunk-Based Development and Branching* (trunkbaseddevelopment.com)
- Jez Humble -- "State of DevOps Report" annual findings on CI and delivery performance
- Google -- *Software Engineering at Google* (CI/CD chapter)
