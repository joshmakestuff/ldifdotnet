# Third-Party Notices

This repository bundles third-party LDAP schema files under
`tests/fixtures/schemas/` so the parser and generator can be tested against
real-world schemas. They are fetched verbatim by `tools/get-schemas.ps1` and
verified against recorded SHA-256 checksums (so they are unmodified from their
pinned upstream sources).

**These files are used for testing only. They are NOT included in the published
NuGet packages** (`LdifDotNet`, `LdifDotNet.Schema`, `LdifDotNet.Generator`),
which contain only the compiled assemblies and their package README.

Each file retains its own upstream license, listed below. LdifDotNet's own code
is licensed separately (see `LICENSE`).

---

## OpenLDAP schemas — `tests/fixtures/schemas/openldap/*.schema`

- **Source:** the OpenLDAP Project, `servers/slapd/schema` at tag
  `OPENLDAP_REL_ENG_2_6_10` (<https://github.com/openldap/openldap>)
- **License:** OpenLDAP Public License v2.8 (SPDX: `OLDAP-2.8`) — full text in
  `tests/fixtures/schemas/openldap/LICENSE.OpenLDAP`
- **Modifications:** none (copied verbatim).

## eduPerson — `tests/fixtures/schemas/contrib/eduperson.schema`

- **Title/creator:** "eduPerson (202111)", by REFEDS (Research and Education
  FEDerations).
- **Source:** REFEDS/eduperson at commit
  `c672bc4be5e081cba95013b1ac1f7e18fd247ca1`, path `schema/openldap/eduperson.schema`
  (<https://github.com/REFEDS/eduperson>)
- **License:** Creative Commons Attribution-NonCommercial-ShareAlike 4.0
  International (SPDX: `CC-BY-NC-SA-4.0`) —
  <https://creativecommons.org/licenses/by-nc-sa/4.0/>
- **Modifications:** none (copied verbatim).
- **Note:** the **NonCommercial** term of CC BY-NC-SA-4.0 restricts commercial
  use, and **ShareAlike** requires derivatives to carry the same license. These
  terms apply to this file specifically; they do not affect LdifDotNet's own
  code or the other bundled files. Downstream users who need to use this schema
  commercially should obtain it under different terms from REFEDS.

## rfc2307bis — `tests/fixtures/schemas/contrib/rfc2307bis.schema`

- **Source:** jtyr/rfc2307bis at commit
  `4fb02fcfc5816e62716e34a9e27c506e2bedd9c8` (<https://github.com/jtyr/rfc2307bis>)
- **License:** MIT (SPDX: `MIT`), per the source repository's `LICENSE`.
- **Modifications:** none (copied verbatim).
- **Provenance note:** the file header records that its content was extracted
  from the IETF Internet-Draft `draft-howard-rfc2307bis-02` (which expired
  without becoming an RFC). The redistributing repository applies the MIT
  license; the relationship between that grant and the underlying IETF draft
  material is noted here for transparency.

## Sudo schema — `tests/fixtures/schemas/contrib/sudo.schema`

- **Source:** sudo-project/sudo at commit
  `2afb054bcc05b410a4cab8d94b39f3cec2f02c97`, path `docs/schema.OpenLDAP`
  (<https://github.com/sudo-project/sudo>)
- **License:** ISC-style permissive license (SPDX: `ISC`), Copyright (c)
  1994-1996, 1998-2026 Todd C. Miller — see the source repository's `LICENSE.md`.
- **Modifications:** none (copied verbatim).

## OpenSSH LPK schema — `tests/fixtures/schemas/contrib/openssh-lpk.schema`

- **Source:** AndriiGrytsenko/openssh-ldap-publickey at commit
  `afc095a0e9be1ca0a1c47ac2891f70cda2cdbfe2`, path
  `misc/openssh-lpk-openldap.schema`
  (<https://github.com/AndriiGrytsenko/openssh-ldap-publickey>)
- **License:** MIT (SPDX: `MIT`), per the source repository's `LICENSE`. The
  schema credits Eric AUGE and a proposal by Mark Ruijter.
- **Modifications:** none (copied verbatim).
