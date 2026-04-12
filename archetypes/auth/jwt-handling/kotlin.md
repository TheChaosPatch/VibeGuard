---
schema_version: 1
archetype: auth/jwt-handling
language: kotlin
principles_file: _principles.md
libraries:
  preferred: com.nimbusds:nimbus-jose-jwt
  acceptable:
    - io.jsonwebtoken:jjwt-api
  avoid:
    - name: Manual base64 decode of JWT payload
      reason: Reads claims without signature verification — not authentication.
minimum_versions:
  kotlin: "2.0"
  java: "21"
---

# JWT Handling — Kotlin

## Library choice
Kotlin on the JVM uses the same JOSE ecosystem as Java. `nimbus-jose-jwt` is the first choice — it is the backing library for Spring Security and Ktor's `jwt` plugin. For Ktor applications, `io.ktor:ktor-server-auth-jwt` wraps `java-jwt` (Auth0) and is idiomatic in that context. For Spring Boot Kotlin projects, follow the Java archetype's Spring Security approach with Kotlin-idiomatic configuration DSL.

## Reference implementation
```kotlin
// Ktor — Application.kt
import com.auth0.jwt.JWT
import com.auth0.jwt.algorithms.Algorithm
import io.ktor.server.auth.*
import io.ktor.server.auth.jwt.*
import java.security.interfaces.RSAPublicKey
import java.util.Date

val algorithm = Algorithm.RSA256(publicKey as RSAPublicKey, null)

fun Application.configureSecurity() {
    val verifier = JWT.require(algorithm)
        .withIssuer("https://auth.example.com")
        .withAudience("https://api.example.com")
        .acceptLeeway(30)
        .build()

    install(Authentication) {
        jwt("auth-jwt") {
            realm = "example"
            verifier(verifier)
            validate { credential ->
                val exp = credential.payload.expiresAt ?: return@validate null
                if (exp.before(Date())) null
                else JWTPrincipal(credential.payload)
            }
            challenge { _, _ ->
                call.respond(HttpStatusCode.Unauthorized, "Token invalid or expired")
            }
        }
    }
}

fun issueToken(subject: String, privateKey: RSAPrivateKey): String =
    JWT.create()
        .withSubject(subject)
        .withIssuer("https://auth.example.com")
        .withAudience("https://api.example.com")
        .withIssuedAt(Date())
        .withExpiresAt(Date(System.currentTimeMillis() + 15 * 60 * 1000))
        .sign(Algorithm.RSA256(null, privateKey))
```

## Language-specific gotchas
- Ktor's `validate` block receives a `JWTCredential` — this is **after** signature and expiry verification by the `verifier`. Do not re-check the signature here; check only application-level claims (roles, scopes).
- The `challenge` block must respond; if it does not, Ktor defaults to a 401 with an empty body, which may confuse clients expecting a `WWW-Authenticate` header.
- Kotlin null safety helps here: `credential.payload.expiresAt` is nullable — the explicit null check in `validate` ensures expired tokens are rejected even if the verifier misconfigures `acceptExpiresAt`.
- When using `Algorithm.RSA256(publicKey, null)` for verification-only, the second parameter (private key) must be `null`. Passing the wrong key type throws a runtime exception, not a compile error.
- In coroutine contexts (Ktor, Spring WebFlux), JWT parsing is CPU-bound. Move it off the main dispatcher if throughput is critical: `withContext(Dispatchers.Default) { verifier.verify(token) }`.

## Tests to write
- `GET /protected` with a valid JWT → 200 and `call.principal<JWTPrincipal>()` is non-null.
- Expired JWT → 401 with `challenge` body.
- JWT with wrong issuer → 401.
- JWT signed with a different RSA key → 401.
- JWT missing `aud` → 401.
