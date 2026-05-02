# Security Policy

## Reporting a vulnerability

Please do not report security vulnerabilities through public GitHub issues, discussions, pull requests, or comments.

Report vulnerabilities by emailing:

security@nkdagility.com

If that address is not monitored, replace it before making this repository public.

Please include:

- A clear description of the issue
- Affected component, project, or package version, if known
- Reproduction steps, if safe to share
- Expected and actual behaviour
- Potential impact
- Any relevant logs with secrets and customer data removed

We will review reports and respond as soon as practical.

## Supported versions

Until the first stable release, only the `main` branch is actively reviewed for security fixes.

| Version | Supported |
|---|---|
| `main` | Yes |
| Pre-release branches | No, unless explicitly stated |
| Forks | No |

## Sensitive data

Do not include any of the following in GitHub issues, pull requests, discussions, examples, screenshots, logs, or attachments:

- Personal Access Tokens
- Azure client secrets
- SAS tokens
- connection strings
- storage account keys
- Entra application secrets
- private keys
- customer organisation URLs
- customer project names
- exported work item data
- attachments
- identity mapping files
- migration packages
- `.migration` folders
- package logs
- `idmap.db`
- checkpoint files

If sensitive data is accidentally disclosed, revoke or rotate the exposed credential immediately and notify the repository maintainers.

## Security scope

Security-sensitive areas include:

- Authentication and credential handling
- Azure DevOps and Team Foundation Server access
- Control Plane authentication and authorisation
- Agent lease handling
- Package storage access
- Azure Blob Storage access
- Local filesystem package handling
- Entitlement and billing integrations
- Logging and telemetry data classification
- Migration package contents
- Identity mapping and unresolved identity files

## Data handling expectations

The platform may process customer-identifiable data, including work item content, identity information, attachments, project names, organisation URLs, and diagnostic logs.

When reporting a security issue:

- Use synthetic data wherever possible
- Redact customer names, tenant names, project names, URLs, tokens, IDs, and secrets
- Do not attach migration packages
- Do not attach `.migration` folders
- Do not attach logs unless they have been reviewed and redacted

## Dependency vulnerabilities

For dependency vulnerabilities, include:

- Package name
- Affected version
- Fixed version, if known
- Whether the vulnerability affects runtime, build time, test code, or tooling
- Any known exploit path in this project

## Public disclosure

Please allow maintainers reasonable time to investigate and fix reported vulnerabilities before public disclosure.

Do not publish exploit details, reproduction repositories, or proof-of-concept code until the issue has been reviewed and a fix or mitigation is available.

## Contact

Security reports should be sent to:

contact@nkdagility.com