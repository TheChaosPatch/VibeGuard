---
schema_version: 1
archetype: architecture/supply-chain-security
title: Supply Chain Security
summary: Establishing provenance, integrity verification, and trust controls across all software dependencies, build artifacts, and deployment pipelines.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - supply-chain
  - sbom
  - provenance
  - signing
  - attestation
  - slsa
  - dependency
  - artifact-integrity
  - build-pipeline
  - typosquatting
related_archetypes:
  - persistence/dependency-management
  - architecture/secure-ci-cd
references:
  owasp_asvs: V14.2
  owasp_cheatsheet: Software Supply Chain Security Cheat Sheet
  cwe: "1395"
---

# Supply Chain Security -- Principles

## When this applies
Every system that incorporates third-party code -- which is every system. The 2020 SolarWinds attack, the 2021 Log4Shell vulnerability, and the 2024 XZ Utils backdoor demonstrated that supply chain attacks bypass traditional application security controls entirely. If your system depends on packages, uses a build pipeline, or produces deployable artifacts, your software supply chain is an attack surface.

## Architectural placement
Supply chain security spans the development phase (dependency selection and pinning), the build phase (hermetic builds, artifact signing), the distribution phase (registry controls, signature verification), and the deployment phase (admission verification, SBOM ingestion). It is the security discipline applied to the infrastructure that produces your software, not just to the software itself.

## Principles
1. **Generate and maintain a Software Bill of Materials (SBOM).** An SBOM is an authoritative, machine-readable inventory of every component in your software: direct dependencies, transitive dependencies, and their exact versions. Generate the SBOM as part of every build. The SBOM enables vulnerability scanning, license compliance, and rapid impact assessment when a new CVE is published (e.g., "does our SBOM contain log4j 2.14?").
2. **Pin dependencies to exact, verified versions.** Lock files are not optional. Package managers that support lock files (npm, Cargo, Pipenv, Go modules) must use them. Pin to exact versions, not version ranges. A range that resolves to a different version on a different day is a build reproducibility failure and a supply chain risk. Hash verification (integrity hashes in lock files) ensures the package content has not changed since pinning.
3. **Verify artifact provenance before use.** Before consuming a build artifact, container image, or binary, verify that it was produced by a known, trusted pipeline from known source code -- and that the artifact has not been tampered with in transit. Provenance attestations (SLSA) and artifact signatures (Sigstore/Cosign) provide this assurance in a machine-verifiable form.
4. **Sign all artifacts your pipeline produces.** Build artifacts, container images, and release binaries produced by your pipeline are signed with a key controlled by your organization. Consumers of those artifacts verify the signature before use. Signing creates accountability and tamper detection -- an artifact that does not match the signature was either not produced by your pipeline or was modified after signing.
5. **Use SLSA to frame build pipeline integrity.** The Supply-chain Levels for Software Artifacts (SLSA) framework provides a maturity model for build integrity. At minimum, achieve SLSA Level 2: build scripts are version-controlled, builds produce provenance attestations, and the provenance is signed. SLSA Level 3 adds hermetic builds (no network access during build, reproducible outputs) and stronger provenance guarantees.
6. **Evaluate dependencies before adopting them.** Adoption criteria include: active maintenance, number of maintainers (single-maintainer packages are high risk for account takeover), published security policy, known vulnerabilities, and license compatibility. "There is a package for this" is not adoption criteria. The transitive dependency tree grows with every dependency added -- evaluate the full tree, not just the direct dependency.
7. **Monitor dependencies continuously for new vulnerabilities.** Pinning a dependency at a clean version does not make it permanently safe. Subscribe to vulnerability feeds for your package ecosystems. Use automated dependency update tooling (Dependabot, Renovate) to receive vulnerability alerts and PRs. The mean time to patch known vulnerabilities in dependencies is a security metric.
8. **Protect the build pipeline with the same rigor as production.** The CI/CD system that produces your software has privileged access to your source code, signing keys, and deployment targets. Compromise of the build pipeline is compromise of every artifact it produces. Build pipelines must have strong authentication, least-privilege permissions, immutable execution environments, and comprehensive audit logs.
9. **Guard against typosquatting and dependency confusion.** Attackers register packages with names similar to legitimate packages (typosquatting) or with the same name as an internal package on public registries (dependency confusion). Mitigations include: verifying package names before adding dependencies, using private registry proxies that prefer internal packages, and namespace reservation on public registries.

## Anti-patterns
- No lock file: dependencies resolve to "latest compatible" on every build, silently changing.
- Pulling dependencies directly from the internet during production builds with no caching or verification.
- Container images built `FROM scratch` by pulling from Docker Hub without signature verification.
- No SBOM: a CVE is published and the team cannot quickly determine which services are affected.
- Build pipelines with production deployment keys checked into the pipeline configuration file.
- Using curl-to-bash installation scripts for build tool setup without verifying the script's integrity.
- A single long-lived signing key with no rotation plan and no revocation procedure.
- Trusting a package because it has many downloads -- npm typosquatting packages have accumulated millions of downloads.
- No automated dependency update process: teams manually update dependencies once a year or not at all.

## References
- OWASP ASVS V14.2 -- Dependency
- OWASP Software Supply Chain Security Cheat Sheet
- CWE-1395 -- Dependency on Vulnerable Third-Party Component
- SLSA Framework -- Supply-chain Levels for Software Artifacts (slsa.dev)
- Sigstore / Cosign -- Keyless artifact signing
- NIST SP 800-161r1 -- Cybersecurity Supply Chain Risk Management
- OpenSSF Scorecard -- Automated supply chain risk assessment for open-source projects
