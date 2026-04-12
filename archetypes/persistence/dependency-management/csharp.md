---
schema_version: 1
archetype: persistence/dependency-management
language: csharp
principles_file: _principles.md
libraries:
  preferred: NuGet with Central Package Management
  acceptable:
    - PackageReference with exact versions
  avoid:
    - name: Floating version ranges in application projects
      reason: "A compromised patch lands in your build without review."
    - name: packages.config (legacy format)
      reason: No transitive dependency resolution. Migrate to PackageReference.
minimum_versions:
  dotnet: "10.0"
---

# Dependency Management — C#

## Library choice
NuGet is the only package manager for .NET. Use `Central Package Management` (a `Directory.Packages.props` file at the repo root) to pin every version in one place. This ensures all projects in the solution resolve the same version of a shared dependency and makes version review trivial — the diff is in one file. Enable NuGet package signature verification and locked-mode restore in CI so that the resolved graph is reproducible and tamper-evident.

## Reference implementation
```xml
<!-- Directory.Packages.props — repo root -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageVersion Include="Serilog" Version="4.2.0" />
    <PackageVersion Include="xunit" Version="2.9.3" />
  </ItemGroup>
</Project>
```
```xml
<!-- nuget.config — repo root -->
<configuration>
  <config>
    <add key="signatureValidationMode" value="require" />
  </config>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```
```bash
# CI pipeline — locked-mode restore and vulnerability check
dotnet restore --locked-mode
dotnet list package --vulnerable --include-transitive
```

## Language-specific gotchas
- `dotnet restore` without `--locked-mode` silently resolves newer patch versions if the lockfile (`packages.lock.json`) is missing or stale. In CI, always pass `--locked-mode` and fail the build if the lockfile is out of date.
- Enable `RestorePackagesWithLockFile` in `Directory.Build.props` to generate lockfiles automatically: `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`. Commit the resulting `packages.lock.json` files.
- `dotnet list package --vulnerable` checks the NuGet advisory database. It does not check CVEs from other sources. Supplement with a dedicated SCA tool (Snyk, GitHub Dependabot, OSV-Scanner) for broader coverage.
- Central Package Management + `<PackageVersion>` uses exact versions by default. Do not add `[1.0,2.0)` ranges — they defeat the purpose of pinning.
- `<PackageSourceMapping>` prevents dependency confusion. If you have a private feed for internal packages, map your internal namespace prefix to that feed and everything else to nuget.org. Without this, a higher-version public package with your internal name wins resolution.
- NuGet's signature verification (`signatureValidationMode: require`) rejects unsigned packages. Some legacy packages are unsigned — evaluate whether the risk of disabling verification for one package outweighs the risk of using an unsigned artifact.
- Transitive dependencies are invisible in the `.csproj` but visible in `dotnet list package --include-transitive`. Audit these when they change — a new transitive dependency is an unreviewed trust decision.

## Tests to write
- CI lockfile validation: `dotnet restore --locked-mode` succeeds without modification in a clean checkout.
- Vulnerability scan: `dotnet list package --vulnerable --include-transitive` exits with zero findings (or documented, accepted exceptions).
- Central Package Management: assert all `.csproj` files use `<PackageReference Include="..." />` without `Version=` attributes (version comes from `Directory.Packages.props`).
- Source mapping: assert `nuget.config` has `<packageSourceMapping>` and that internal package namespaces are scoped to the private feed.
