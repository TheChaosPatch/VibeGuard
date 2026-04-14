---
schema_version: 1
archetype: engineering/commit-hygiene
title: Commit Hygiene
summary: Small atomic commits with intent-revealing messages; one logical change per commit; main history is a reading experience.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - commit
  - atomic-commit
  - commit-message
  - version-control
  - git
  - conventional-commits
  - changelog
  - git-history
  - pull-request
  - code-review
  - bisect
  - revert
related_archetypes:
  - engineering/refactoring-discipline
  - engineering/continuous-integration
  - engineering/documentation-discipline
  - engineering/api-evolution
references:
  article: "Conventional Commits (conventionalcommits.org)"
  article: "Tim Pope — A Note About Git Commit Messages"
  book: "Clean Code — Robert C. Martin (chapter on naming)"
---

# Commit Hygiene -- Principles

## When this applies
Every time you commit. Every time you review a PR. Every time you use `git bisect` to hunt a regression and either thank or curse the person who structured the history. Commit hygiene is not cosmetic -- it is the interface between past-you and future-you (or future-them). A messy history makes bisect useless, code review painful, revert dangerous, and changelog generation manual. A clean history makes each of those cheap.

## Architectural placement
Commits are the unit of history -- finer than releases, coarser than edits. They are the granularity at which changes are reviewed, shipped, reverted, bisected, and cited. Commit hygiene sits alongside refactor discipline (refactors and features go in separate commits) and CI (a failing commit is precisely locatable when commits are small). The discipline protects the team's ability to work with git instead of fighting it.

## Principles
1. **One logical change per commit.** A commit represents one intentional step: "add rate limiter interface", "implement rate limiter", "wire rate limiter into login handler". Not "rate limiter + fix typo in unrelated file + update README + bump version". The test: can you write a one-sentence commit message that accurately describes the whole diff? If not, split.
2. **Atomic and reversible.** A commit either lands completely or not at all -- tests pass, build is green, nothing half-done. `git revert <sha>` should remove a self-contained unit of change, not leave dangling references or broken tests. If reverting requires multiple commits, they should have been one commit.
3. **Write for a future reader.** The commit message is read weeks or years later by someone who does not know what was obvious to you today. Explain the *why*, not the *what* -- the diff already shows what. "Fix bug" is useless; "fix off-by-one in pagination cursor when page_size equals total_count" is a document.
4. **Subject, blank line, body.** A short imperative subject line (≤72 characters), a blank line, then a body that explains the context, the constraints, and the reasoning if non-obvious. The subject line is the headline; the body is the article. Tools truncate subjects; they preserve bodies.
5. **Imperative mood.** "Add rate limiter", not "Added rate limiter" or "Adding rate limiter". The convention matches how git itself phrases things ("Merge pull request...", "Revert commit...") and reads consistently across tooling.
6. **Keep history clean before pushing, don't rewrite after.** Locally, rebase, squash, and reword freely until the history is a sequence of useful commits. Once pushed to a shared branch, treat history as immutable. Force-pushing over someone else's base breaks their local state.
7. **Never mix refactor with feature.** Renames and moves in one commit, behavior changes in another. Reviewing a diff that mixes them is nearly impossible -- every real change is camouflaged by renames. See refactoring-discipline for the full rule.
8. **Conventional prefixes for machine readability.** `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:` (or a similar team-adopted taxonomy). They enable automated changelogs, semantic version bumps, and grep-based history review. Pick a scheme and hold it.
9. **PRs are review units; commits are history units.** A PR may contain many well-scoped commits or be squashed into one, depending on team convention. Either way, each unit that lands on main should read as a deliberate, self-contained change. Squashing into one commit loses the step-by-step narrative; merging without squash preserves it but requires the commits to have been clean.
10. **Delete trivial commits.** `wip`, `fix typo`, `address review feedback`, `oops` do not belong on main. Fold them into the commit that introduced the issue, or the commit they were meant to be. Disposable commits pollute history and make bisect pick bad baselines.

## Anti-patterns
- Commit messages like `fix`, `update`, `wip`, `more changes`, `asdf` -- each is a confession that the author could not describe what they did.
- A single commit titled "Implement new feature" containing 40 files, three refactors, two unrelated bug fixes, and an upgrade to a dependency.
- `Co-authored-by` tags applied automatically by tools, populating every solo commit with ceremonial noise.
- Force-pushing to shared branches after others have based work on them, destroying their local state.
- PRs with a commit history of `initial`, `fix`, `fix again`, `address review`, `fix test`, `fix lint` -- each was meaningful in the moment and meaningless in main.
- Commit messages that cite ticket numbers without explaining the change -- future readers must pivot to an external system to understand anything.
- Squashing a multi-step refactor into one mega-commit, losing the ability to bisect to the specific step that broke something.
- "Fix bug" as a commit message repeated 40 times across a project's history -- git blame tells you who, not what.
- Binary blobs, generated files, or build outputs committed to the repo, because "it was easier than fixing the .gitignore."

## References
- Conventional Commits -- https://www.conventionalcommits.org
- Tim Pope -- "A Note About Git Commit Messages" (tbaggery.com/2008/04/19/a-note-about-git-commit-messages.html)
- Chris Beams -- "How to Write a Git Commit Message" (cbea.ms/git-commit)
- Linus Torvalds -- Linux kernel SubmittingPatches documentation
- Ben Balter -- "The essentials of writing a git commit message"
