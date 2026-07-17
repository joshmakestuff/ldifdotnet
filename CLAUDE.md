# ldifdotnet

Pure managed .NET implementation of LDIF (RFC 2849). Deliberately NOT a P/Invoke
binding of OpenLDAP's C code — OpenLDAP compatibility is proven by tests, not
linkage. Primary consumer: an OpenLDAP .NET Aspire integration. Targets net8.0;
PowerShell 7+ only.

## Commands

- Build and test: `dotnet test`
- Refresh the OpenLDAP fixture corpus: `./tools/get-openldap-fixtures.ps1`
  (release tag is pinned inside the script)
- Differential tests against real OpenLDAP: skipped unless `LDIF_DIFFERENTIAL=1`
  and slapd tools are installed (the Linux CI job does this)

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

## Working rules

- No new design documents. Decisions are recorded briefly here or expressed in
  code and tests. If a rule can't fail a build, question whether it's worth
  writing down.
- Work items are GitHub issues on this repo (`gh issue list`). One work item
  per session/agent; finish at green `dotnet test` before starting the next.
- The repo is private for now; do not publish packages anywhere until it goes
  public deliberately.
