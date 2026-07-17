# ldifdotnet

Pure managed .NET implementation of LDIF (RFC 2849). Deliberately NOT a P/Invoke
binding of OpenLDAP's C code — OpenLDAP compatibility is proven by tests, not
linkage. Primary consumer: an OpenLDAP .NET Aspire integration. Targets the
current LTS only (net10.0, C# 14); the PowerShell module requires pwsh 7.6+
(the .NET 10 host — older hosts cannot load net10 assemblies).

## Commands

- Build and test: `dotnet test`
- Refresh the OpenLDAP fixture corpus: `./tools/get-openldap-fixtures.ps1`
  (release tag is pinned inside the script)
- Differential tests against real OpenLDAP: skipped unless `LDIF_DIFFERENTIAL=1`
  and slapd tools are installed (the Linux CI job does this)
- Try the PowerShell module locally: `Import-Module
  ./src/LdifDotNet.PowerShell/bin/Debug/net8.0/LdifDotNet.PowerShell.psd1`
  (pwsh e2e tests run via `dotnet test` wherever pwsh is on PATH)

## Invariants — mechanically enforced, do not weaken

- `tests/LdifDotNet.Tests/PublicApi.approved.txt` is the API contract. Any
  public-surface change fails tests; review the diff deliberately, re-approve by
  running tests once with `UPDATE_PUBLIC_API=1`, and commit the updated file.
  Never regenerate it without reading the diff.
- `tests/fixtures/` is the behavioral spec: `rfc2849/` holds the RFC's own
  examples (errata-corrected — see its README), `openldap/` is vendored from
  the OpenLDAP project at a pinned tag. Never hand-edit fixture files.
- Reader is tolerant of input (LF or CRLF, trailing whitespace); writer emits
  strictly RFC-conformant output.
- `TreatWarningsAsErrors` is on. CI must be green on ubuntu/windows/macos.
- Code style is spec: `.editorconfig` + `EnforceCodeStyleInBuild` make the
  warning-severity style rules build errors. The enforced set is deliberately
  only what the codebase fully satisfies; escalate a suggestion to warning only
  after making the whole codebase comply. `.ldif`/`.schema` fixtures are exempt
  from whitespace normalization — trailing spaces there are RFC-meaningful.
- Cross-platform is an invariant, enforced two ways: the full suite runs on all
  three OSes in CI, and .NET analyzers run as errors (`AnalysisLevel
  latest-all` in Directory.Build.props; deliberate exceptions are documented in
  .editorconfig and project files), including platform-compat (CA1416) and
  locale-safety rules (CA1305/CA1307/CA1310 — ordinal comparisons, invariant
  formatting).
- Banned APIs (RS0030, BannedSymbols.txt): no clocks (DateTime.Now etc.), no
  unseeded randomness, no process-CWD reliance. These back the generator
  determinism and explicit-path invariants; extend the list rather than
  suppressing RS0030.
- Meziantou.Analyzer runs as errors. MA0048 (one type per file) is the default;
  a file that deliberately groups a type family opts out with a justified
  `#pragma warning disable MA0048` at the top. MA0006/MA0051 are off by policy
  (see .editorconfig); MA0074 is off for tests only. Never assume path separators or OS file locations in
  product code; build paths with Path.Combine. The differential tests' unix
  defaults (`/etc/ldap/schema` etc.) are the one deliberate exception — they are
  Linux-gated and env-overridable. Unix-looking strings in generator output
  (homeDirectory, loginShell) are LDAP attribute values, not filesystem paths.

## Working rules

- No new design documents. Decisions are recorded briefly here or expressed in
  code and tests. If a rule can't fail a build, question whether it's worth
  writing down.
- Work items are GitHub issues on this repo (`gh issue list`). One work item
  per session/agent; finish at green `dotnet test` before starting the next.
- The repo is public; v0.1.0 shipped 2026-07-17 to nuget.org (via NuGet
  Trusted Publishing — the release workflow's OIDC login; there is no NuGet
  API key) and to PSGallery (PSGALLERY_API_KEY secret, scoped to the module).
  Releases are cut by pushing a vX.Y.Z tag; both publishes then run
  automatically.
