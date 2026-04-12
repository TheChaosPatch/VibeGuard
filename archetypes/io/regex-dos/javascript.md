---
schema_version: 1
archetype: io/regex-dos
language: javascript
principles_file: _principles.md
libraries:
  preferred: safe-regex + built-in RegExp (Node.js / browser)
  acceptable:
    - re2 (Node.js native binding to Google RE2)
  avoid:
    - name: new RegExp(userInput)
      reason: Attacker controls the pattern; direct ReDoS and injection vector.
    - name: Unaudited regex literals on unbounded user input
      reason: V8's regex engine is a backtracking NFA; catastrophic patterns block the event loop, taking down the entire Node.js process.
minimum_versions:
  node: "22"
---

# ReDoS Defense — JavaScript

## Library choice
V8 (Node.js and browsers) uses a backtracking NFA regex engine. The event loop architecture makes ReDoS especially dangerous: a single spinning regex blocks all I/O, all other requests, and all timers in the Node.js process. Use `safe-regex` (npm) or `vuln-regex-detector` to statically audit patterns for catastrophic backtracking risk before deploying them. For truly untrusted, runtime-evaluated patterns, use the `re2` npm package (native binding to Google RE2) which guarantees linear time. All patterns must have a documented maximum input length; reject inputs over that length before regex evaluation.

## Reference implementation
```javascript
// @ts-check
"use strict";

const MAX_EMAIL_LEN = 254;
const MAX_SLUG_LEN  = 128;

// Audited: no nested quantifiers, no overlapping alternations.
// Complexity: O(n) on V8's NFA for all benign and adversarial inputs within the stated length cap.
const SLUG_RE  = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const EMAIL_RE = /^[a-zA-Z0-9._%+\-]{1,64}@[a-zA-Z0-9.\-]{1,253}\.[a-zA-Z]{2,63}$/;

/**
 * @param {string} value
 * @returns {boolean}
 */
function isValidSlug(value) {
  if (typeof value !== "string" || value.length > MAX_SLUG_LEN) return false;
  return SLUG_RE.test(value);
}

/**
 * @param {string} value
 * @returns {boolean}
 */
function isValidEmail(value) {
  if (typeof value !== "string" || value.length > MAX_EMAIL_LEN) return false;
  return EMAIL_RE.test(value);
}

// RE2 alternative for runtime patterns or high-risk validators.
// npm install re2
let RE2;
try {
  RE2 = require("re2");
} catch {
  RE2 = null;
}

const safeEmailRE = RE2
  ? new RE2(String.raw`^[a-zA-Z0-9._%+\-]{1,64}@[a-zA-Z0-9.\-]{1,253}\.[a-zA-Z]{2,63}$`)
  : EMAIL_RE;

function isValidEmailSafe(value) {
  if (typeof value !== "string" || value.length > MAX_EMAIL_LEN) return false;
  return safeEmailRE.test(value);
}

module.exports = { isValidSlug, isValidEmail, isValidEmailSafe };
```

## Language-specific gotchas
- The Node.js event loop is single-threaded. A regex that backtracks for 5 seconds blocks the entire server — all concurrent requests, all timers, all I/O callbacks. This is not "slow for one user" — it is a full process denial of service.
- V8 (Node.js 16+) introduced an experimental linear-time regex engine for a subset of patterns. As of Node 22 it is not enabled by default for all patterns. Do not rely on it as a defense.
- `new RegExp(userInput)` evaluates to a pattern where the attacker can supply `(a+)+`, `(.*)*`, or similar. If you must accept user search terms, escape them with a function like `value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")` to treat the entire input as a literal, not a pattern.
- `safe-regex` (npm) is a static analysis tool that returns `true` if the pattern is "safe" (no super-linear worst case on its heuristic). It can have false negatives. Use it as a CI lint check, not a runtime guard.
- `re2` (npm) is a native Node.js module wrapping C++ RE2. It requires native compilation (`node-gyp`). In environments where native modules are unavailable (some serverless runtimes), fall back to a carefully audited `RegExp` with a strict length cap.
- Browser environments have no `re2`. For client-side validation, audit the pattern with `safe-regex` at build time and enforce the length cap. Validation on the client is UX; authoritative validation is always server-side.

## Tests to write
- Slug happy path: `"hello-world"` returns `true`.
- Slug too long: a string of `MAX_SLUG_LEN + 1` characters returns `false`.
- Slug non-string: `isValidSlug(123)` returns `false`.
- Email happy path: `"user@example.com"` returns `true`.
- Email too long: a 255-character string returns `false`.
- Adversarial slug: `"a".repeat(200) + "!"` completes in under 50 ms.
- RE2 path: when `re2` is available, `isValidEmailSafe` uses the RE2 instance (check constructor name).
- Pattern literal: verify `SLUG_RE` and `EMAIL_RE` are defined as module-level literals and never reassigned.
