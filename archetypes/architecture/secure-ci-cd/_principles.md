---
schema_version: 1
archetype: architecture/secure-ci-cd
title: Secure CI/CD Pipelines
summary: Hardening build, test, and deployment pipelines against supply-chain attacks and configuration drift.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - ci-cd
  - pipeline
  - supply-chain
  - artifact
  - signing
  - reproducible-build
  - branch-protection
  - deployment
  - sbom
  - slsa
  - provenance
  - secret-management
  - pipeline-security
related_archetypes:
  - architecture/secure-development-lifecycle
  - persistence/dependency-management
  - persistence/secrets-handling
  - logging/audit-trail
references:
  owasp_asvs: V14
  owasp_cheatsheet: CI CD Security Cheat Sheet
  cwe: "829"
---

# Secure CI/CD Pipelines -- Principles

## When this applies
Any project with an automated build, test, or deployment pipeline -- GitHub Actions, GitLab CI, Azure DevOps, Jenkins, CircleCI, or any equivalent. The CI/CD pipeline is the most privileged automated actor in your system: it has commit access, secret access, deployment credentials, and often production access. A compromised pipeline is a compromised product. If you have a `Dockerfile`, a workflow YAML, or a deployment script, this archetype applies.

## Architectural placement
The CI/CD pipeline sits between source code and production. It is a trust boundary as critical as the application's own authentication layer -- yet it is often treated as plumbing rather than infrastructure. This archetype governs the pipeline itself: how it authenticates, what it can access, how it handles secrets, and how it produces verifiable artifacts. It complements `persistence/dependency-management` (which governs what goes into the build) and `persistence/secrets-handling` (which governs how secrets are stored and loaded).

## Principles
1. **Treat pipeline configuration as production code.** Workflow files, Dockerfiles, and deployment scripts are part of the attack surface. They live in version control, go through code review, and require the same approval as application code. A one-line change to a workflow YAML can exfiltrate every secret in the repository.
2. **Pin dependencies by hash, not by tag.** Container base images, GitHub Actions, and build tools should be pinned to an immutable digest (`sha256:...`), not a mutable tag (`:latest`, `@v3`). A compromised upstream tag silently replaces your build dependency with an attacker's payload.
3. **Minimize pipeline permissions.** The CI token should have the minimum permissions required for the job. A test job does not need deployment credentials. A lint job does not need write access to the repository. Use scoped tokens, short-lived credentials, and OIDC federation where the platform supports it.
4. **Never store secrets in pipeline configuration.** Secrets live in the platform's secret store (GitHub Actions secrets, Vault, cloud KMS), never in YAML files, environment variable definitions committed to the repo, or build arguments visible in logs. Secrets injected into the pipeline are available only to the steps that need them, not to every step in the workflow.
5. **Protect the main branch.** Require pull request reviews, passing CI checks, and signed commits before merging to the branch that triggers production deployments. Disable force-push on deployment branches. A single developer should not be able to push directly to production without review.
6. **Sign and verify artifacts.** Build artifacts (binaries, container images, packages) should be signed at build time and verified before deployment. If you cannot prove that the artifact in production came from your pipeline and not from an attacker's laptop, your deployment integrity is unverifiable. Use Sigstore, GPG, or platform-native signing.
7. **Generate and publish an SBOM.** A Software Bill of Materials records every dependency in the final artifact. It enables downstream consumers to check for known vulnerabilities without reverse-engineering your build. SPDX and CycloneDX are the standard formats.
8. **Isolate build environments.** Each pipeline run should start from a clean, ephemeral environment. No state leaks between runs. Build caches are acceptable for performance but must not carry secrets or mutable state from previous builds. Shared, long-lived build agents accumulate risk.
9. **Audit pipeline runs.** Every pipeline execution should produce a log that records: who triggered it (or what event), what commit was built, what secrets were accessed, what artifacts were produced, and where they were deployed. These logs are the audit trail for your software supply chain.
10. **Test the pipeline itself.** Pipeline misconfigurations are bugs. Test that branch protection rules are enforced, that secret access is scoped correctly, that artifact signing produces verifiable signatures, and that deployment gates actually block bad builds.

## Anti-patterns
- Workflow files that are editable without code review or branch protection.
- Using `actions/checkout@v4` (mutable tag) instead of pinning to a commit SHA.
- A single long-lived CI token with admin access to the entire organization.
- Secrets stored as plain-text environment variables in committed YAML files.
- A main branch that accepts direct pushes without review or CI checks.
- Deploying unsigned container images pulled from a public registry by tag.
- Build agents that are shared, long-lived, and accumulate state across runs.
- No audit log of what the pipeline deployed, when, or from which commit.
- "We trust the CI environment" as justification for skipping security controls inside the pipeline.

## References
- OWASP ASVS V14 -- Configuration
- OWASP CI/CD Security Cheat Sheet
- CWE-829 -- Inclusion of Functionality from Untrusted Control Sphere
- SLSA (Supply-chain Levels for Software Artifacts) framework
- OpenSSF Scorecard
- NIST SSDF (Secure Software Development Framework)
