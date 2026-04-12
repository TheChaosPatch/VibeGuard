---
schema_version: 1
archetype: http/request-smuggling
language: java
principles_file: _principles.md
libraries:
  preferred: Spring Boot (embedded Tomcat / Netty)
  acceptable:
    - Jetty embedded
    - Undertow embedded
  avoid:
    - name: Tomcat 8.5 or earlier
      reason: Known CL+TE desync bugs fixed in Tomcat 9.0.31+; upgrade to Tomcat 10.x (Jakarta EE).
minimum_versions:
  java: "21"
  spring_boot: "3.3"
---

# HTTP Request Smuggling — Java

## Library choice
Spring Boot's embedded Tomcat (10.x, Jakarta EE) and Netty (for reactive/WebFlux) both implement RFC 9112 compliant header rejection for conflicting `Content-Length` and `Transfer-Encoding` headers. The primary mitigation is keeping the embedded server up-to-date and configuring it to reject ambiguous requests explicitly. Add a `javax.servlet.Filter` (Tomcat) or `WebFilter` (WebFlux) as defense-in-depth. For proxy scenarios, Spring Cloud Gateway over HTTP/2 eliminates desync surface.

## Reference implementation
```java
// SmugglingGuardFilter.java (Spring MVC / Tomcat)
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.springframework.core.annotation.Order;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

import java.io.IOException;

@Component
@Order(Integer.MIN_VALUE)   // run before all other filters
public class SmugglingGuardFilter extends OncePerRequestFilter {

    @Override
    protected void doFilterInternal(
            HttpServletRequest request,
            HttpServletResponse response,
            FilterChain chain) throws ServletException, IOException {

        boolean hasCl = request.getHeader("Content-Length") != null;
        boolean hasTe = request.getHeader("Transfer-Encoding") != null;

        if (hasCl && hasTe) {
            response.sendError(
                HttpServletResponse.SC_BAD_REQUEST,
                "Ambiguous request length headers.");
            return;
        }

        String te = request.getHeader("Transfer-Encoding");
        if (te != null) {
            String normalized = te.trim().toLowerCase();
            if (!normalized.equals("chunked") && !normalized.equals("identity")) {
                response.sendError(
                    HttpServletResponse.SC_BAD_REQUEST,
                    "Non-standard Transfer-Encoding rejected.");
                return;
            }
        }

        chain.doFilter(request, response);
    }
}
```

```yaml
# application.yml — Tomcat connector hardening
server:
  tomcat:
    max-http-form-post-size: 2MB
    max-swallow-size: 2MB
    connection-timeout: 20000     # ms; close idle connections sooner
    keep-alive-timeout: 15000     # ms; limit shared-connection window
    max-keep-alive-requests: 100  # close after N requests
  max-http-request-header-size: 32KB
```

## Language-specific gotchas
- Tomcat 10.x (Jakarta EE 10) is the minimum for a fully patched HTTP/1.1 parser. Tomcat 9 is end-of-life. Spring Boot 3.x uses Tomcat 10 by default.
- `request.getHeader("Transfer-Encoding")` returns only the first value if the header is repeated. Use `request.getHeaders("Transfer-Encoding")` (returns an `Enumeration`) to catch multiple values and reject them.
- Spring WebFlux (Netty): implement `WebFilter` and examine `ServerWebExchange.getRequest().getHeaders()`. Netty itself rejects CL+TE conflicts; the filter adds auditable defense-in-depth.
- `@Order(Integer.MIN_VALUE)` places the filter at the very beginning of the chain — before Spring Security, before authentication. This is intentional: smuggled headers should be rejected before any business logic executes.
- Load balancers in front of Spring Boot: AWS ALB speaks HTTP/2 to targets natively when Target Group protocol version is `HTTP2` — configure this to eliminate H2-to-H1 downgrade.
- `connection-timeout` vs `keep-alive-timeout`: both should be set. `connection-timeout` covers the initial handshake; `keep-alive-timeout` covers idle persistent connections used in smuggling attacks.

## Tests to write
- `MockMvc`: POST with both `Content-Length: 0` and `Transfer-Encoding: chunked` headers — expect 400.
- POST with `Transfer-Encoding: xchunked` — expect 400.
- Normal POST with body — expect 200.
- Filter order: verify `SmugglingGuardFilter` runs before `UsernamePasswordAuthenticationFilter`.
- `application.yml` integration: verify `max-keep-alive-requests` is applied by checking Tomcat MBean via `TomcatEmbeddedWebappClassLoader`.
