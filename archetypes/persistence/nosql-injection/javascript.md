---
schema_version: 1
archetype: persistence/nosql-injection
language: javascript
principles_file: _principles.md
libraries:
  preferred: mongoose
  acceptable:
    - mongodb
    - ioredis
  avoid:
    - name: req.body as MongoDB filter
      reason: Express req.body is parsed JSON — an object value injects BSON operators directly into the query.
minimum_versions:
  node: "22"
---

# NoSQL Injection Defense — JavaScript

## Library choice
`mongoose` provides schema-enforced documents. Querying with a model method like `User.findOne({ email })` where `email` is typed as `String` in the schema causes Mongoose to cast the value — if the user sends `{ "$ne": "" }`, the cast to `String` produces the literal string `"[object Object]"`, not an operator. This is not a complete defense (casting rules vary by type), so explicit type validation is still required. Use `zod` or `joi` to validate the request body before any database call.

## Reference implementation
```javascript
import mongoose from "mongoose";
import { z } from "zod";

const UserSchema = new mongoose.Schema({
    email: { type: String, required: true, index: true },
    role:  { type: String, enum: ["user", "admin"], default: "user" },
    createdAt: { type: Date, default: Date.now },
});
const User = mongoose.model("User", UserSchema);

const SORT_FIELDS = new Set(["email", "createdAt"]);

const EmailInput = z.object({ email: z.string().email().max(320) });
const SortInput  = z.object({ field: z.enum(["email", "createdAt"]) });

export async function findByEmail(rawInput) {
    const { email } = EmailInput.parse(rawInput); // throws ZodError on bad shape
    return User.findOne({ email }).lean();
}

export async function listSorted(rawInput) {
    const { field } = SortInput.parse(rawInput);
    return User.find().sort({ [field]: 1 }).limit(50).lean();
}
```

## Language-specific gotchas
- `req.body` in Express is a parsed JavaScript object. If the client sends `{"email": {"$ne": ""}}`, `req.body.email` is the object `{"$ne": ""}`. Mongoose's String cast converts this to `"[object Object]"` rather than executing as an operator — but this is fragile. Validate types explicitly.
- `mongodb` (the low-level driver) does not schema-cast. Passing `req.body` directly as a filter to `collection.findOne(req.body)` is a direct injection vector.
- Never use `$where` with any user-supplied string. The Node.js MongoDB driver does not block it.
- For `ioredis`, sanitise key segments: check that user-supplied segments match `/^[a-zA-Z0-9_-]{1,64}$/` before interpolating into a key.
- `mongoose.sanitizeFilter()` (available since Mongoose 6) strips `$` keys from filter objects. Enable it globally with `mongoose.set("sanitizeFilter", true)` as a defence-in-depth measure — not as a substitute for input validation.

## Tests to write
- `findByEmail({ email: { $ne: "" } })` throws `ZodError` before any database call.
- `listSorted({ field: "password" })` throws `ZodError`.
- Integration: insert a user, retrieve by exact email, confirm single result.
- Confirm `mongoose.set("sanitizeFilter", true)` strips operator keys in a direct filter test.
