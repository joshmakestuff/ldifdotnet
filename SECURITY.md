# Security Policy

## Reporting a vulnerability

Please do not open a public issue for security problems. Use GitHub's private
vulnerability reporting: **Security → Report a vulnerability** on this
repository. You should receive a response within a week.

## Supported versions

Until 1.0, only the latest released version receives fixes.

## Scope notes

The LDIF reader is designed to accept untrusted input: parse failures must
raise `LdifParseException` (never corrupt data or hang), and resource use must
stay proportional to input size. URL value references (`:<`) are never fetched
by the library. Deviations from any of that are security-relevant bugs.
