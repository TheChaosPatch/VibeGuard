---
schema_version: 1
archetype: memory/buffer-overflow
title: Buffer Overflow Defense
summary: Preventing out-of-bounds writes and reads through bounds checking, safe APIs, and compiler hardening.
applies_to: [c, rust, go]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - buffer
  - overflow
  - bounds
  - stack
  - heap
  - smash
  - oob
  - memcpy
  - strncpy
  - unsafe
related_archetypes:
  - io/input-validation
references:
  owasp_asvs: V5.4
  owasp_cheatsheet: Buffer Overflow Prevention
  cwe: "120"
---

# Buffer Overflow Defense — Principles

## When this applies
Any code that writes to a fixed-size buffer, indexes into an array or slice, performs pointer arithmetic, copies strings, or calls into C-level memory APIs. In managed languages this is handled by the runtime; in C, Rust `unsafe`, and Go's `unsafe` package, it is your responsibility.

## Architectural placement
Bounds enforcement belongs at the **lowest layer** that touches raw memory. Higher-level code should never receive a raw pointer and a separate length — it should receive a bounded type (a slice, a span, a struct with an embedded length field) that makes overflows structurally impossible. When you must drop to raw pointers, isolate that code into the smallest function you can, validate all lengths at its entry, and expose a safe API above it.

## Principles
1. **Check the length before the copy, every time.** No `memcpy`, `memmove`, or manual loop should execute without a preceding comparison of `source_len` against `dest_capacity`. This check is not optional for "internal" buffers.
2. **Use bounded-by-construction APIs.** Prefer `snprintf` over `sprintf`, `strlcpy` over `strcpy`, slice indexing with bounds checks over raw pointer arithmetic. The safest overflow check is one the API performs for you.
3. **Validate arithmetic before allocation.** `count * element_size` can wrap around on overflow, producing a tiny allocation that later overflows. Check for multiplication overflow before calling `malloc`/`calloc`, or use `calloc` which checks internally.
4. **Treat compiler warnings as errors.** `-Wall -Werror` in C, `#[deny(unsafe_op_in_unsafe_fn)]` in Rust, `-race` in Go. Compilers catch a class of overflows statically — let them.
5. **Enable runtime hardening.** Stack canaries (`-fstack-protector-strong`), ASLR, DEP/NX, and AddressSanitizer in CI are defense-in-depth layers. They do not replace bounds checks, but they limit the blast radius of the ones you miss.
6. **Minimize unsafe scope.** In Rust and Go, the surface area of `unsafe` blocks or `unsafe.Pointer` usage should be as small as possible. Wrap every unsafe operation in a safe function that validates its inputs and returns a safe type.
7. **Fuzz every parser.** Any function that reads from a byte buffer — network packets, file formats, serialized data — must be fuzz-tested. Fuzzers find the off-by-one errors that code review misses.

## Anti-patterns
- Using `strcpy`, `strcat`, `sprintf`, or `gets` anywhere in new code.
- Assuming a buffer "will always be big enough" because of an upstream length check in a different function.
- Performing `malloc(n * sizeof(T))` without checking `n * sizeof(T)` for overflow.
- Silently truncating input that exceeds a buffer without reporting an error to the caller.
- Disabling AddressSanitizer in CI because "it makes the tests slow."
- Writing large `unsafe` blocks in Rust instead of isolating each operation.
- Catching a Go slice-bounds panic with `recover` instead of checking `len()` before indexing.
- Relying on NUL-termination of data received from the network.

## References
- OWASP ASVS V5.4 — Memory, String, and Unmanaged Code Requirements
- OWASP Buffer Overflow Prevention Cheat Sheet
- CWE-120 — Buffer Copy without Checking Size of Input
