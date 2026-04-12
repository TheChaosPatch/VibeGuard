---
schema_version: 1
archetype: memory/use-after-free
language: rust
principles_file: _principles.md
libraries:
  preferred: safe Rust ownership and borrowing (Box, Arc, Rc)
  avoid:
    - name: "std::ptr::drop_in_place followed by read"
      reason: Equivalent to use-after-free; the borrow checker cannot see it in unsafe.
    - name: "ManuallyDrop::take on shared data"
      reason: Creates a second owner; first owner's drop becomes use-after-free.
    - name: raw pointer dereference after Box::into_raw without tracking lifetime
      reason: Raw pointers opt out of the borrow checker entirely.
minimum_versions:
  rust: "1.75"
---

# Use-After-Free Defense — Rust

## Library choice
Safe Rust makes use-after-free impossible at compile time through the ownership and borrowing system. The guidance here targets `unsafe` code: FFI wrappers, custom allocators, self-referential structs, and any code that calls `Box::into_raw`, `ManuallyDrop`, `std::ptr::read`, or `std::ptr::drop_in_place`. The rule is the same as C — single owner, explicit transfer — but the borrow checker cannot enforce it across raw pointers. You must enforce it yourself and prove correctness with Miri.

## Reference implementation
```rust
/// An FFI-friendly session handle that owns its heap data.
/// Ownership rule: created by `session_new`, freed by `session_free`.
pub struct SessionHandle {
    user: String,
    token: String,
}

/// Create a session and return an owning raw pointer for FFI.
/// Caller must eventually pass the pointer to `session_free`.
#[no_mangle]
pub extern "C" fn session_new(
    user: *const u8, user_len: usize,
    token: *const u8, token_len: usize,
) -> *mut SessionHandle {
    let user = match str_from_raw(user, user_len) {
        Some(s) => s,
        None => return std::ptr::null_mut(),
    };
    let token = match str_from_raw(token, token_len) {
        Some(s) => s,
        None => return std::ptr::null_mut(),
    };
    Box::into_raw(Box::new(SessionHandle { user, token }))
}

/// Free a session. Pointer must not be used after this call.
/// # Safety: `ptr` must have come from `session_new` and not been freed.
#[no_mangle]
pub unsafe extern "C" fn session_free(ptr: *mut SessionHandle) {
    if ptr.is_null() { return; }
    drop(unsafe { Box::from_raw(ptr) });
}

fn str_from_raw(ptr: *const u8, len: usize) -> Option<String> {
    if ptr.is_null() || len == 0 { return None; }
    let slice = unsafe { std::slice::from_raw_parts(ptr, len) };
    String::from_utf8(slice.to_vec()).ok()
}
```

## Language-specific gotchas
- `Box::into_raw` transfers ownership out of Rust's type system. From that point you are in C territory: single owner, null after free, no aliasing. `Box::from_raw` takes it back — call it exactly once.
- `ManuallyDrop` prevents `Drop` from running. If you call `ManuallyDrop::take` on shared or aliased data, you create a second owner and the first owner's eventual drop is a double-free.
- Self-referential structs (a struct holding a reference to its own field) cannot be expressed safely. Use `Pin` to prevent moves, or redesign to use indices instead of references.
- `Arc`/`Rc` cycles (A holds `Arc<B>` and B holds `Arc<A>`) prevent freeing and are a memory leak, not a UAF. Break cycles with `Weak`.
- `std::mem::transmute` and `std::ptr::read` can create bitwise copies of non-`Copy` types, leading to double-drop. Only use them on `Copy` types or `ManuallyDrop` wrappers.
- Run `cargo +nightly miri test` in CI. Miri detects use-after-free, double-free, and dangling references in `unsafe` code with zero false positives.

## Tests to write
- Round-trip: `session_new` returns a non-null pointer, then `session_free` completes without error.
- Null inputs to `session_new` return null pointer, not a panic.
- `session_free(std::ptr::null_mut())` is a no-op.
- Run all tests under Miri (`cargo +nightly miri test`). Any use-after-free in unsafe code is a test failure with a precise diagnostic.
- Property test: create and free N sessions in random order. Miri must report zero undefined behavior.
