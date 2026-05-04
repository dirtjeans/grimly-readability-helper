# Security Policy

## Reporting a vulnerability

If you find a security issue in Grimly, **please don't file a public GitHub issue.** Use GitHub's private vulnerability reporting instead:

1. Go to the [Security tab](../../security) of this repository.
2. Click **Report a vulnerability**.
3. Describe the issue with steps to reproduce.

I'll respond within a week. If the issue is confirmed, I'll work on a fix and coordinate disclosure with you before the patch is published.

## Scope

In scope:
- Code execution, privilege escalation, or sandbox escapes triggered by Grimly itself.
- Anything that causes Grimly to exfiltrate user data to a non-localhost destination (this would contradict the project's privacy claim).
- Issues in the build/release pipeline (`.github/workflows/`) that could let an attacker inject malicious code into a release artifact.

Out of scope:
- Issues in [Microsoft Foundry Local](https://github.com/microsoft/foundry) — please report those upstream.
- Issues in third-party dependencies — please report them to the dependency's maintainers; I'll bump the version once a fix lands.
- Theoretical issues with no demonstrable exploit path on a typical user's machine.

## Supported versions

Only the latest released version of Grimly receives security updates. There are no LTS branches.
