---
schema_version: 1
archetype: architecture/least-privilege
title: Least Privilege
summary: Granting every principal -- human or machine -- only the minimum permissions required to perform its current function.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - least-privilege
  - rbac
  - permissions
  - scope
  - minimal
  - access
  - just-in-time
  - standing-access
  - segregation-of-duties
  - entitlement
related_archetypes:
  - auth/authorization
  - architecture/zero-trust
references:
  owasp_asvs: V4.1
  owasp_cheatsheet: Authorization Cheat Sheet
  cwe: "250"
---

# Least Privilege -- Principles

## When this applies
Every system that has identities -- users, service accounts, API keys, CI runners, database users, cloud IAM roles, administrative operators. Least privilege is not optional for sensitive systems; it is the foundational access control principle from which all other access control decisions derive. If any principal in your system has broader access than its function requires, this archetype applies.

## Architectural placement
Least privilege is enforced at every layer where access decisions are made: application-layer authorization (routes, business logic, data filters), infrastructure-layer IAM (cloud roles, database grants, Kubernetes RBAC), network-layer controls (firewall rules, security groups), and operational access (break-glass procedures, just-in-time access). It is a design constraint applied at system design time and enforced continuously through access reviews.

## Principles
1. **Grant the minimum permissions required for the task, not the role.** The question is not "what permissions does a developer normally need?" but "what permissions does this specific task require?" A deployment pipeline needs to write to one S3 bucket -- not read/write all buckets. A background job needs to read one database table -- not query the entire schema.
2. **Prefer time-limited over standing access.** Standing access (permanent permissions that persist until revoked) accumulates over time and is rarely audited. Prefer just-in-time access: permissions granted on demand, scoped to the requesting principal and task, and auto-revoked after the task completes or a time limit expires. Privileged access should be exceptional, not default.
3. **Scope credentials to their consumer.** Each service, CI job, and external integration has its own credential scoped to exactly the permissions it needs. A credential shared between two services has the union of their permissions -- which is always more than either needs. Shared credentials also make it impossible to attribute actions to a specific caller.
4. **Enforce segregation of duties for sensitive operations.** No single principal should be able to initiate and approve a sensitive operation alone. Deployment to production requires a different principal than the one who wrote the code. Creating a payment also requires a different principal to approve it. Segregation of duties prevents insider threat and credential compromise from being sufficient for a full attack.
5. **Remove permissions when they are no longer needed.** User role changes, project completions, and service deprecations all create stale permissions. Design a lifecycle process: when a principal's function changes, its permissions change with it -- not when an annual access review catches it a year later. Automate deprovisioning where possible.
6. **Apply allowlist-based access control, not denylist.** Granting all permissions and then removing the ones that are dangerous misses permissions that are not yet known to be dangerous. Start from zero permissions and add only what is explicitly required. Every permission granted should have a documented reason.
7. **Apply least privilege to data access, not just API access.** A service that reads user records for a specific operation should query only the columns it needs, filtered to only the rows it is authorized for. Row-level security, column-level grants, and database views enforce data-layer least privilege independently of application-layer authorization.
8. **Audit access grants and review them periodically.** Maintain a record of what permissions each principal holds and why. Review access periodically -- quarterly at minimum for privileged access, annually for standard access. Revoke anything that cannot be justified. Access reviews that happen only during audits are access reviews that find no problems.
9. **Treat over-permission as a vulnerability.** An identity with more permissions than its function requires is a pre-positioned privilege escalation. If that identity is compromised, the attacker inherits the over-permission. Design and code review should flag over-permission with the same severity as other vulnerabilities.

## Anti-patterns
- Giving all developers production database credentials with read/write on all tables.
- A CI/CD pipeline with cloud administrator permissions because "it was easier to set up."
- Service accounts that accumulate permissions over time as services grow without corresponding cleanup.
- Shared credentials between services, making attribution and revocation impossible.
- No access review process -- permissions are permanent once granted.
- Denylist-based IAM policies that attempt to remove dangerous permissions from a broad grant.
- A single privileged operator account shared by the entire operations team.
- Application roles modeled on org chart titles rather than on the specific operations each role performs.

## References
- OWASP ASVS V4.1 -- General Access Control Design
- OWASP Authorization Cheat Sheet
- CWE-250 -- Execution with Unnecessary Privileges
- NIST SP 800-53 AC-6 -- Least Privilege
- Saltzer & Schroeder, "The Protection of Information in Computer Systems" (1975)
- ISO 27001 Annex A.9 -- Access Control
