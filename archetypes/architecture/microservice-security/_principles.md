---
schema_version: 1
archetype: architecture/microservice-security
title: Microservice Security
summary: Securing service-to-service communication, auth boundaries, and data isolation in distributed microservice architectures.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - microservice
  - mtls
  - service-mesh
  - sidecar
  - boundary
  - istio
  - workload-identity
  - east-west
  - blast-radius
  - token-propagation
related_archetypes:
  - architecture/zero-trust
  - architecture/defense-in-depth
references:
  owasp_asvs: V1.1
  owasp_cheatsheet: Microservices Security Cheat Sheet
  cwe: "306"
---

# Microservice Security -- Principles

## When this applies
Any system composed of independently deployable services that communicate over a network. Microservice architectures dramatically expand the internal attack surface compared to monoliths: service-to-service communication, per-service secrets, distributed authorization, and the challenge of maintaining consistent security policy across dozens or hundreds of independently deployed components.

## Architectural placement
Microservice security operates at three layers: the communication layer (mTLS, network policy), the identity layer (workload identity, token propagation), and the data layer (per-service storage isolation, secret management). A service mesh enforces the communication and identity layers as infrastructure so that individual services do not implement them ad hoc. The data layer is enforced at the persistence level.

## Principles
1. **Authenticate every service-to-service call.** Service identity is not the same as network location. Every call between services must carry a credential that proves who the caller is. mTLS provides mutual authentication at the transport layer -- the server proves its identity to the caller and the caller proves its identity to the server, before any application data is exchanged.
2. **Use a service mesh to enforce policy as infrastructure.** Service meshes (Istio, Linkerd, Consul Connect) apply mTLS, traffic policy, and observability as a sidecar, independent of application code. This ensures that security policy is consistently enforced even when teams release independently. Services should not implement their own transport-level auth.
3. **Issue workload identities, not shared credentials.** Each service gets a unique, short-lived, cryptographically verifiable identity (SPIFFE/SPIRE, cloud IAM workload identity). No shared API keys across services. No static passwords in environment variables. Compromising one service's credential does not compromise another's.
4. **Propagate user context explicitly, not implicitly.** When a user request flows through multiple services, the original user's identity and authorization context travel with it -- as a signed, validated token (JWT), not as a plain header that any service can forge. Each service re-validates the token; it does not trust a forwarded claim from an upstream service.
5. **Apply authorization at every service boundary.** The API gateway validates authentication. Each downstream service validates authorization for its own resources. No service assumes that because the gateway allowed the request, every operation within it is authorized. A compromised upstream service must not be able to escalate privileges in a downstream service.
6. **Isolate data stores per service.** Services should own their data. A service's database is not accessed directly by other services -- it is accessed through the owning service's API. Shared databases create implicit couplings and mean a compromised service can access another's data directly.
7. **Limit the blast radius of a compromised service.** Design for the assumption that any service can be compromised. The service's credentials grant access only to its own resources. Network policy allows only documented communication paths. A compromised service cannot reach databases it does not own or call endpoints it is not supposed to call.
8. **Use short-lived secrets and rotate them automatically.** Service credentials, database passwords, and API keys are issued with a short TTL and rotated automatically by a secrets management system. Rotation is infrastructure behavior, not a manual operational task.
9. **Centralize and aggregate observability across services.** Distributed tracing, centralized logging, and metrics aggregation are security controls in a microservice architecture. Without them, an attacker can move laterally and the incident response team cannot reconstruct the timeline. Correlation IDs must be propagated across all service calls.

## Anti-patterns
- Flat internal networking where any service can call any other service without restriction.
- Shared service credentials (one API key used by all services to call a shared dependency).
- Authorization logic only at the gateway; downstream services trust the gateway's decision unconditionally.
- Services accessing each other's databases directly, bypassing the owning service's API.
- Static secrets in environment variables with no rotation mechanism.
- User identity passed as a plain header (e.g., `X-User-Id: 42`) without a signed token, allowing any service to impersonate any user.
- No distributed tracing, making lateral movement invisible during incident response.
- Service mesh bypassed by services communicating directly on ports not covered by the mesh.

## References
- OWASP ASVS V1.1 -- Architecture, Design and Threat Modeling
- OWASP Microservices Security Cheat Sheet
- CWE-306 -- Missing Authentication for Critical Function
- SPIFFE/SPIRE -- Secure Production Identity Framework for Everyone
- NIST SP 800-204 -- Security Strategies for Microservices
- NSA/CISA Kubernetes Hardening Guidance
