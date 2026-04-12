---
schema_version: 1
archetype: memory/use-after-free
title: Use-After-Free Defense
summary: Preventing access to freed memory through ownership clarity, lifetime management, and pointer discipline.
applies_to: [c, rust]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - use-after-free
  - dangling
  - pointer
  - lifetime
  - ownership
  - free
  - malloc
  - heap
  - double-free
  - uaf
related_archetypes:
  - memory/buffer-overflow
  - io/input-validation
references:
  owasp_asvs: V5.4
  owasp_cheatsheet: Memory Management
  cwe: "416"
---

# Use-After-Free Defense — Principles

## When this applies
Any code that manually allocates and frees memory, stores pointers to heap objects whose lifetime is controlled by another component, returns pointers from functions, or uses raw pointers in a language with a garbage collector or borrow checker that you have explicitly opted out of. In garbage-collected languages (Go, Python, C#, Java) this class of bug does not occur in normal code.

## Architectural placement
Ownership policy is decided at **design time**, not discovered at debug time. Every heap allocation has exactly one owner — the component responsible for freeing it. Ownership transfers are explicit (passing a pointer with a documented "caller takes ownership" contract). Borrows are explicit (pointer valid only for the duration of a call, not stored). This discipline belongs in the module's API contract and is enforced by code review, static analysis, and runtime sanitizers.

## Principles
1. **Single owner, explicit transfer.** Every allocation has one owner at any point in time. When ownership moves (returned to a caller, stored in a struct), document it and null the source pointer. Shared ownership requires a reference count.
2. **Null after free.** Immediately set a freed pointer to `NULL`. This turns use-after-free into a null dereference — still a bug, but deterministic, debuggable, and not exploitable for code execution.
3. **Never return pointers to stack-local data.** A pointer to a local variable is dangling the moment the function returns. Return by value, or allocate on the heap and transfer ownership.
4. **Do not alias owned pointers.** If two pointers refer to the same allocation and one frees it, the other is dangling. Minimize aliases; where they are necessary, use reference counting or a clear borrowing protocol.
5. **Use RAII or defer for cleanup.** In languages that support it, tie the lifetime of a resource to a scope so that freeing cannot be forgotten or done twice. In C, centralize cleanup at the end of the function with `goto cleanup` or a single-return pattern.
6. **Detect in CI with sanitizers.** AddressSanitizer (ASan) and Valgrind catch use-after-free at runtime. Run them in CI on every commit. They find the bugs that code review misses.
7. **Document ownership in APIs.** Every function that returns a pointer or accepts a pointer parameter must document whether it transfers, borrows, or shares ownership. Undocumented ownership is a UAF waiting to happen.

## Anti-patterns
- Freeing a pointer and continuing to read from or write to it.
- Returning `&local_var` from a function.
- Storing a borrowed pointer in a long-lived struct without ensuring the source outlives it.
- Calling `free` in an error path but not in the success path, or vice versa.
- Double-freeing because two code paths both think they own the pointer.
- Using `realloc` and continuing to use the old pointer (it may have moved).
- Freeing inside a loop iteration and accessing the freed object in the next iteration.
- Suppressing ASan/Valgrind findings instead of fixing the root cause.

## References
- OWASP ASVS V5.4 — Memory, String, and Unmanaged Code Requirements
- CWE-416 — Use After Free
- CWE-415 — Double Free
