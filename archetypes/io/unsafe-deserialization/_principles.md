---
schema_version: 1
archetype: io/unsafe-deserialization
title: Unsafe Deserialization Defense
summary: Parsing untrusted payloads into typed data without granting the sender RCE.
applies_to: [csharp, python]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - deserialize
  - deserialization
  - pickle
  - binaryformatter
  - yaml
  - json
  - polymorphic
  - typenamehandling
  - gadget
related_archetypes:
  - io/input-validation
references:
  owasp_asvs: V5.5
  owasp_cheatsheet: Deserialization Cheat Sheet
  cwe: "502"
---

# Unsafe Deserialization Defense — Principles

## When this applies
Any time your code reads bytes that originated outside your process and converts them into an in-process object graph: HTTP request bodies, message-queue payloads, cache entries, files uploaded by users, rows read back from a database that a user could influence, cookies you round-trip for the client. "I trust this because it came from our own Redis" is wrong — the question is whether an attacker can ever write to that Redis, and usually the answer is yes.

## Architectural placement
Every inbound payload is parsed by a named `IPayloadReader<T>` / `parse_<message>` function that takes `bytes` and returns a *specific* typed record. The type is closed: every field is listed, every type is concrete, and the parser rejects anything it doesn't recognize. Handlers call `parser.Parse<CreateOrderRequest>(body)`, not `JsonSerializer.Deserialize<object>(body)`. Polymorphism, if you truly need it, is expressed with a closed discriminated union and an allowlist of known type tags — never by letting the payload pick the runtime type. This structural choice turns "load whatever the attacker wants" into "load exactly the shape we documented," which is the entire defense.

## Principles
1. **Safe format by default — JSON with a typed target.** `System.Text.Json` into a record, `pydantic` into a `BaseModel`. Both are constructive: they build a known type from known fields and reject the rest. `pickle`, `yaml.load` (unsafe loader), `BinaryFormatter`, `NetDataContractSerializer`, and Java's `ObjectInputStream` are not parsers — they are arbitrary-code-execution engines wearing a serialization costume.
2. **Never let the payload choose its runtime type.** `TypeNameHandling.All`, `JsonTypeInfoResolver` configured to walk all assemblies, `pickle.Unpickler.find_class` with no override — all of these let the attacker instantiate types you didn't intend. The canonical gadget-chain attacks exploit exactly this hook.
3. **Validate *after* deserialization, before use.** A parseable message is not a valid message. Check required fields, ranges, cross-field invariants, enum membership. Pydantic does this automatically in `__init__`; `System.Text.Json` doesn't, so add an explicit validation step.
4. **Cap size before parsing.** The parser allocates memory for whatever fits in the payload. A 2 GB JSON blob of nested arrays is a DoS even without code execution. Reject over-size inputs at the transport layer.
5. **Cap depth and collection sizes.** Nested objects and dictionary-of-dictionary payloads are a second DoS surface. Set `JsonSerializerOptions.MaxDepth` and bound collection sizes in your model. Python's `json` has no depth limit by default — bound it yourself.
6. **One endpoint, one schema.** Don't share a generic "payload" type across endpoints and branch on a `kind` field inside your handler. Route shape to code at the transport boundary. Different endpoints, different typed parsers.
7. **Fail closed on parse or validation error.** Log the *shape* of the failure (which field, which rule), never the raw bytes. Do not try a second parser on failure — "maybe it's YAML" is how unsafe YAML parsers sneak back in.

## Anti-patterns
- `pickle.loads(request.body)` from any network-reachable source. There is no safe way to make this safe.
- `yaml.load(data)` without `Loader=yaml.SafeLoader` — default loaders instantiate arbitrary Python objects.
- `BinaryFormatter.Deserialize` in .NET. Microsoft marks it obsolete and disabled by default in .NET 9+ for this exact reason.
- `JsonConvert.DeserializeObject` with `TypeNameHandling.All` or `TypeNameHandling.Auto` — this *is* the remote-code-execution primitive in dozens of disclosed CVEs.
- `System.Text.Json` with a target of `object` or `JsonElement` passed deep into business logic — deserialization succeeds, but validation becomes impossible.
- Catching a parse exception and retrying with a "more lenient" parser.
- Accepting payloads with no `Content-Length` cap.
- Deserializing before authenticating the caller — see `auth/api-endpoint-authentication` for why the order matters.

## References
- OWASP ASVS V5.5 — Deserialization Prevention
- OWASP Deserialization Cheat Sheet
- CWE-502 — Deserialization of Untrusted Data
