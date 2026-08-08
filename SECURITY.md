# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in AltTabExcluder, please report it
responsibly:

1. **Do not open a public GitHub issue.**
2. Use [GitHub's private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability):
   go to the **Security** tab of this repository and click **Report a vulnerability**.
3. Include a description of the issue, steps to reproduce, and any relevant
   logs or screenshots.

You will receive a response within 72 hours. If the vulnerability is confirmed,
a fix will be prepared and a security advisory published once the fix is
released.

## Scope

AltTabExcluder is a local Windows tray utility. It does not expose any network
services or handle remote input. Security-relevant areas include:

- **Win32 interop** — style manipulation on window handles (local only).
- **File-based persistence** — settings, rules, and logs written to
  `%APPDATA%\AltTabExcluder`. Files are written atomically (temp + move) and
  loaded fault-tolerantly.
- **No telemetry or network communication** — the app does not make any
  outbound network requests.

## Supported Versions

Only the latest release receives security fixes.

| Version | Supported |
|---------|-----------|
| Latest  | Yes       |
| Older   | No        |
