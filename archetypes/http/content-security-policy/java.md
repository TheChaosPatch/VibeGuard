---
schema_version: 1
archetype: http/content-security-policy
language: java
principles_file: _principles.md
libraries:
  preferred: Spring Security (HeadersConfigurer / ContentSecurityPolicyHeaderWriter)
  acceptable:
    - Servlet Filter with SecureRandom (framework-agnostic)
  avoid:
    - name: OWASP CSRFGuard CSP helpers
      reason: CSRFGuard is a CSRF library; it has no CSP nonce management.
minimum_versions:
  java: "21"
  spring_boot: "3.3"
---

# Content Security Policy — Java

## Library choice
Spring Security's `HttpSecurity.headers().contentSecurityPolicy()` covers static CSP strings but does not natively support per-request nonces. Implement nonce injection with a `OncePerRequestFilter` that uses `SecureRandom` to generate a nonce, stores it in a request attribute, builds the CSP string, and sets the header before the response is committed. For Thymeleaf templates, expose the nonce via the request attribute and reference it with `th:attr="nonce=${cspNonce}"`.

## Reference implementation
```java
// CspNonceFilter.java
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

import java.io.IOException;
import java.security.SecureRandom;
import java.util.Base64;

@Component
public class CspNonceFilter extends OncePerRequestFilter {

    private static final SecureRandom RANDOM = new SecureRandom();
    private static final String NONCE_ATTR = "cspNonce";

    @Override
    protected void doFilterInternal(
            HttpServletRequest request,
            HttpServletResponse response,
            FilterChain chain) throws ServletException, IOException {

        byte[] bytes = new byte[16];
        RANDOM.nextBytes(bytes);
        String nonce = Base64.getEncoder().encodeToString(bytes);
        request.setAttribute(NONCE_ATTR, nonce);

        String policy = String.format(
            "default-src 'none'; " +
            "script-src 'nonce-%s' 'strict-dynamic'; " +
            "style-src 'nonce-%s' 'self'; " +
            "img-src 'self' data:; " +
            "connect-src 'self'; " +
            "font-src 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "upgrade-insecure-requests",
            nonce, nonce);

        response.setHeader("Content-Security-Policy", policy);
        chain.doFilter(request, response);
    }
}
```

```java
// SecurityConfig.java — disable Spring Security's default CSP to avoid conflict
@Configuration
@EnableWebSecurity
public class SecurityConfig {
    @Bean
    public SecurityFilterChain filterChain(HttpSecurity http) throws Exception {
        http.headers(headers -> headers.contentSecurityPolicy(csp -> {}));
        // CspNonceFilter is a @Component; Spring Boot auto-registers it.
        return http.build();
    }
}
```

## Language-specific gotchas
- `SecureRandom` is thread-safe; instantiate it once as a static field. Avoid `new SecureRandom()` per request — seeding is expensive.
- Spring Security's `ContentSecurityPolicyHeaderWriter` sets a static string and will overwrite a dynamically set header if it runs after your filter. Either disable the Spring Security CSP writer or register your filter with `Order(Ordered.HIGHEST_PRECEDENCE)`.
- `HttpServletResponse.setHeader()` must be called before `chain.doFilter()` — once the response body starts writing the headers are committed and `setHeader` is a no-op.
- Thymeleaf: reference the nonce as `<script th:attr="nonce=${cspNonce}">`. The SpEL expression reads from the request attribute. Ensure the Thymeleaf context populates request attributes (default in Spring MVC).
- Jakarta EE applications (non-Spring): implement `jakarta.servlet.Filter` instead of `OncePerRequestFilter` and register via `web.xml` or `@WebFilter`.
- `String.format` with `%s` is fine here; the nonce is base64 (alphanumeric + `+`, `/`, `=`) so there is no injection risk inside the header value.

## Tests to write
- `MockMvc` GET: response header `Content-Security-Policy` contains `nonce-` followed by a valid base64 string.
- Two sequential requests produce different nonce values.
- Request attribute `cspNonce` is set after filter execution and accessible to the view layer.
- `frame-ancestors 'none'` is present in the policy string.
- `@SpringBootTest` with `TestRestTemplate`: verify the header on a live port response.
