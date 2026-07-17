# Contributing

## Build and test

```
dotnet test
```

That is the whole loop: warnings are errors, style and analyzers are build
assertions, and the fixture corpora are the behavioral spec. If `dotnet test`
is green on your change, CI probably is too (it runs the same suite on Linux,
Windows, and macOS, plus differential tests against a real OpenLDAP).

## Ground rules

- Work items are GitHub issues; one logical change per pull request.
- `tests/LdifDotNet.Tests/PublicApi*.approved.txt` is the API contract. If your
  change alters the public surface, re-approve deliberately with
  `UPDATE_PUBLIC_API=1 dotnet test` and include the diff in your PR.
- Never hand-edit files under `tests/fixtures/` — they are vendored
  (RFC examples and pinned OpenLDAP releases).
- Reader stays tolerant of input; writer stays strictly RFC-conformant.
- Behavior fixes come with a regression test.

See `CLAUDE.md` for the full list of enforced invariants.
