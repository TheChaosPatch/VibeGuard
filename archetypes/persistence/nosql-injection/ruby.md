---
schema_version: 1
archetype: persistence/nosql-injection
language: ruby
principles_file: _principles.md
libraries:
  preferred: mongoid
  acceptable:
    - mongo
    - redis-rb
  avoid:
    - name: params hash as Mongoid/Mongo query
      reason: Rails/Sinatra params hashes allow nested key injection; passing them directly as a Mongoid where clause passes operator keys to MongoDB.
minimum_versions:
  ruby: "3.4"
---

# NoSQL Injection Defense — Ruby

## Library choice
`mongoid` is the standard ODM for Ruby. Its `where` clause accepts a hash — but the hash must be constructed from validated, typed values, not passed through from `params`. For the low-level `mongo` driver, use the same `Filters` pattern. For Redis, `redis-rb` uses method-based commands; validate key segments before interpolation.

## Reference implementation
```ruby
require "mongoid"

SORT_FIELDS = %w[email created_at].freeze
USER_ID_PATTERN = /\A[a-f0-9]{24}\z/

class User
  include Mongoid::Document
  field :email,      type: String
  field :role,       type: String, default: "user"
  field :created_at, type: Time
end

class UserRepository
  def find_by_email(email)
    raise ArgumentError, "email must be a String" unless email.is_a?(String)
    raise ArgumentError, "email is blank"         if email.strip.empty?
    # Plain string value — no operator injection possible.
    User.where(email: email).first
  end

  def list_sorted(field)
    raise ArgumentError, "Unknown sort field: #{field}" unless SORT_FIELDS.include?(field)
    User.all.order_by(field => :asc).limit(50)
  end
end
```

## Language-specific gotchas
- `User.where(params[:filter])` or `User.where(params)` passes the raw params hash, which Rails populates with nested hashes from query strings like `filter[$ne]=`. Never do this.
- Mongoid's `where` does not strip operator keys from nested hashes. `User.where(email: { "$ne" => "" })` executes as a `$ne` query.
- `String(params[:email])` converts the param to a string representation of the object, not to a validated scalar — `{"$ne"=>""}` becomes the string `"{\"$ne\"=>\"\"}"` which is effectively safe but unintended. Use `is_a?(String)` instead.
- For `redis-rb`, use `redis.get("session:#{user_id}")` only after `user_id` matches `USER_ID_PATTERN`.
- Mass-assignment via Mongoid's `User.new(params)` can set operator-named fields if the model attributes are not explicitly permitted. Use strong parameters or an explicit attribute list.

## Tests to write
- `find_by_email({ "$ne" => "" })` raises `ArgumentError` before any database call.
- `list_sorted("password")` raises `ArgumentError`.
- RSpec integration: create a user, retrieve by email, assert record equality.
- Verify `User.where(email: { "$ne" => "" })` is not reachable from the repository's public interface.
