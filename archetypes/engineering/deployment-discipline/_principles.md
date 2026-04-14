---
schema_version: 1
archetype: engineering/deployment-discipline
title: Deployment Discipline
summary: Small frequent deploys; feature flags decouple deploy from release; safe rollback is non-negotiable.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - deployment
  - release
  - feature-flag
  - canary
  - blue-green
  - rollback
  - zero-downtime
  - continuous-delivery
  - progressive-delivery
  - release-engineering
  - immutable-infrastructure
  - deploy-pipeline
related_archetypes:
  - engineering/continuous-integration
  - engineering/data-migration-discipline
  - engineering/api-evolution
  - engineering/observability
  - architecture/secure-ci-cd
references:
  book: "Continuous Delivery — Humble & Farley"
  book_2: "Accelerate — Forsgren, Humble, Kim"
  article: "Martin Fowler — Feature Toggles"
---

# Deployment Discipline -- Principles

## When this applies
From the walking skeleton's first deploy onward. Deployment discipline applies every time code moves from a developer's machine toward production -- through staging, canary, full rollout. It also applies at the design stage: decisions about data migrations, API versioning, and feature sequencing are deployment decisions. The discipline recognizes that deployment is not an end-of-cycle event; it is a continuous activity that the entire engineering process must accommodate.

## Architectural placement
Deployment sits downstream of CI and upstream of production. The CI pipeline produces a tested artifact; the deploy pipeline promotes that artifact through environments with increasing exposure. Strong deployment discipline -- small units of change, safe rollback, decoupled deploy from release -- is what lets teams ship dozens of times per day without drama. Poor deployment discipline makes every release an event, compresses risk into rare all-hands windows, and trains the team to fear deploying.

## Principles
1. **Small, frequent deploys beat large, rare ones.** A deploy touching ten lines is rarely the source of a production issue that a deploy touching ten thousand lines is. Small deploys isolate blame, narrow rollback scope, and surface problems while context is fresh. Aim for multiple deploys per day per service, not per week.
2. **Decouple deploy from release.** Deploy (ship the binary to production) is separate from release (make the new behavior visible to users). Feature flags are the decoupling mechanism: code ships dark, gets exercised in production under flag-off conditions, then is released by flipping the flag. Deploying without releasing is safe; releasing without deploying is impossible.
3. **Progressive rollout.** A new version reaches 1% of traffic, then 10%, then 50%, then 100% -- with monitoring at every step. A bug is caught at 1% instead of 100%, dropping blast radius by two orders of magnitude. Canary analysis, automatic rollback on metric degradation, and blue-green or rolling strategies are the mechanics.
4. **Rollback is a first-class path, not a heroic effort.** Every deploy has a tested rollback plan. Rolling forward with a fix is often better, but rollback is the floor of safety. A rollback requiring manual surgery on databases or config is not a rollback; it is damage control.
5. **Immutable artifacts.** The binary built by CI is the binary that reaches production -- byte-identical, content-addressed, signed if possible. Never SSH into a box and patch it in place. Immutable artifacts make "what version is running" answerable and rollback deterministic.
6. **Config is versioned and deployed separately from code.** Configuration lives in version control; deploying a config change is its own reviewed, deployable unit. "Changing a setting in the cloud console" is an undocumented change that defeats reproducibility.
7. **The deploy pipeline is code.** Terraform, GitHub Actions, Argo, whatever the tool -- the pipeline is declarative, reviewed, versioned, tested. Manual click-deploys through a UI are a ratchet of tribal knowledge and the failure mode that always strikes the new engineer on-call.
8. **Deploy during business hours, monitored.** Deploys happen when engineers are awake and on-call tooling is responsive. Midnight deploys "because it's low traffic" remove the humans from the monitoring equation; low traffic also means bugs surface later.
9. **Schema changes precede code that requires them; removals follow code that stopped using them.** The expand-contract pattern from data-migration-discipline extends to the deploy pipeline: schema and API contracts are changed in the order that allows the rolling population of old and new servers to interoperate safely.
10. **Every deploy leaves a trail.** Each deploy emits a version marker (log line, metric with version tag, deploy annotation on dashboards). When an incident starts, the first question is "what changed?" -- the deploy timeline should answer in seconds.

## Anti-patterns
- "Release trains" where a month of changes ship on a Friday at 4 PM, with a rollback plan of "hope nothing breaks before Monday."
- Feature flags without cleanup, accumulating dead branches and config sprawl until flag evaluation itself becomes a bug source.
- Deploys that require a human to SSH into a box, run a script, edit a config, and restart a service -- every step a chance to skip or typo.
- Rollback that requires restoring a database snapshot, losing all writes since the deploy -- rollback is effectively impossible and the team never uses it.
- Canary analysis based on "I watched the dashboard for five minutes" -- no automated thresholds, no defined abort criteria, no documented metric tie.
- Unversioned configuration changed through a cloud provider console, leaving no paper trail of who changed what when.
- "Build once, deploy many" violated by recompiling for each environment -- the binary in production is not the binary that passed CI.
- Friday deploys with Saturday/Sunday on-call, because "we needed to get it in before the week ended."
- Deploy pipelines that do not emit version markers, forcing post-mortem forensics to reconstruct what shipped and when.

## References
- Jez Humble & David Farley -- *Continuous Delivery*
- Nicole Forsgren, Jez Humble, Gene Kim -- *Accelerate*
- Martin Fowler -- "Feature Toggles (aka Feature Flags)" (martinfowler.com/articles/feature-toggles.html)
- Google SRE Book -- "Release Engineering" chapter
- Charity Majors -- "Deploys are the #1 Cause of Incidents" (honeycomb.io blog)
