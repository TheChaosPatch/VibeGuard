---
schema_version: 1
archetype: architecture/secure-configuration
title: Secure Configuration
summary: Establishing hardened, auditable configuration baselines that default to secure, resist drift, and are enforced continuously.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - configuration
  - hardening
  - defaults
  - drift
  - baseline
  - cis
  - immutable
  - secrets-management
  - environment-separation
  - configuration-as-code
related_archetypes:
  - architecture/secure-ci-cd
  - crypto/tls-configuration
references:
  owasp_asvs: V14.1
  owasp_cheatsheet: Infrastructure as Code Security Cheat Sheet
  cwe: "16"
---

# Secure Configuration -- Principles

## When this applies
Every system that runs on infrastructure, uses a framework, or depends on external services -- which is every system. Security misconfiguration is consistently the most prevalent finding in security assessments and penetration tests, and the easiest to prevent. If your system has configuration files, environment variables, deployment manifests, or runtime settings, this archetype applies.

## Architectural placement
Secure configuration spans the build phase (how defaults are set in code), the packaging phase (what configuration is baked into images or packages), the deployment phase (what configuration is injected at runtime), and the operational phase (how configuration drift is detected and remediated). Configuration is infrastructure code and belongs in version control, code review, and automated verification pipelines.

## Principles
1. **Default to secure.** Every configuration option that has a secure and an insecure state defaults to the secure state. Debug mode off by default. Verbose error messages off by default. Administrative interfaces disabled by default. Sample credentials deleted before packaging. Developers opt out of security explicitly and reviewably; they do not opt in.
2. **Separate configuration from code, and secrets from configuration.** Application code does not contain configuration values. Configuration does not contain secrets. Secrets are injected at runtime from a secrets management system (Vault, AWS Secrets Manager, Azure Key Vault) and are never present in version control, environment variable listings, or container images.
3. **Manage configuration as code.** All configuration -- application settings, infrastructure parameters, security group rules, TLS settings -- lives in version-controlled files. Changes to configuration go through the same review process as changes to code. Config that exists only in a console or a team member's head is config that cannot be audited or reproduced.
4. **Establish and enforce hardened baselines.** Define a configuration baseline (CIS Benchmarks, DISA STIGs, cloud provider security baselines) for each platform and runtime. Automate compliance checks against the baseline in CI and in runtime monitoring. Deviation from the baseline is a finding, not an option.
5. **Separate configuration by environment.** Production configuration is not the same as development configuration. Database credentials, feature flags, log verbosity, and external service endpoints differ by environment. Environment-specific configuration is injected at deployment time -- not stored in a single file with `if env == "prod"` branches.
6. **Eliminate unused components, features, and endpoints.** Frameworks ship with sample applications, default admin interfaces, and enabled-by-default features. Remove everything that is not needed by the production workload. An unused, vulnerable component is a vulnerability even if the application never calls it directly.
7. **Detect and alert on configuration drift.** Running configuration diverges from the documented baseline through manual changes, hotfixes, and infrastructure drift. Use configuration management tools (Ansible, Chef, Terraform), cloud Config services (AWS Config, Azure Policy), and CIS benchmark scanners to continuously detect drift. Drift is an incident, not a maintenance item.
8. **Rotate secrets on a schedule, not only on disclosure.** Secrets have a maximum lifetime defined before deployment. Rotation is automated where possible (database credential rotation, certificate renewal via ACME) and procedure-driven where not. Waiting until a secret is compromised to rotate it is too late.
9. **Harden TLS and cryptographic configuration explicitly.** TLS version minimums, cipher suites, certificate validation behavior, and HSTS policy are explicitly configured -- not left to framework defaults that may change across versions or be less restrictive than required. See `crypto/tls-configuration` for the full treatment.
10. **Inventory and manage all configuration dependencies.** External services, SaaS integrations, and cloud APIs that the system depends on have their own configuration surfaces. Capture these dependencies, know their defaults, and apply the same hardening principles to them.

## Anti-patterns
- Deploying to production with `DEBUG=true` or equivalent debug flags enabled.
- Secrets in version control -- even in "private" repositories or in git history.
- Configuration changes made directly in the production console, bypassing version control and review.
- Using the same configuration (including credentials) across dev, staging, and production.
- Leaving framework defaults in place ("the framework is secure by default") without verifying what those defaults actually are.
- Sample applications, example endpoints, or default admin dashboards accessible in production.
- No alerting when production configuration deviates from the baseline.
- Manual certificate renewal processes that rely on a human to remember before expiry.
- Monolithic configuration files with no environment separation, committed to the repository.

## References
- OWASP ASVS V14.1 -- Build and Deploy
- OWASP Infrastructure as Code Security Cheat Sheet
- CWE-16 -- Configuration
- CIS Benchmarks -- Platform-specific hardening guidance
- NIST SP 800-123 -- Guide to General Server Security
- OWASP Top 10 A05:2021 -- Security Misconfiguration
