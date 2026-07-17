# LDAP schema corpus

Fetched by `tools/get-schemas.ps1` — exact pins and SHA-256 checksums live there.

- `openldap/` — every `*.schema` shipped with OpenLDAP at the pinned release tag
  (OpenLDAP Public License, included).
- `contrib/` — well-known third-party schemas pinned to exact commits:
  - `eduperson.schema` — REFEDS/eduperson (CC BY-NC-SA 4.0)
  - `rfc2307bis.schema` — jtyr/rfc2307bis (MIT; content from draft-howard-rfc2307bis)
  - `sudo.schema` — sudo-project/sudo `docs/schema.OpenLDAP` (ISC)
  - `openssh-lpk.schema` — AndriiGrytsenko/openssh-ldap-publickey (MIT)

These are used for testing only and are not shipped in the NuGet packages. Each
retains its upstream license; full attribution, sources, pinned commits, and
license terms are recorded in `THIRD-PARTY-NOTICES.md` at the repository root.
Note that `eduperson.schema` is CC BY-NC-SA 4.0 (NonCommercial + ShareAlike);
those terms apply to that file only. Do not hand-edit these files — they are
verbatim, SHA-256-pinned copies (see `tools/get-schemas.ps1`).
