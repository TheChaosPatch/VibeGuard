---
schema_version: 1
archetype: io/input-validation
language: c
principles_file: _principles.md
libraries:
  preferred: hand-rolled parsers with explicit bounds
  acceptable:
    - cJSON
  avoid:
    - name: scanf family
      reason: Easy to misuse; no bounds on %s without explicit width.
    - name: gets
      reason: Removed from C11. Never use.
minimum_versions:
  c: "C11"
---

# Input Validation — C

## Library choice
C has no "validation framework" and you should not pretend it does. Write small, bounded parsers per input shape, check every length, and reject early. For JSON inputs, `cJSON` is acceptable but still forces you to own every bounds check manually.

## Reference implementation
```c
#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <stdbool.h>

#define MAX_USERNAME 64
#define MAX_EMAIL    254

typedef struct {
    char username[MAX_USERNAME + 1];
    char email[MAX_EMAIL + 1];
    uint8_t age;
} user_registration_t;

typedef enum {
    VALIDATION_OK = 0,
    VALIDATION_BAD_USERNAME,
    VALIDATION_BAD_EMAIL,
    VALIDATION_BAD_AGE,
    VALIDATION_OVERSIZE,
} validation_result_t;

validation_result_t parse_user_registration(
    const char *username, size_t username_len,
    const char *email, size_t email_len,
    int age_in,
    user_registration_t *out)
{
    if (username_len == 0 || username_len > MAX_USERNAME) return VALIDATION_OVERSIZE;
    if (email_len == 0 || email_len > MAX_EMAIL) return VALIDATION_OVERSIZE;
    if (age_in < 13 || age_in > 120) return VALIDATION_BAD_AGE;

    if (memchr(email, '@', email_len) == NULL) return VALIDATION_BAD_EMAIL;

    memcpy(out->username, username, username_len);
    out->username[username_len] = '\0';
    memcpy(out->email, email, email_len);
    out->email[email_len] = '\0';
    out->age = (uint8_t)age_in;
    return VALIDATION_OK;
}
```

## Language-specific gotchas
- Always pass explicit lengths. Do not rely on `strlen` on buffers coming from the network — if the sender didn't null-terminate, `strlen` runs into the weeds.
- `memcpy` instead of `strcpy`, with the length already bounds-checked.
- `out->username[username_len] = '\0'` only after the bounds check — off-by-one here is a classic overflow.
- Enum return codes, not bare `int`s. Callers can `switch` on them without guessing.
- Never use `sprintf` to build anything from user input. `snprintf` with a bounded buffer is mandatory.

## Tests to write
- Each invalid branch returns its specific enum value.
- Exact-length boundary cases (`MAX_USERNAME`, `MAX_USERNAME + 1`) behave correctly.
- Fuzz with random bytes — the parser must never read past `*_len`.
- Non-ASCII usernames are handled deliberately: either allowed (document the policy) or rejected (validate with a whitelist).
