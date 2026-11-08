# Contributing

Thanks for your interest. Issues and pull requests are welcome.

## Licensing of contributions

By submitting a contribution (code, documentation, tests or anything else) you agree that:

1. it is licensed under this repository's [MIT License](LICENSE), and Avlon Technologies and every recipient may use,
   modify and distribute it under those terms;
2. you wrote it, or have the right to submit it under that license; and
3. it contains no credentials, no production data, and no code you are not entitled to share, for example code owned by
   your employer.

Please sign off your commits (`git commit -s`) to confirm this, per the
[Developer Certificate of Origin](https://developercertificate.org/).

## Before opening a pull request

- `dotnet build SqlInvestigator.slnx` must pass with no warnings, because warnings are errors.
- `dotnet test tests/SqlInvestigator.Tests` must pass. The integration and acceptance suites need a disposable SQL
  Server. See the README.
- Never weaken the safety boundaries in [`CLAUDE.md`](CLAUDE.md) (stdout reserved for MCP, the fail-closed allowlist,
  local-only credential provisioning, no production data). A change that loosens them is very unlikely to be merged.
- Never use real or production data in tests or issues. Fixtures are synthetic.

## Architecture model

This project is developed against a [Continuum](continuum/) architecture model (`continuum/model/`), which governs the
design. Outside contributors don't need the Continuum tooling. The maintainers run the Continuum gate, and they'll
update the model if a contribution changes the architecture.

## Security issues

See [`SECURITY.md`](SECURITY.md). Please don't open public issues for vulnerabilities.
