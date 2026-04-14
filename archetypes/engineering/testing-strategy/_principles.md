---
schema_version: 1
archetype: engineering/testing-strategy
title: Testing Strategy
summary: Test pyramid shape; test behavior not internals; deterministic, fast, and independent tests as living specification.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - testing
  - test-pyramid
  - unit-test
  - integration-test
  - end-to-end-test
  - deterministic
  - test-as-spec
  - tdd
  - behavior-testing
  - test-independence
  - flaky-test
  - test-fixture
  - test-double
related_archetypes:
  - engineering/refactoring-discipline
  - engineering/continuous-integration
  - engineering/interface-first-design
  - engineering/walking-skeleton
  - engineering/commit-hygiene
references:
  book: "Growing Object-Oriented Software, Guided by Tests — Freeman & Pryce"
  book_2: "Test-Driven Development by Example — Kent Beck"
  book_3: "xUnit Test Patterns — Gerard Meszaros"
---

# Testing Strategy -- Principles

## When this applies
When designing the test suite for a new system, when the suite for an existing system is slow, flaky, or mistrusted, when a PR adds behavior without tests, and when a bug makes it to production unnoticed. Tests are the executable specification of the system's behavior; a codebase with poor tests is a codebase no one can safely change. The test strategy is decided early -- what kinds of tests, at what layers, with what shape -- because retrofitting a testing culture is harder than retrofitting any single feature.

## Architectural placement
Tests sit alongside the code they verify and in the CI pipeline that gates every change. They are not separate from the codebase; they are part of its shape. A good test strategy enables refactoring (small changes verified quickly), enables continuous delivery (green main is the promise tests enforce), and enables comprehension (tests read as worked examples of the system's behavior). This archetype pairs closely with refactoring-discipline -- tests are the safety net -- and with walking-skeleton -- the first test is the first feature.

## Principles
1. **Test behavior, not internals.** Tests should describe what the system does from the caller's perspective, not how it does it. `should_return_404_when_order_not_found` is a behavior test; `should_call_repository_find_method_with_id` tests an internal detail. Internal tests break every refactor; behavior tests survive them.
2. **Pyramid shape.** Many fast unit tests (pure, isolated, <10 ms each), fewer integration tests (real infrastructure, seconds each), a small number of end-to-end tests (full stack, minutes each). An inverted pyramid -- mostly end-to-end -- is slow, flaky, and hides root causes. A diamond -- mostly integration -- is a warning that units are not really isolated.
3. **Deterministic or not a test.** A test that passes sometimes is worse than no test -- it destroys trust in the whole suite. Remove sources of nondeterminism: wall clocks, random numbers, ordering of iteration, unmocked network, cross-test shared state. If a test cannot be made deterministic, delete it.
4. **Fast enough to run constantly.** The unit suite should run in single-digit seconds so developers run it on every save. If the suite takes minutes, developers batch changes and stop running it -- which defeats its purpose. Speed is a feature of the suite, not an optimization.
5. **Tests are independent.** Any test runs in any order, any subset, alone or parallel, with the same result. Tests that depend on other tests running first are fragile by design. Fresh fixtures per test, isolated databases per test run (or per test), no mutable shared state.
6. **Arrange-Act-Assert.** A test sets up state (arrange), performs the action under test (act), and checks outcomes (assert). Multiple actions or multiple assertions across unrelated behavior mean multiple tests hiding in one. One behavior per test; descriptive names that read like specifications.
7. **Prefer fakes and stubs to elaborate mocks.** A fake is a working in-memory implementation of a real interface (an in-memory repository, a fake clock). A stub returns canned answers. A mock verifies interactions. Mocks couple tests to implementation; fakes and stubs couple only to the interface. Reach for mocks only when verifying interaction is the behavior being tested.
8. **Coverage is a smell detector, not a target.** 100% line coverage with shallow assertions is worse than 70% coverage with thoughtful ones. Uncovered code is a prompt to ask why -- is the logic trivial, dead, or untested? -- not to add tests mechanically.
9. **Tests are documentation.** A test name and body should teach a reader what the system does in that case. Sacrifice brevity for clarity: duplicate setup across tests if it keeps each test readable top-to-bottom. Future readers will thank the person who wrote five clear tests over the person who shared one clever fixture.
10. **Red, green, refactor.** When adding behavior, write a failing test first (red), make it pass with the minimum change (green), then clean up (refactor). TDD is not mandatory everywhere, but the discipline of writing the test first clarifies intent and prevents tests from being shaped around existing code.

## Anti-patterns
- Tests that mock every collaborator, re-state the production code in reverse, and pass trivially -- they verify the code compiles, nothing more.
- Snapshot tests with enormous blobs that developers approve without reading, drifting into a rubber stamp ritual.
- Shared mutable test fixtures (`BeforeAll` setup of a shared database) where tests order-dependently pollute each other.
- Flaky tests kept "because they usually pass" -- they poison the signal of every red build and train the team to ignore failures.
- Tests that assert internal call counts (`repository.save was called 1 time`) when what matters is the returned value.
- End-to-end tests used to cover edge cases that should be unit tests -- a ten-minute suite for a twenty-millisecond logic error.
- A single "integration test" that walks a happy path through the whole stack and calls itself the test suite.
- No test fails when the feature is deleted -- the suite tests nothing the caller cares about.

## References
- Steve Freeman & Nat Pryce -- *Growing Object-Oriented Software, Guided by Tests*
- Kent Beck -- *Test-Driven Development by Example*
- Gerard Meszaros -- *xUnit Test Patterns*
- Mike Cohn -- *Succeeding with Agile* (test pyramid)
- Martin Fowler -- "Mocks Aren't Stubs" (martinfowler.com/articles/mocksArentStubs.html)
