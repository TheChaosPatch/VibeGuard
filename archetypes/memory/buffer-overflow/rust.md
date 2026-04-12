---
schema_version: 1
archetype: memory/buffer-overflow
language: rust
principles_file: _principles.md
libraries:
  preferred: safe Rust (slices, Vec, bounds-checked indexing)
  avoid:
    - name: std::ptr::copy_nonoverlapping in unsafe without bounds check
      reason: Equivalent to memcpy with no safety net.
    - name: slice::get_unchecked
      reason: Skips bounds check. Use slice::get or normal indexing.
    - name: from_raw_parts with unchecked length
      reason: Trusting an external length creates the same bug class as C.
minimum_versions:
  rust: "1.75"
---

# Buffer Overflow Defense — Rust

## Library choice
Safe Rust eliminates buffer overflows by construction: array and slice indexing panics on out-of-bounds, `Vec` tracks its own capacity, and there is no pointer arithmetic without `unsafe`. The guidance here targets `unsafe` blocks — FFI boundaries, custom allocators, SIMD intrinsics, and performance-critical inner loops where you drop to raw pointers. The rule is: validate every length *before* entering `unsafe`, and expose only safe types from the wrapper.

## Reference implementation
```rust
#[derive(Debug, thiserror::Error)]
pub enum BufError {
    #[error("buffer overflow: need {needed}, have {capacity}")]
    Overflow { needed: usize, capacity: usize },
}

/// Copy `src` into `dst`, returning an error if it does not fit.
pub fn safe_copy(dst: &mut [u8], src: &[u8]) -> Result<usize, BufError> {
    if src.len() > dst.len() {
        return Err(BufError::Overflow {
            needed: src.len(),
            capacity: dst.len(),
        });
    }
    dst[..src.len()].copy_from_slice(src);
    Ok(src.len())
}

/// Read a length-prefixed message from a raw FFI buffer.
/// # Safety contract is enforced here, not left to the caller.
pub fn read_prefixed_message(raw: &[u8]) -> Result<&[u8], BufError> {
    let len_bytes = raw.get(..4).ok_or(BufError::Overflow {
        needed: 4,
        capacity: raw.len(),
    })?;
    let msg_len = u32::from_le_bytes(len_bytes.try_into().unwrap()) as usize;
    raw.get(4..4 + msg_len).ok_or(BufError::Overflow {
        needed: 4 + msg_len,
        capacity: raw.len(),
    })
}
```

## Language-specific gotchas
- `slice[index]` panics on OOB. This is safe (no UB) but crashes the process. Use `slice.get(index)` to return `Option` when OOB is an expected condition, not an invariant violation.
- Inside `unsafe` blocks, the compiler does NOT check bounds. You must do it yourself *before* the `unsafe` keyword. A common mistake is validating inside the unsafe block but after the pointer dereference.
- `std::slice::from_raw_parts(ptr, len)` trusts `len` unconditionally. If `len` comes from FFI or network data, validate it against the known allocation size first.
- `#[deny(unsafe_op_in_unsafe_fn)]` forces each unsafe operation inside an `unsafe fn` to have its own `unsafe` block, making the scope explicit. Enable it crate-wide.
- Use `cargo fuzz` (libFuzzer) or `cargo-afl` for any function that parses byte buffers. Rust's safety guarantees vanish inside `unsafe`, and fuzzers are the best tool to prove your manual checks are correct.
- `checked_mul`, `checked_add` on `usize` prevent silent integer overflow in size calculations — use them before allocating.

## Tests to write
- `safe_copy` with `src.len() == dst.len()` succeeds; `src.len() == dst.len() + 1` returns `Err(Overflow)`.
- `read_prefixed_message` with a length prefix larger than the remaining buffer returns `Err`, not a panic or garbage.
- Property test: for any `&[u8]` input, `read_prefixed_message` either returns `Ok` pointing within the input or returns `Err`. It never panics.
- Run `cargo test` under Miri (`cargo +nightly miri test`) to detect undefined behavior in any unsafe code.
