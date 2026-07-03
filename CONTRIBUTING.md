# Contributing to Tiro

Thank you for your interest in contributing to Tiro. Contributions are welcome,
but the licensing terms matter. This is where open-source projects either stay
clean or slowly become a copyright compost heap. We are choosing clean.

## License of Contributions

By submitting a contribution to Tiro, you agree that your contribution is
licensed under the GNU Affero General Public License v3.0 only
(`AGPL-3.0-only`).

You also grant Patrick Turner, as the project maintainer and copyright holder,
a perpetual, worldwide, non-exclusive, royalty-free license to use, reproduce,
modify, sublicense, relicense, distribute, and otherwise exploit your contribution
as part of Tiro under:

1. the AGPL-3.0-only license; and
2. separate commercial license terms offered by Patrick Turner.

This allows Tiro to remain publicly available under the AGPL while also allowing
separate commercial licensing for organizations that do not want to accept AGPL
source-sharing obligations.

## Ownership

You retain ownership of your own contribution. You are not assigning copyright by
contributing. You are granting the licensing rights described above.

## Contributor Promise

By contributing, you represent that:

- you wrote the contribution yourself, or you have the right to submit it;
- the contribution does not knowingly include code, text, data, or other material
  that you do not have permission to contribute;
- the contribution is not copied from a proprietary codebase or incompatible
  license;
- you are legally able to grant the rights described in this document.

## Inbound License

Unless explicitly agreed otherwise in writing, all inbound contributions are
accepted under the same license terms described above.

## How to Contribute

Before submitting a substantial change, open an issue or discussion describing:

- the problem being solved;
- the proposed design;
- any schema, storage, tool, or API impact;
- expected tests;
- known risks or limitations.

Small fixes, documentation improvements, and test additions may be submitted as
pull requests directly.

## Code Standards

Contributions should:

- keep Tiro modular;
- preserve deterministic/auditable memory behavior;
- avoid hidden network calls;
- avoid broad shell execution;
- avoid secret logging;
- include tests for new behavior;
- update documentation when behavior changes.

## Security Issues

Do not open public issues for vulnerabilities, secret exposure, prompt-injection
bypasses, or tool-boundary failures.

Report security concerns privately to:

Patrick Turner
pturner1976@gmail.com

## No Warranty

Contributions are accepted as-is. Tiro is distributed without warranty under the
terms of the AGPL. The machines may be innocent, but the warranty is not coming
to save anyone.
