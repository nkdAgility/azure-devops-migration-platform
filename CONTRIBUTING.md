# Contributing

Thank you for your interest in contributing to DevOps Migration Platform.

This project is licensed under AGPL-3.0-only. Contributions are accepted only when the required Contributor Licence Agreement (CLA) checks pass in GitHub.

## Contributor Licence Agreement

By opening a pull request, you agree that your contribution may only be accepted after the configured CLA process has been completed.

Pull requests that do not pass the CLA check will not be merged.

## Contribution process

1. Open an issue before starting substantial work.
2. Keep pull requests focused and small.
3. Use RED → GREEN → REFACTOR for every addition, bug fix, and behaviour change: start from a failing behavioural test, make it pass with the smallest production change, then refactor while staying green.
4. Do not include secrets, customer data, migration packages, logs, generated artefacts, or `.migration` folders.
5. Follow the existing architecture and module boundaries.
6. Ensure all source files include the appropriate SPDX header.

## Licence headers

AGPL-licensed source files must include:

SPDX-License-Identifier: AGPL-3.0-only

Files or components distributed under separate NKD Agility terms must use the appropriate `LicenseRef-*` SPDX identifier and must not be added to the AGPL core without explicit approval.

## Architecture boundary

The AGPL core defines platform contracts and default implementations.

Optional add-in assemblies or separately distributed components may implement those contracts under separate licence terms.

The AGPL core must remain independently buildable and usable without separately licensed add-ins.
