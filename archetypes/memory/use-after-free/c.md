---
schema_version: 1
archetype: memory/use-after-free
language: c
principles_file: _principles.md
libraries:
  preferred: manual ownership discipline + Valgrind/ASan
  avoid:
    - name: realloc without updating all aliases
      reason: realloc may move the block; old pointer becomes dangling.
    - name: alloca for objects whose pointer escapes the frame
      reason: Stack memory is invalid after the function returns.
minimum_versions:
  c: "C11"
---

# Use-After-Free Defense — C

## Library choice
C has no ownership system — you enforce it with convention and tooling. Document ownership in every function's header comment. Use a `goto cleanup` pattern for resource release so that every exit path frees exactly once. Run Valgrind (`--track-origins=yes`) in development and AddressSanitizer (`-fsanitize=address`) in CI on every commit.

## Reference implementation
```c
#include <stddef.h>
#include <stdlib.h>
#include <string.h>

/* Ownership: caller owns the returned session. Must call session_free(). */
typedef struct {
    char *user;    /* owned */
    char *token;   /* owned */
} session_t;

session_t *session_new(const char *user, size_t user_len,
                       const char *token, size_t token_len)
{
    session_t *s = calloc(1, sizeof(*s));
    if (!s) return NULL;

    s->user = calloc(user_len + 1, 1);
    if (!s->user) goto fail;
    memcpy(s->user, user, user_len);

    s->token = calloc(token_len + 1, 1);
    if (!s->token) goto fail;
    memcpy(s->token, token, token_len);
    return s; /* ownership transferred to caller */

fail:
    session_free(s); /* forward declaration required */
    return NULL;
}

void session_free(session_t *s)
{
    if (!s) return;
    free(s->token);
    s->token = NULL;  /* null after free */
    free(s->user);
    s->user = NULL;
    free(s);
    /* Caller must set their pointer to NULL after this call. */
}
```

## Language-specific gotchas
- Always null the pointer after `free`. A macro can help: `#define SAFE_FREE(p) do { free(p); (p) = NULL; } while(0)`. This turns UAF into a null deref.
- `free(NULL)` is safe (C standard guarantees it is a no-op). You do not need to guard `free` with `if (p)` — but you do need to null afterward.
- `realloc(ptr, new_size)` may return a different address. Always assign the result to a temporary: `tmp = realloc(ptr, size); if (!tmp) { /* ptr is still valid */ } else { ptr = tmp; }`. Never do `ptr = realloc(ptr, size)` — if it fails, you leak the original.
- Functions that return pointers to `static` or stack-local data create hidden dangling references. Make the ownership model explicit: either return heap-allocated memory (caller frees) or fill a caller-provided buffer.
- In `goto cleanup` patterns, initialize all pointer variables to `NULL` at declaration. `free(NULL)` is safe, so the cleanup block works regardless of which allocation step failed.
- Double-free is as dangerous as use-after-free. The null-after-free pattern prevents it because `free(NULL)` is a no-op.

## Tests to write
- `session_new` with valid inputs returns non-NULL with correct `user` and `token` strings.
- `session_new` with a failing allocation (use `malloc` interposition or fault injection) frees all prior allocations and returns `NULL`. Verify with Valgrind: zero leaks.
- `session_free(NULL)` does not crash.
- Double call to `session_free` on the same pointer — the second call receives a dangling pointer. This test must run under ASan to catch it; fix by nulling the caller's pointer.
- Run the full suite under `valgrind --leak-check=full --track-origins=yes` with zero errors and zero leaks.
