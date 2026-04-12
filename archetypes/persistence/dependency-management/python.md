---
schema_version: 1
archetype: persistence/dependency-management
language: python
principles_file: _principles.md
libraries:
  preferred: pip + pip-audit + poetry (or uv)
  acceptable:
    - pip-tools (pip-compile)
  avoid:
    - name: pip install without pinned versions
      reason: "Unreproducible builds. A compromised new release installs silently."
    - name: setup.py with open-ended install_requires
      reason: Libraries may use ranges; deployed applications must pin.
minimum_versions:
  python: "3.10"
---

# Dependency Management — Python

## Library choice
The Python ecosystem has multiple packaging tools; what matters is the discipline, not the tool. Use `poetry` (with `poetry.lock`) or `uv` (with `uv.lock`) for application projects — both generate lockfiles with integrity hashes and support locked installs. For simpler setups, `pip-tools` (`pip-compile`) generates a pinned `requirements.txt` with hashes. In all cases, commit the lockfile to version control, install with `--require-hashes` in CI, and run `pip-audit` on every PR.

## Reference implementation
```toml
# pyproject.toml — application project (poetry)
[tool.poetry]
name = "myservice"
version = "1.0.0"

[tool.poetry.dependencies]
python = "^3.10"
sqlalchemy = "2.0.36"
httpx = "0.28.1"
pydantic = "2.10.4"

[tool.poetry.group.dev.dependencies]
pytest = "8.3.4"
pip-audit = "2.9.0"
```
```bash
# CI pipeline
poetry install --no-interaction
poetry lock --check            # fails if lockfile is stale
pip-audit --require-hashes --strict
```
```bash
# Alternative: pip-tools
pip-compile --generate-hashes requirements.in -o requirements.txt
pip install --require-hashes -r requirements.txt
pip-audit -r requirements.txt --strict
```

## Language-specific gotchas
- `pip install sqlalchemy` without a version specifier installs the latest release. In CI, this means Tuesday's build may get a different version than Monday's. Always pin: `sqlalchemy==2.0.36`.
- `poetry.lock` and `uv.lock` record exact resolved versions and hashes. If the lockfile is not committed, `poetry install` resolves fresh on every machine and reproducibility is lost.
- `pip-audit` checks the OSV and PyPI advisory databases. Run it in CI with `--strict` so that any finding fails the build. Do not silence findings with `--ignore` unless the exception is documented and time-boxed.
- `--require-hashes` in pip verifies every downloaded wheel against the hashes in the requirements file. This is the only defense against a PyPI compromise or MITM that serves a tampered package. It also requires that every dependency (including transitives) has a hash — which forces you to pin everything.
- Dependency confusion: if you use a private PyPI index for internal packages, configure pip's `--index-url` to your private index and `--extra-index-url` to public PyPI. Better: use `--index-url` pointed at a private index that proxies public PyPI and blocks public packages with your internal namespace.
- `pip install -e .` (editable installs) in CI bypasses the lockfile and installs from the local source tree. Use `pip install .` or `poetry install` for CI builds.
- Virtual environments are mandatory. Installing into the system Python pollutes the global site-packages and creates untracked version conflicts.

## Tests to write
- Lockfile freshness: `poetry lock --check` (or `pip-compile --check`) exits 0 in CI — fails if someone added a dependency without regenerating the lockfile.
- Hash verification: `pip install --require-hashes -r requirements.txt` succeeds from a clean virtual environment.
- Vulnerability scan: `pip-audit --strict` exits 0 with no findings (or documented exceptions).
- No unpinned dependencies: parse `requirements.txt` or `pyproject.toml` and assert every direct dependency has an exact version (`==`), not a range.
