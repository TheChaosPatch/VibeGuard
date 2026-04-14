---
schema_version: 1
archetype: engineering/refactoring-discipline
title: Refactoring Discipline
summary: Refactor in small, behavior-preserving steps under a green test suite; never mix refactor with feature work.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - refactoring
  - behavior-preserving
  - small-steps
  - safety-net
  - rename
  - extract-method
  - inline-method
  - move-method
  - characterization-test
  - legacy-code
  - technical-debt
  - code-smell
related_archetypes:
  - engineering/testing-strategy
  - engineering/commit-hygiene
  - engineering/dry-and-abstraction
  - engineering/module-decomposition
  - engineering/naming-and-readability
references:
  book: "Refactoring — Martin Fowler"
  book_2: "Working Effectively with Legacy Code — Michael Feathers"
  book_3: "Five Lines of Code — Christian Clausen"
---

# Refactoring Discipline -- Principles

## When this applies
Whenever you are about to change code structure -- renaming, extracting, inlining, moving, splitting, merging, reshaping -- without changing observable behavior. Also before adding a feature in tangled code: make the change easy first, then make the easy change. And after a feature lands in a hurry: pay down the structure debt while the context is fresh. Refactoring is not "rewriting"; it is a sequence of tiny behavior-preserving transformations, each verified by tests, each commit reversible.

## Architectural placement
Refactoring is the practice that keeps a codebase malleable over time. Without it, every feature settles into whatever shape emerged during its hurried first implementation, and the codebase accretes rigidity until changes become dangerous. Disciplined refactoring is how architecture changes incrementally rather than through rewrites. It is the tool that makes module-decomposition and interface-first-design living rather than frozen -- boundaries redrawn as understanding grows, abstractions extracted when the third example arrives.

## Principles
1. **Refactor means behavior preserved.** A refactor changes structure, not behavior. If observable behavior changes -- returned values, side effects, error messages, timing -- it is not a refactor, it is a rewrite. Even "this edge case seems wrong, let me fix it while I'm here" stops being a refactor. Do one at a time.
2. **Small steps, all the way down.** Each refactor step is small enough that you can reason about its correctness without running tests, and small enough that the tests catch anything you missed. Rename. Extract. Inline. Move. Not "refactor this module" but "extract this method, then run tests, then commit." The safety comes from the smallness, not from the cleverness.
3. **Never mix refactor with feature work.** Two commits: one pure refactor (tests unchanged), one feature addition on top. Mixing them produces diffs where behavior changes are hidden among renames, making review impossible and bisecting a disaster. If a feature requires restructuring, restructure first, land it, then add the feature.
4. **Tests are the safety net.** Refactor under a green test suite. If coverage of the code you are changing is thin, add characterization tests that pin down current behavior before refactoring. The tests may later be replaced by better ones, but in the moment of the refactor, they are what catches mistakes.
5. **Use the tool, not the keyboard.** IDE rename, extract, move refactors are mechanical and reliable. Manual search-replace across files is error-prone. Tools that understand the AST protect invariants the keyboard cannot see.
6. **Commit after every green step.** Each passing, behavior-preserving step is a commit. The history becomes a paper trail of tiny reversible moves; if a step turns out to have introduced a bug, `git bisect` finds it in seconds. Big-bang refactor commits hide exactly which step went wrong.
7. **Rule of thumb: "make the change easy, then make the easy change."** When a feature is hard to add, the problem is usually the current shape. Refactor until the feature becomes a local, obvious addition. Trying to add the feature to tangled code simultaneously refactor *and* add -- and the tangle gets worse.
8. **Delete is a refactor too.** Removing dead code is a behavior-preserving change. Prune unused parameters, unreachable branches, commented-out blocks, obsolete abstractions. The smaller the codebase you walk through, the easier every other change becomes.
9. **Characterization tests for legacy code.** Before refactoring code with no tests, pin its current behavior (bugs and all) with tests that describe what the code actually does. These tests are disposable -- they go away as real behavior tests replace them -- but they are the only way to refactor safely in the meantime.
10. **Know when to stop.** Refactoring is not a terminal state. Aim for "clean enough for the current work," not "perfect." A week-long refactor is rarely a better use of time than shipping the feature, then refactoring incrementally on subsequent changes.

## Anti-patterns
- PRs titled "Feature X + some refactoring" where behavior changes hide among renames and extracts -- reviewers cannot tell what is intentional.
- "Big rewrite" refactors that turn into weeks-long branches diverging from main, producing merge conflicts that dominate the cost.
- Renaming across the codebase with grep + sed and no test verification, silently breaking references inside strings, comments, or reflection.
- Changing method behavior "while the test is already broken from a refactor" -- compounding bugs until no one can tell which change caused what.
- Refactoring code whose tests you have not read, assuming the tests are sufficient -- characterization is not an assumption, it is a verification.
- Leaving commented-out code behind "just in case" -- the commented block is a false signal to future readers and eternal cruft in the file.
- A `refactor` commit that also bumps dependency versions, tweaks config, adjusts CI -- the commit message lies about what the commit does.
- Treating refactor as optional when the feature is "urgent" -- the urgency recurs next week, and the tangle grows, and velocity steadily decays.

## References
- Martin Fowler -- *Refactoring: Improving the Design of Existing Code*
- Michael Feathers -- *Working Effectively with Legacy Code*
- Christian Clausen -- *Five Lines of Code*
- Kent Beck -- *Tidy First?* (micro-refactoring discipline)
- Arlo Belshee -- "Naming is a Process" (microscopic refactor steps)
