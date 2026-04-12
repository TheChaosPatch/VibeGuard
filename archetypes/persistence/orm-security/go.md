---
schema_version: 1
archetype: persistence/orm-security
language: go
principles_file: _principles.md
libraries:
  preferred: GORM
  acceptable:
    - sqlx
    - database/sql (stdlib)
  avoid:
    - name: Binding JSON directly to GORM models
      reason: Mass assignment via struct tags. Use separate input structs.
    - name: db.Raw with fmt.Sprintf
      reason: SQL injection through the ORM's raw escape hatch.
minimum_versions:
  go: "1.22"
---

# ORM Security — Go

## Library choice
`GORM` is the most common Go ORM and the default for this archetype. It auto-parameterizes `.Where()` calls but its `Raw()` and `Exec()` methods accept raw SQL that you must parameterize yourself. The bigger risk in GORM is mass assignment: `db.Create(&user)` writes every non-zero field, and if you decoded the HTTP body directly into the model struct, the client controls which columns are set. Use separate request/response structs and map fields explicitly. `sqlx` and `database/sql` are acceptable lower-level alternatives that do not have mass assignment risk but require manual query construction.

## Reference implementation
```go
package users

import (
	"context"
	"fmt"
	"gorm.io/gorm"
)

type Entity struct { // internal — never bind from HTTP, never return in responses
	ID, Email, DisplayName string
	IsAdmin                bool   `json:"-" gorm:"default:false"`
	PassHash               string `json:"-"`
}

type CreateCmd struct { // allowlists the fields the client may set
	Email       string `json:"email"       validate:"required,email"`
	DisplayName string `json:"display_name" validate:"required,max=200"`
}

type Response struct{ ID, Email, DisplayName string }

const maxPage = 100

type Repo struct{ db *gorm.DB }

func (r *Repo) Create(ctx context.Context, cmd CreateCmd) (Response, error) {
	e := Entity{ID: newID(), Email: cmd.Email, DisplayName: cmd.DisplayName}
	if err := r.db.WithContext(ctx).Create(&e).Error; err != nil {
		return Response{}, fmt.Errorf("users: create: %w", err)
	}
	return Response{e.ID, e.Email, e.DisplayName}, nil
}

func (r *Repo) List(ctx context.Context, limit int) ([]Response, error) {
	if limit <= 0 || limit > maxPage {
		limit = maxPage
	}
	var ents []Entity
	if err := r.db.WithContext(ctx).Order("email").Limit(limit).Find(&ents).Error; err != nil {
		return nil, fmt.Errorf("users: list: %w", err)
	}
	out := make([]Response, len(ents))
	for i, e := range ents {
		out[i] = Response{e.ID, e.Email, e.DisplayName}
	}
	return out, nil
}
```

## Language-specific gotchas
- `json.NewDecoder(r.Body).Decode(&entity)` where `entity` is a GORM model lets the client set `IsAdmin`, `PassHash`, and any other exported field. Decode into a `CreateCmd` struct with only the allowed fields.
- `db.Raw(fmt.Sprintf("SELECT * FROM users WHERE email = '%s'", email))` is injection. Use `db.Raw("SELECT * FROM users WHERE email = ?", email)` — GORM binds `?` placeholders.
- GORM's `Updates(map[string]interface{}{...})` writes exactly the keys in the map. `Updates(entity)` skips zero-value fields but writes non-zero ones — if the client set `IsAdmin: true` in the JSON, it goes to the database.
- GORM's `Preload("Orders")` eagerly loads the relationship. If you serialize the parent struct including the preloaded field, the response contains data the caller may not be authorized to see. Use the response struct to control output shape.
- `db.Find(&entities)` without `.Limit()` loads the entire table. Always apply a limit with a server-enforced cap.
- GORM's default logger logs SQL queries including bound values at `Info` level. In production, set `logger.Default.LogMode(logger.Silent)` or `logger.Warn` to avoid logging sensitive query parameters.

## Tests to write
- Mass assignment: POST `{"email":"a@b.com","display_name":"A","is_admin":true}` decoded into `CreateCmd` — assert `IsAdmin` is `false` on the stored entity.
- Response shape: marshal a `Response` to JSON — assert it contains only `id`, `email`, `display_name`.
- Pagination cap: `List(ctx, 999999)` returns at most `maxPage` entities.
- Raw SQL parameterization: if any code path uses `db.Raw()`, call with `"'; DROP TABLE users--"` and assert no error.
- Unique constraint: create two users with the same email — assert a GORM error, not a silent overwrite.
