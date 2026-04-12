---
schema_version: 1
archetype: auth/jwt-handling
language: go
principles_file: _principles.md
libraries:
  preferred: github.com/golang-jwt/jwt/v5
  acceptable:
    - github.com/lestrrat-go/jwx/v2
  avoid:
    - name: github.com/dgrijalva/jwt-go
      reason: Unmaintained since 2020; known CVE for algorithm confusion (CVE-2020-26160).
minimum_versions:
  go: "1.22"
---

# JWT Handling — Go

## Library choice
`github.com/golang-jwt/jwt/v5` is the maintained community fork of the original `jwt-go`. It validates signatures and registered claims with a clean, generic API. `github.com/lestrrat-go/jwx/v2` is a full JOSE implementation (JWE, JWK, JWS) better suited when you need to consume a remote JWKS endpoint or issue JWE tokens. For plain RS256/ES256 access token validation, `golang-jwt/jwt/v5` is the lighter choice.

## Reference implementation
```go
package auth

import (
    "crypto/rsa"
    "errors"
    "time"
    "github.com/golang-jwt/jwt/v5"
)

const issuer = "https://auth.example.com"
const audience = "https://api.example.com"

func Issue(sub string, key *rsa.PrivateKey, kid string) (string, error) {
    now := time.Now().UTC()
    claims := jwt.RegisteredClaims{
        Subject: sub, Issuer: issuer, Audience: jwt.ClaimStrings{audience},
        IssuedAt: jwt.NewNumericDate(now), ExpiresAt: jwt.NewNumericDate(now.Add(15 * time.Minute)),
    }
    t := jwt.NewWithClaims(jwt.SigningMethodRS256, claims)
    t.Header["kid"] = kid
    return t.SignedString(key)
}

func Validate(tokenStr string, pub *rsa.PublicKey) (*jwt.RegisteredClaims, error) {
    p := jwt.NewParser(
        jwt.WithValidMethods([]string{"RS256"}),
        jwt.WithIssuedAt(), jwt.WithIssuer(issuer), jwt.WithAudience(audience),
        jwt.WithLeeway(30*time.Second),
    )
    token, err := p.ParseWithClaims(tokenStr, &jwt.RegisteredClaims{}, func(t *jwt.Token) (any, error) {
        if _, ok := t.Method.(*jwt.SigningMethodRSA); !ok {
            return nil, errors.New("unexpected signing method")
        }
        return pub, nil
    })
    if err != nil || !token.Valid {
        return nil, errors.New("invalid token")
    }
    return token.Claims.(*jwt.RegisteredClaims), nil
}
```

## Language-specific gotchas
- Always use `jwt.NewParser` with `jwt.WithValidMethods` — the old `jwt.Parse` without a method check allowed algorithm substitution attacks.
- The `Keyfunc` must check `t.Method` type before returning the key. An attacker can set `alg: HS256` and sign with the public key (as bytes) if the keyfunc blindly returns whatever key it has.
- `jwt.RegisteredClaims.Audience` is `jwt.ClaimStrings` (a `[]string`). Validate it with `jwt.WithAudience` — do not compare manually.
- `WithLeeway` of 30 seconds is appropriate for clock drift between services. Do not set it higher than 60 seconds.
- The original `github.com/dgrijalva/jwt-go` is in many transitive dependency trees. Run `go mod graph | grep dgrijalva` to confirm you are not pulling it in.

## Tests to write
- `Validate(Issue(sub, roles, priv, kid), pub)` returns claims with correct `sub` and `roles`.
- Token with past `ExpiresAt` → error containing "expired".
- Token signed with a different RSA key → error.
- Token with `alg: HS256` header value → keyfunc returns error → validation fails.
- Token with a different `iss` or `aud` → validation fails.
