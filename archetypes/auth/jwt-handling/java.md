---
schema_version: 1
archetype: auth/jwt-handling
language: java
principles_file: _principles.md
libraries:
  preferred: com.nimbusds:nimbus-jose-jwt
  acceptable:
    - io.jsonwebtoken:jjwt-api
  avoid:
    - name: com.auth0:java-jwt (standalone, not spring-security-oauth2-resource-server)
      reason: Acceptable but nimbus-jose-jwt is the Spring Security default and more complete.
minimum_versions:
  java: "21"
---

# JWT Handling — Java

## Library choice
`nimbus-jose-jwt` is the JOSE library embedded in Spring Security's OAuth2 Resource Server support — it handles JWK set retrieval, key rotation, algorithm allowlisting, and all registered claim validation. When using Spring Security, delegate entirely to `spring-boot-starter-oauth2-resource-server` rather than calling Nimbus directly. For non-Spring contexts, `jjwt` (JJWT) from io.jsonwebtoken is a fluent alternative with strong type safety.

## Reference implementation
```java
// Spring Boot resource server — application.yml
// spring.security.oauth2.resourceserver.jwt.jwk-set-uri: https://auth.example.com/.well-known/jwks.json

// SecurityConfig.java
@Configuration
@EnableWebSecurity
public class SecurityConfig {

    @Bean
    public SecurityFilterChain filterChain(HttpSecurity http) throws Exception {
        return http
            .authorizeHttpRequests(a -> a
                .requestMatchers("/health").permitAll()
                .anyRequest().authenticated())
            .oauth2ResourceServer(o -> o
                .jwt(jwt -> jwt.jwtAuthenticationConverter(claimsConverter())))
            .sessionManagement(s -> s.sessionCreationPolicy(STATELESS))
            .build();
    }

    @Bean
    JwtDecoder jwtDecoder(OAuth2ResourceServerProperties props) {
        NimbusJwtDecoder decoder = NimbusJwtDecoder
            .withJwkSetUri(props.getJwt().getJwkSetUri())
            .jwsAlgorithm(SignatureAlgorithm.RS256)   // explicit allowlist
            .build();
        decoder.setJwtValidator(JwtValidators.createDefaultWithIssuer("https://auth.example.com"));
        return decoder;
    }

    private JwtAuthenticationConverter claimsConverter() {
        var c = new JwtAuthenticationConverter();
        c.setJwtGrantedAuthoritiesConverter(jwt ->
            ((List<String>) jwt.getClaim("roles")).stream()
                .map(r -> new SimpleGrantedAuthority("ROLE_" + r))
                .toList());
        return c;
    }
}
```

## Language-specific gotchas
- `NimbusJwtDecoder.withJwkSetUri(...).jwsAlgorithm(...)` restricts the algorithm. Without it, the decoder accepts any algorithm the JWKS endpoint advertises, which is wider than you want.
- `JwtValidators.createDefaultWithIssuer` validates `exp`, `nbf`, and `iss`. Audience validation requires a custom `DelegatingOAuth2TokenValidator` with `JwtClaimValidator`.
- The Spring Security JWT filter runs before the dispatcher servlet — do not add a second JWT check in a `HandlerInterceptor`. Two checks with different configurations create gaps.
- `jwt.getClaim("roles")` returns `Object` at runtime; cast carefully or use a typed `JwtClaimsSet` accessor. A malformed payload can put unexpected types in that field.
- In tests, use `spring-security-test`'s `SecurityMockMvcRequestPostProcessors.jwt()` to inject a pre-built authentication rather than generating real tokens.

## Tests to write
- Endpoint with valid JWT → 200 and correct `Authentication` principal.
- Expired JWT → 401 with `WWW-Authenticate: Bearer error="invalid_token"`.
- JWT signed with an unknown key → 401.
- JWT with `alg: none` or an algorithm not in the allowlist → 401.
- Missing audience when audience validation is configured → 401.
