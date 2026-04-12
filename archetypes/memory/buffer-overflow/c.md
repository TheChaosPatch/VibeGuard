---
schema_version: 1
archetype: memory/buffer-overflow
language: c
principles_file: _principles.md
libraries:
  preferred: POSIX safe functions (snprintf, strlcpy, calloc)
  avoid:
    - name: gets
      reason: No bounds checking, removed in C11. Never use.
    - name: strcpy
      reason: No bounds checking; use strlcpy or snprintf.
    - name: strcat
      reason: No bounds checking; use strlcat or snprintf.
    - name: sprintf
      reason: No bounds checking; use snprintf exclusively.
    - name: scanf with unbounded %s
      reason: Reads until whitespace with no length limit.
minimum_versions:
  c: "C11"
---

# Buffer Overflow Defense — C

## Library choice
C has no "safe string library" in the standard — you must enforce bounds yourself. Use `snprintf` for all formatted output, `strlcpy`/`strlcat` where available (BSD/macOS; on Linux, link `libbsd` or write a trivial wrapper), and `calloc` instead of `malloc` for allocations that involve a count and a size (it checks for multiplication overflow internally). Compile with `-Wall -Werror -Wformat-security -fstack-protector-strong -D_FORTIFY_SOURCE=2` and run CI under `-fsanitize=address,undefined`.

## Reference implementation
```c
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#define MSG_MAX 256

typedef enum {
    BUF_OK = 0,
    BUF_ERR_NULL,
    BUF_ERR_OVERFLOW,
    BUF_ERR_ALLOC,
} buf_result_t;

/* Safe copy: bounds-checked, always NUL-terminated. */
buf_result_t safe_copy(char *dst, size_t dst_cap, const char *src, size_t src_len)
{
    if (!dst || !src) return BUF_ERR_NULL;
    if (src_len >= dst_cap) return BUF_ERR_OVERFLOW;

    memcpy(dst, src, src_len);
    dst[src_len] = '\0';
    return BUF_OK;
}

/* Overflow-checked allocation for an array of items. */
buf_result_t safe_alloc_array(size_t count, size_t elem_size, void **out)
{
    if (!out) return BUF_ERR_NULL;
    if (count == 0 || elem_size == 0) { *out = NULL; return BUF_OK; }

    /* calloc checks count*elem_size for overflow internally. */
    *out = calloc(count, elem_size);
    if (!*out) return BUF_ERR_ALLOC;
    return BUF_OK;
}

/* Format a log message into a fixed-size buffer, safely. */
buf_result_t format_log_entry(char *buf, size_t buf_cap,
                              const char *user, const char *action)
{
    if (!buf || !user || !action) return BUF_ERR_NULL;
    int written = snprintf(buf, buf_cap, "[%s] %s", user, action);
    if (written < 0 || (size_t)written >= buf_cap) return BUF_ERR_OVERFLOW;
    return BUF_OK;
}
```

## Language-specific gotchas
- `snprintf` returns the number of characters that *would have been written*, not the number actually written. Always compare `(size_t)written >= buf_cap` to detect truncation.
- `strncpy` does NOT NUL-terminate if `src_len >= n`. If you must use it, always set `dst[n - 1] = '\0'` afterward. Prefer `strlcpy` or the `safe_copy` pattern above.
- `calloc(count, size)` is safer than `malloc(count * size)` because the multiplication overflow check is built in. Use it for all array allocations.
- `-D_FORTIFY_SOURCE=2` turns many `memcpy`/`strcpy` calls into checked variants at compile time. It is free defense-in-depth.
- Stack-allocated buffers that leave the function as a pointer (returned or stored in a struct) are undefined behavior and a common source of overflows in the caller.
- Never use `alloca` or variable-length arrays (VLAs) with untrusted sizes. They cannot be bounds-checked against remaining stack space.

## Tests to write
- `safe_copy` with `src_len == dst_cap - 1` (exact fit) succeeds; `src_len == dst_cap` returns `BUF_ERR_OVERFLOW`.
- `format_log_entry` with a user+action that exceed `MSG_MAX` returns overflow, not truncated data.
- `safe_alloc_array` with `SIZE_MAX / 2` count returns `BUF_ERR_ALLOC`, not a tiny allocation.
- Run the entire test suite under AddressSanitizer (`-fsanitize=address`) — any OOB access is a test failure.
- Fuzz `safe_copy` with random lengths and verify no ASan findings.
