---
schema_version: 1
archetype: persistence/nosql-injection
language: typescript
principles_file: _principles.md
libraries:
  preferred: mongoose
  acceptable:
    - mongodb
    - ioredis
  avoid:
    - name: req.body cast to filter object
      reason: TypeScript types are erased at runtime; a typed field variable still accepts the runtime object that carries BSON operators.
minimum_versions:
  node: "22"
  typescript: "5.7"
---

# NoSQL Injection Defense — TypeScript

## Library choice
`mongoose` with TypeScript generics provides compile-time model typing. `zod` validates the runtime shape of incoming data. The combination means the TypeScript type system catches structural mismatches at development time and Zod catches them at runtime — closing the gap that type erasure opens.

## Reference implementation
```typescript
import mongoose, { Document, Model, Schema } from "mongoose";
import { z } from "zod";

interface IUser {
    email: string;
    role: "user" | "admin";
    createdAt: Date;
}

const userSchema = new Schema<IUser>({
    email:     { type: String, required: true, index: true },
    role:      { type: String, enum: ["user", "admin"], default: "user" },
    createdAt: { type: Date, default: Date.now },
});

const UserModel: Model<IUser> = mongoose.model<IUser>("User", userSchema);

const EmailInput = z.object({ email: z.string().email().max(320) });
const SortInput  = z.object({ field: z.enum(["email", "createdAt"]) });

type EmailInput = z.infer<typeof EmailInput>;
type SortInput  = z.infer<typeof SortInput>;

export async function findByEmail(raw: unknown): Promise<IUser | null> {
    const { email } = EmailInput.parse(raw);
    return UserModel.findOne({ email }).lean<IUser>();
}

export async function listSorted(raw: unknown): Promise<IUser[]> {
    const { field } = SortInput.parse(raw);
    return UserModel.find().sort({ [field]: 1 }).limit(50).lean<IUser[]>();
}
```

## Language-specific gotchas
- TypeScript `as` casts do not validate at runtime. `const input = req.body as { email: string }` does not prevent `req.body.email` being `{ $ne: "" }` at runtime. Always pair type assertions with a Zod parse.
- The `mongodb` driver's `Filter<T>` generic type accepts `{ $ne: string }` as a valid value for a `string` field because `FilterOperators<string>` includes operators. Accepting `Filter<IUser>` from an external caller is not safe.
- Enable `mongoose.set("sanitizeFilter", true)` globally as defence-in-depth.
- For `ioredis`, validate key segments with a regex before string interpolation even when the type is `string` — the type annotation does not constrain the runtime value.
- `strictNullChecks: true` must be enabled in `tsconfig.json` — without it, null returns from `findOne` are invisible to the compiler.

## Tests to write
- `findByEmail({ email: { $ne: "" } })` throws `ZodError` — verify with `expect(() => ...).rejects.toThrow(ZodError)`.
- `listSorted({ field: "password" })` throws `ZodError`.
- Type test: ensure `findByEmail` accepts `unknown` input (not a pre-cast type) so callers cannot bypass Zod.
- Integration: round-trip a user through insert and `findByEmail`, assert field equality.
