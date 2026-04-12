---
schema_version: 1
archetype: io/regex-dos
language: typescript
principles_file: _principles.md
libraries:
  preferred: safe-regex + built-in RegExp (Node.js / browser, typed)
  acceptable:
    - re2 (Node.js native binding to Google RE2)
  avoid:
    - name: new RegExp(userInput)
      reason: Attacker-controlled pattern; direct ReDoS and injection vector regardless of TypeScript type safety.
    - name: Unaudited regex literals applied to unbounded string inputs
      reason: TypeScript's type system does not constrain string length; the V8 backtracking engine spins on adversarial inputs.
minimum_versions:
  node: "22"
  typescript: "5.5"
---

# ReDoS Defense — TypeScript

## Library choice
TypeScript compiles to JavaScript and runs on V8, so all the JavaScript ReDoS risks apply identically. TypeScript adds branded types and `readonly` patterns that make it easier to express "this string has already been validated" as a type-level guarantee, preventing raw `string` values from reaching business logic without passing through the validator. Use `safe-regex` for static pattern auditing, `re2` for runtime linear-time matching when native modules are available, and branded/opaque types to make validated values distinct from raw strings.

## Reference implementation
```typescript
import { strict as assert } from "node:assert";

const MAX_EMAIL_LEN = 254;
const MAX_SLUG_LEN  = 128;

// Branded types -- callers receive a ValidEmail, not a string.
// This makes it a type error to pass an unvalidated string where a ValidEmail is required.
declare const _brand: unique symbol;
type ValidEmail = string & { readonly [_brand]: "ValidEmail" };
type ValidSlug  = string & { readonly [_brand]: "ValidSlug" };

const SLUG_RE  = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const EMAIL_RE = /^[a-zA-Z0-9._%+\-]{1,64}@[a-zA-Z0-9.\-]{1,253}\.[a-zA-Z]{2,63}$/;

export function validateSlug(value: string): ValidSlug {
  if (value.length > MAX_SLUG_LEN) throw new RangeError("Slug too long");
  if (!SLUG_RE.test(value)) throw new TypeError(`Invalid slug: ${JSON.stringify(value)}`);
  return value as ValidSlug;
}

export function validateEmail(value: string): ValidEmail {
  if (value.length > MAX_EMAIL_LEN) throw new RangeError("Email too long");
  if (!EMAIL_RE.test(value)) throw new TypeError(`Invalid email: ${JSON.stringify(value)}`);
  return value as ValidEmail;
}

// RE2 path -- imported dynamically so the module loads even without native bindings.
let re2EmailRE: { test(s: string): boolean } = EMAIL_RE;
try {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const RE2 = require("re2") as typeof import("re2");
  re2EmailRE = new RE2(String.raw`^[a-zA-Z0-9._%+\-]{1,64}@[a-zA-Z0-9.\-]{1,253}\.[a-zA-Z]{2,63}$`);
} catch { /* native module unavailable; fall back to audited RegExp */ }

export function validateEmailSafe(value: string): ValidEmail {
  if (value.length > MAX_EMAIL_LEN) throw new RangeError("Email too long");
  if (!re2EmailRE.test(value)) throw new TypeError(`Invalid email: ${JSON.stringify(value)}`);
  return value as ValidEmail;
}
```

## Language-specific gotchas
- Branded types (`ValidEmail`, `ValidSlug`) propagate the validation guarantee through the type system. A function that accepts `ValidEmail` cannot receive a `string` without an explicit cast, which makes forgotten validation a compile-time error, not a runtime surprise.
- TypeScript's `string` type has no length constraint. Length checks must happen at runtime; the type system cannot express "string with max 254 chars" natively. Use a Zod schema (`z.string().max(254).email()`) to combine runtime validation with TypeScript type inference if your project already uses Zod.
- `@typescript-eslint/no-unsafe-regex` (or `eslint-plugin-regexp`) can detect catastrophically backtracking patterns at lint time. Add this rule to your ESLint config as a CI gate.
- The `as ValidSlug` cast is sound only because the function validates first. TypeScript type assertions bypass the type checker — the validation logic preceding the cast is load-bearing.
- In strict mode TypeScript with `noImplicitAny`, dynamic `require("re2")` requires `@types/re2` or an `// @ts-ignore` comment. Install `@types/re2` from npm.
- When Zod is used for request body parsing, Zod's `.regex()` validator accepts a `RegExp` literal — not a user-supplied string. Zod does not guard against catastrophic backtracking in the pattern you provide; audit the pattern at definition time.

## Tests to write
- Slug happy path: `validateSlug("hello-world")` returns the input typed as `ValidSlug`.
- Slug too long: throws `RangeError` before regex runs.
- Email happy path: `validateEmail("user@example.com")` returns `ValidEmail`.
- Email too long: throws `RangeError`.
- Adversarial email: `"a".repeat(200) + "@"` throws `TypeError` (non-match) in under 50 ms.
- Type assertion: a function accepting `ValidEmail` refuses a raw `string` argument at the TypeScript compiler level (checked by `tsc --noEmit`).
- RE2 fallback: when `re2` is not installed, `validateEmailSafe` still works via the stdlib `RegExp` fallback.
