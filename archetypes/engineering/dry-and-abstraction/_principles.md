---
schema_version: 1
archetype: engineering/dry-and-abstraction
title: DRY and Abstraction Discipline
summary: Don't Repeat Yourself — but don't abstract prematurely; the wrong abstraction is worse than duplication.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - dry
  - duplication
  - abstraction
  - rule-of-three
  - premature-abstraction
  - wet
  - wrong-abstraction
  - code-reuse
  - refactoring
  - generalization
  - design-principle
  - copy-paste
related_archetypes:
  - engineering/yagni-and-scope
  - engineering/refactoring-discipline
  - engineering/module-decomposition
  - engineering/interface-first-design
references:
  book: "The Pragmatic Programmer — Hunt & Thomas"
  article: "Sandi Metz — The Wrong Abstraction"
  article: "Martin Fowler — Refactoring"
---

# DRY and Abstraction Discipline -- Principles

## When this applies
When you notice two pieces of code that look alike and feel the urge to extract a shared helper, base class, generic method, or template. When a teammate proposes a new abstraction layer to "clean up duplication." When existing abstractions feel awkward to use and every caller is working around them. DRY (Don't Repeat Yourself) is one of the most misquoted principles in software; its original meaning is about *knowledge*, not about textual similarity, and confusing the two produces abstractions that actively hurt.

## Architectural placement
DRY lives in tension with YAGNI. YAGNI says "do not build what is not yet needed"; DRY says "do not duplicate knowledge." The resolution is the rule of three: duplicate until a real pattern emerges, then abstract to capture the *knowledge*, not the textual shape. Wrong abstractions propagate through every caller and are harder to undo than duplicated code. This archetype governs the moment-to-moment choice between "extract this" and "leave it alone" that every engineer faces daily.

## Principles
1. **DRY is about knowledge, not text.** Two pieces of code that look the same but encode different business rules are not duplication -- they are coincidence. If "shipping cost for orders" and "tax for refunds" happen to use the same formula today, that is accidental. If the formulas drift tomorrow (and they often do), a shared helper forces you to add branching that undoes the abstraction. Duplicate accidental similarity; unify only genuine shared knowledge.
2. **Rule of three.** The first time you write something, write it directly. The second time a similar need arises, duplicate and mark the discomfort. The third time, abstract -- now you have three real examples to shape the abstraction around. Abstracting from two examples is a coin flip; abstracting from three examples captures the real pattern.
3. **Wrong abstractions are expensive to remove.** Once an abstraction has many callers, each caller has bent its use case to fit the abstraction's shape. Removing the abstraction means straightening every caller, which is a cross-cutting change much larger than the original duplication. A Sandi Metz maxim: "duplication is far cheaper than the wrong abstraction."
4. **WET before DRY.** "Write Everything Twice" is not a mistake -- it is a deliberate deferral. The second occurrence makes the first less mysterious; the third reveals the variation. Extracting after three is cheap refactoring; extracting after one is speculative design.
5. **If extending an abstraction needs a new flag, rethink.** When adding a seventh parameter or a `mode` enum to bend an abstraction toward a new use case, stop. The abstraction no longer fits. Inline it, duplicate the relevant parts, and let a new pattern emerge naturally. Flags accreted onto abstractions are a slow-motion recognition that the extraction was wrong.
6. **Shape abstractions from real callers.** An abstraction designed for "any possible caller" fits no caller well. Build abstractions by looking at concrete call sites, asking what they all need, and lifting only that. Anything one specific caller needs stays with that caller.
7. **Prefer deletion to deduplication.** Before extracting a shared helper, ask: can one of the duplicated copies be deleted? Dead code, speculative paths, and "just in case" variants often outnumber the genuinely useful ones. Removing waste often removes the duplication problem.
8. **Inline to learn, extract to commit.** When an abstraction feels wrong, inline it first -- push the code back into each caller. The clarity that returns often reveals the real pattern, which you then extract with intention. Inline is refactor, not regression.
9. **Duplication in tests is often acceptable.** Tests prize clarity over concision. A test that repeats setup is readable top-to-bottom; a test that shares helpers with five other tests requires understanding the helpers before understanding the test. Duplicate test data, duplicate arrange blocks, abstract sparingly.

## Anti-patterns
- Extracting a function the moment two similar lines appear, before knowing whether they represent the same concept or only look alike.
- Base classes with a dozen abstract methods, half of which most subclasses leave empty -- the hierarchy was too ambitious for the real shape.
- Generic utilities with twenty flags and modes, each added to accommodate one more caller -- the abstraction has become a switchboard.
- "Clever" meta-abstractions (reflection-driven handlers, metaprogramming pipelines) that dedupe three lines and cost every reader an hour of comprehension.
- Helpers named `processData`, `doThing`, `handleRequest` in a `shared/` directory, imported by modules across the codebase with no clear contract.
- Keeping a wrong abstraction alive by adding more flags rather than inlining, because "so many places use it" -- the reluctance to inline is precisely why it must be inlined.
- Enforcing DRY on test fixtures, producing a shared base with setUp/tearDown that no single test fully understands.

## References
- Andrew Hunt & David Thomas -- *The Pragmatic Programmer* (original DRY formulation)
- Sandi Metz -- "The Wrong Abstraction" (sandimetz.com/blog/2016/1/20/the-wrong-abstraction)
- Martin Fowler -- *Refactoring* (Inline Method, Extract Method, Rename)
- John Ousterhout -- *A Philosophy of Software Design* (depth vs. shallowness in abstractions)
- Kent Beck -- "Rule of Three" (in *Smalltalk Best Practice Patterns*)
