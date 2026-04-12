---
schema_version: 1
archetype: persistence/database-connections
language: ruby
principles_file: _principles.md
libraries:
  preferred: pg
  acceptable:
    - activerecord
    - mysql2
  avoid:
    - name: ruby-pg (old gem name)
      reason: The gem is now published as "pg"; the old name was informally used and may refer to unmaintained forks.
minimum_versions:
  ruby: "3.4"
---

# Database Connection Security — Ruby

## Library choice
`pg` is the PostgreSQL driver. For Rails applications, `activerecord` configures a connection pool internally and accepts database configuration from `database.yml` or `DATABASE_URL`. For MySQL, `mysql2`. TLS is configured via the `sslmode: verify-full` option in `database.yml` or the DSN. Credentials come from environment variables — Rails reads `DATABASE_URL` by default; do not commit `database.yml` with credentials.

## Reference implementation
```ruby
require "pg"
require "connection_pool"

POOL = ConnectionPool.new(size: 20, timeout: 10) do
  PG.connect(
    host:     ENV.fetch("DB_HOST"),
    dbname:   ENV.fetch("DB_NAME"),
    user:     ENV.fetch("DB_USER"),
    password: ENV.fetch("DB_PASSWORD"),
    sslmode:  "verify-full",
    connect_timeout: 10
  )
end

def count_users
  POOL.with do |conn|
    result = conn.exec("SELECT COUNT(*) AS n FROM users")
    result[0]["n"].to_i
  end
end
```

## Language-specific gotchas
- `ENV.fetch("DB_HOST")` raises `KeyError` if the variable is absent — preferred over `ENV["DB_HOST"]` which silently returns `nil` and produces a confusing connection error later.
- `sslmode: "verify-full"` validates certificate and hostname. `sslmode: "require"` encrypts but does not verify. `sslmode: "disable"` sends plaintext.
- Rails `database.yml` supports ERB interpolation (`<%= ENV["DB_PASSWORD"] %>`). The rendered file may appear in logs or error messages; use `database.yml.example` committed to git and keep the real file in `.gitignore`.
- The `connection_pool` gem is for non-Rails Ruby. ActiveRecord manages its own pool via `pool:` and `checkout_timeout:` in `database.yml`.
- Always call `conn.reset` or let the pool manage recycling — do not hold a `PG::Connection` across process-level forks (Unicorn/Puma preload); disconnect in the `before_fork` hook and reconnect in `after_fork`.

## Tests to write
- `ENV.fetch("DB_HOST")` raises `KeyError` when the env var is absent — test with `ENV.delete("DB_HOST")`.
- Integration: `count_users` returns an integer and does not raise.
- TLS: connect with `sslmode: "disable"` to a TLS-required server; assert `PG::ConnectionBad` is raised.
- Pool exhaustion: check out `size + 1` connections with a short timeout; assert `ConnectionPool::TimeoutError` is raised.
