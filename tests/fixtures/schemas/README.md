# LDAP schema corpus

Fetched by `tools/get-schemas.ps1` — exact pins and SHA-256 checksums live there.

- `openldap/` — every `*.schema` shipped with OpenLDAP at the pinned release tag
  (OpenLDAP Public License, included).
- `contrib/` — well-known third-party schemas pinned to exact commits:
  - `eduperson.schema` — REFEDS/eduperson (canonical eduPerson source)
  - `rfc2307bis.schema` — jtyr/rfc2307bis (community copy; provenance traces to
    Gentoo distfiles / draft-howard-rfc2307bis)
  - `sudo.schema` — sudo-project/sudo `docs/schema.OpenLDAP`
  - `openssh-lpk.schema` — AndriiGrytsenko/openssh-ldap-publickey

License review of `contrib/` sources is required before this repo goes public
(tracked on issue #5).
