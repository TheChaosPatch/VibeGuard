---
schema_version: 1
archetype: http/xss
language: go
principles_file: _principles.md
libraries:
  preferred: html/template (auto-escaped by default)
  acceptable:
    - bluemonday (for HTML sanitization)
  avoid:
    - name: text/template for HTML
      reason: Does not escape; using it for HTML output is a direct XSS vector.
    - name: fmt.Fprintf with HTML
      reason: No encoding; string interpolation into HTML bypasses all protection.
minimum_versions:
  go: "1.22"
---

# Cross-Site Scripting Defense — Go

## Library choice
The standard library's `html/template` package is context-aware: it automatically applies HTML, JS, URL, or CSS encoding based on where the interpolation appears in the template. This is the primary defense and is remarkably good. Do not use `text/template` for any output that reaches a browser. For rich-text sanitization (accepting a subset of HTML), use `bluemonday` with a strict policy. The stdlib `html.EscapeString()` covers the HTML body context only — it is not context-aware and should be used only outside templates.

## Reference implementation
```go
package web

import (
	"html/template"
	"net/http"

	"github.com/microcosm-cc/bluemonday"
)

var (
	tmpl = template.Must(template.ParseFiles("templates/profile.html"))

	// Strict policy: only allow basic formatting tags for rich text.
	richTextPolicy = bluemonday.NewPolicy()
)

func init() {
	richTextPolicy.AllowElements("p", "br", "strong", "em", "ul", "ol", "li", "a")
	richTextPolicy.AllowAttrs("href").OnElements("a")
	richTextPolicy.AllowURLSchemes("https")
	richTextPolicy.RequireNoReferrerOnLinks(true)
}

type ProfileData struct {
	DisplayName string // plain text — template auto-escapes
	Bio         string // pre-sanitized rich HTML
}

func ProfileHandler(w http.ResponseWriter, r *http.Request) {
	rawBio := getUserBio(r.Context()) // untrusted HTML from DB
	data := ProfileData{
		DisplayName: getUserName(r.Context()),
		Bio:         richTextPolicy.Sanitize(rawBio),
	}
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	if err := tmpl.Execute(w, data); err != nil {
		http.Error(w, "render error", http.StatusInternalServerError)
	}
}
// In the template: {{ .DisplayName }} is auto-escaped.
// {{ .Bio }} must be marked as template.HTML after sanitization.
```

## Language-specific gotchas
- `template.HTML(userInput)` tells the engine "this is safe — do not escape." Casting unsanitized input to `template.HTML` is the Go equivalent of `| safe` in Jinja2. Only cast after sanitizing through `bluemonday` or equivalent.
- `html/template` is context-aware for `<script>`, `<style>`, and attribute contexts, but only if the template is syntactically valid HTML. A malformed template can confuse the parser and break the context analysis.
- `text/template` and `html/template` have identical APIs. An import typo (`"text/template"` instead of `"html/template"`) silently removes all XSS protection. Use a linter rule or grep to verify.
- `template.JSStr` and `template.JS` are typed wrappers for safe JS content. Use them carefully — `template.JS(userInput)` is as dangerous as `template.HTML(userInput)`.
- `html.EscapeString()` is HTML-body-only. It does not protect against XSS in attributes, URLs, or JS contexts. Rely on `html/template`'s context-aware escaping instead.
- JSON in a `<script>` tag: use `json.Marshal` and inject as `template.JS` — the template engine will not double-escape it, but you must ensure the JSON encoder escapes `</script>` sequences (Go's `json.Marshal` escapes `<`, `>`, `&` by default).

## Tests to write
- Template rendering a `DisplayName` containing `<script>alert(1)</script>` produces escaped `&lt;script&gt;` in the HTTP response body.
- `richTextPolicy.Sanitize("<img onerror=alert(1)>")` returns an empty string.
- `richTextPolicy.Sanitize("<a href=\"javascript:void(0)\">x</a>")` strips the `href` or the tag.
- Verify the import path is `html/template`, not `text/template`, in all files that produce HTML (use a build-time check or test).
- Template with a `<script>var x = "{{.UserInput}}";</script>` block correctly JS-escapes quotes and angle brackets.
