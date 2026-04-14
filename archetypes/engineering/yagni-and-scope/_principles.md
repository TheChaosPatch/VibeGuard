---
schema_version: 1
archetype: engineering/yagni-and-scope
title: YAGNI and Scope Discipline
summary: Build what the current requirement needs; speculative generality is a liability, not an investment.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - yagni
  - scope
  - speculative-generality
  - over-engineering
  - rule-of-three
  - minimum-viable
  - feature-creep
  - premature-optimization
  - kiss
  - simplicity
  - dead-code
  - unused-feature
related_archetypes:
  - engineering/walking-skeleton
  - engineering/dry-and-abstraction
  - engineering/refactoring-discipline
  - engineering/module-decomposition
references:
  book: "The Pragmatic Programmer — Hunt & Thomas"
  book_2: "Extreme Programming Explained — Kent Beck"
  article: "Martin Fowler — Yagni (martinfowler.com/bliki/Yagni.html)"
---

# YAGNI and Scope Discipline -- Principles

## When this applies
Every time you are about to add a parameter, a flag, an abstraction, a configuration option, a new module, or an extensibility point that is not required by the feature being built right now. Also during design review, when someone says "what if later we want to...". YAGNI -- "You Aren't Gonna Need It" -- is the counterweight to the engineer's instinct to generalize early. The rule is specifically hardest when the speculative feature feels small and the generalization feels elegant; those are the cases where the cost of wrong guesses accumulates most.

## Architectural placement
YAGNI operates at every scale: in a single function (don't add a parameter "just in case"), in a module (don't add an abstraction for a second implementation that may never exist), in a system (don't build a plugin architecture for a product with one integration). It is the daily companion to bootstrapping (which says *what* to defer) and to module-decomposition (which says *how* to separate what you did build). When you violate YAGNI, you pay three times: writing the speculative code, maintaining it through refactors, and finally removing it when the future arrives differently than you guessed.

## Principles
1. **Build for the requirement in hand.** Code the simplest thing that satisfies the current, concrete, documented requirement. Not the requirement-with-a-generalization, not the requirement-plus-likely-sequel. If a user story says "export orders as CSV", build CSV export; do not build a pluggable export framework that "could support JSON later."
2. **Rule of three.** The first time you write something, write it directly. The second time a similar need appears, duplicate with mild discomfort. The third time, abstract. Abstractions built from three real examples fit all three; abstractions built from one imagined example fit zero.
3. **Wrong abstractions are worse than duplication.** Duplicated code has a low, linear cost (change three places instead of one). The wrong abstraction has a high, compounding cost (every user fights its shape, every extension bends it further from its original intent). Removing a premature abstraction often means rewriting every call site. Prefer duplication until the shape is obvious.
4. **Unused code rots fast.** Speculative features are not exercised by tests or by users, so they drift out of sync with the rest of the codebase. A year later they contain bugs no one knows about, based on assumptions that no longer hold. They are landmines, not investments.
5. **The cost of "adding later" is lower than you think.** The standard argument for speculative generality is "it'll be harder to add later." In practice, adding a feature to well-factored code is nearly always easier than extracting the right abstraction from a speculative one. Small well-factored code is a flexible foundation.
6. **Configurability is a tax.** Every config option you expose is a contract you must preserve, a combinatoric explosion you must test, and a cognitive burden on every reader. Do not add a config option because the feature "might need to be toggled." Add it when someone needs to toggle it.
7. **Prefer deletion to generalization.** When you see code that "might be useful someday", delete it. It is preserved in git history; it is not preserved in the shape of the codebase. Dead code confuses readers, slows builds, and blocks refactors.
8. **Reject speculative flexibility in review.** When reviewing a PR, ask: "Is this abstraction required by the change, or anticipated for the future?" If anticipated, push back. The pressure to add "just in case" flexibility is constant; the discipline to say no must be equally constant.
9. **Scope is a design decision.** "What we are not building" is as much a design statement as "what we are building." Explicit non-goals in an ADR or design doc prevent scope creep from consensus drift -- they force anyone adding scope to say so.

## Anti-patterns
- A function with seven parameters, five of which the only caller passes as defaults, because "someone might need them."
- A plugin architecture with one plugin, because "we might add more backends later" -- the indirection costs every reader every day for a future that may never arrive.
- Configuration options that no deployment actually overrides, preserved "for flexibility."
- Generic repositories, generic controllers, generic services built before a second concrete instance exists.
- "Reserved for future use" fields in APIs and schemas that gather sediment for years.
- Abstract base classes with one concrete subclass, created because "OOP says to program to interfaces."
- Premature performance optimization -- caches, pools, batching -- added before measurement showed a problem.
- Feature flags for features that are not yet designed, "so we can toggle them when we build them."

## References
- Martin Fowler -- "Yagni" (martinfowler.com/bliki/Yagni.html)
- Kent Beck -- *Extreme Programming Explained*
- Andrew Hunt & David Thomas -- *The Pragmatic Programmer*
- Sandi Metz -- "The Wrong Abstraction" (sandimetz.com/blog/2016/1/20/the-wrong-abstraction)
- John Ousterhout -- *A Philosophy of Software Design* (on deep vs shallow modules)
