---
schema_version: 1
archetype: architecture/container-security
title: Container Security
summary: Hardening container images, runtimes, and orchestration platforms to reduce attack surface and limit blast radius.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - container
  - docker
  - kubernetes
  - k8s
  - pod
  - image
  - registry
  - rootless
  - admission
  - seccomp
  - namespace
  - rbac
related_archetypes:
  - architecture/least-privilege
  - architecture/secure-ci-cd
references:
  owasp_asvs: V14.4
  owasp_cheatsheet: Docker Security Cheat Sheet
  cwe: "269"
---

# Container Security -- Principles

## When this applies
Any system packaging workloads as containers -- whether running on Kubernetes, managed container services, or bare Docker hosts. Containers introduce a distinct threat model: shared kernel, image supply chain, registry trust, and orchestration-level access control. The flexibility that makes containers powerful also creates security misconfigurations that are common, severe, and often not caught until after deployment.

## Architectural placement
Container security spans the image build phase (base image, installed packages, secrets), the registry phase (image signing, vulnerability scanning), the runtime phase (security contexts, network policy, resource limits), and the orchestration phase (RBAC, admission control, audit logging). Each phase has controls that cannot be substituted by controls in another phase -- a clean image in an over-privileged pod is still a container security failure.

## Principles
1. **Run containers as non-root.** The process inside the container must run as a non-root user. If a container process is compromised and escapes the container namespace, a root process has a far wider host impact than a non-privileged user. Set `USER` explicitly in the Dockerfile to a numeric UID that does not exist on the host.
2. **Use minimal base images.** Every package in a base image is an attack surface. Prefer distroless or minimal base images (Alpine, distroless/static) over full OS images. If a shell is not needed at runtime, it should not be present. Fewer packages means fewer CVEs and a smaller pivot surface after exploitation.
3. **Never bake secrets into images.** Container images are artifacts that are stored in registries, cached on nodes, and potentially copied to many environments. Build-time secrets (API keys, certificates, passwords) must not be present in any image layer -- including intermediate layers. Use secret mounts at build time (`--mount=type=secret`) and runtime secret injection (Kubernetes Secrets, Vault agent) at deployment time.
4. **Scan images for vulnerabilities in CI and at admission.** Container image scanning must occur in CI before images are pushed and again at admission to the cluster. An image that was clean at build time can accumulate critical CVEs. Registry-side continuous scanning (ECR, GCR, Docker Scout) alerts on new vulnerabilities in already-deployed images.
5. **Sign images and verify signatures at deployment.** Use image signing (Sigstore/Cosign, Notary) to create a verifiable chain of custody from the build pipeline to the cluster. Admission controllers (Kyverno, OPA Gatekeeper) verify signatures before allowing images to run. Unsigned images from unknown registries are rejected.
6. **Enforce security contexts on every pod.** Pod and container security contexts must explicitly set: `runAsNonRoot: true`, `readOnlyRootFilesystem: true`, `allowPrivilegeEscalation: false`, and `capabilities: drop: [ALL]`. Privileged containers are banned unless the workload explicitly requires it and the exception is reviewed.
7. **Apply Kubernetes RBAC with least privilege.** Service accounts are namespaced, have minimal permissions, and are not the default service account. RBAC roles grant the minimum verbs on the minimum resources. `cluster-admin` bindings are documented exceptions, not defaults.
8. **Network-isolate pods with NetworkPolicy.** Default-deny network policy blocks all ingress and egress. Specific allow rules are added for each documented communication path. Pods that do not need to communicate have no network path between them. This limits lateral movement within the cluster after a pod is compromised.
9. **Enforce admission policies with a policy controller.** A policy controller (OPA Gatekeeper, Kyverno) enforces cluster-wide security standards at admission time: no root containers, no privileged pods, required labels, approved registries, resource limits required. Policy violations are blocked at admission, not discovered by audit.
10. **Apply resource limits to every container.** CPU and memory limits prevent a compromised or misbehaving container from exhausting node resources and impacting neighboring workloads. Absence of limits is also a denial-of-service vector.

## Anti-patterns
- Containers running as root (`USER root` or no `USER` directive in the Dockerfile).
- Secrets passed as environment variables baked into the image or visible in `docker inspect`.
- Using `latest` as the image tag -- no pinning, no reproducibility, no auditability.
- `privileged: true` pods without documented justification and compensating controls.
- No NetworkPolicy in the cluster -- every pod can reach every other pod and the Kubernetes API.
- Pulling images from public registries without signature verification or vulnerability scanning.
- A single cluster-wide service account used by all workloads.
- No resource limits, allowing a single container to consume all node resources.
- Container images built from unscanned, upstream base images with known critical CVEs.

## References
- OWASP ASVS V14.4 -- HTTP Security Headers (extended to container runtime)
- OWASP Docker Security Cheat Sheet
- CWE-269 -- Improper Privilege Management
- CIS Docker Benchmark
- CIS Kubernetes Benchmark
- NSA/CISA Kubernetes Hardening Guidance (v1.2)
- NIST SP 800-190 -- Application Container Security Guide
