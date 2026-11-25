# Code Signing Policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

---

## 1. Scope & Build Provenance

- **Signed Artifacts:** Official Windows single-file installer (`MyanmarSpellChecker-Setup.exe`) and accompanying Microsoft Office COM add-in binaries (`MyanmarSpellChecker.Core.dll`, `MyanmarSpellChecker.WordAddIn.dll`, `MyanmarSpellChecker.ExcelAddIn.dll`, `MyanmarSpellChecker.PowerPointAddIn.dll`) published on [GitHub Releases](https://github.com/Hashyz/MyanmarSpellChecker/releases).
- **Automated Reproducible CI Origin:** All release binaries submitted for code signing are compiled exclusively from public open-source code (`GPL-3.0-or-later`) inside this GitHub repository on clean GitHub-hosted Windows runners via [`.github/workflows/release.yml`](./.github/workflows/release.yml) and [`installer/Build-EnterpriseRelease.ps1`](./installer/Build-EnterpriseRelease.ps1). No locally compiled developer binaries are uploaded to SignPath.
- **License:** 100% Free and Open Source under the [GNU General Public License v3.0 (GPL-3.0-or-later)](./LICENSE) with Section 7(b)/(c) origin attribution ([`NOTICE`](./NOTICE)). There is no commercial dual-licensing and no proprietary closed-source component.

---

## 2. Team Roles & Repository Governance

All repository maintainers with commit or release authority enforce Multi-Factor Authentication (2FA/MFA) on both GitHub and SignPath:

- **Author, Committer & Reviewer:** [Htet Yar Zar (`@Hashyz`)](https://github.com/Hashyz) — Reviews all source code changes, pull requests, lexicon updates, and GitHub Actions workflow definitions prior to merging into `main`.
- **Release Approver:** [Htet Yar Zar (`@Hashyz`)](https://github.com/Hashyz) — Verifies build provenance, automated test/benchmark gates, and explicitly authorizes SignPath Foundation release signing requests.
- **Linguistic & QA Contributors (Non-Approvers):** Hein Htet Zaw and Thu Reain Khant (credited in [`NOTICE`](./NOTICE) for linguistic testing and corpus feedback; they do not hold release-signing approval privileges).

---

## 3. Privacy Policy & Network Disclosure

**This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.**

Specifically:
1. **100% Offline Proofing, Dictionary & Toolkit:** All spell checking, grammar analysis, contextual meaning checks, bilingual dictionary/encyclopedia lookups, and document toolkit operations execute 100% locally on the user's Windows PC with zero background telemetry, zero analytics, zero cloud APIs, and zero document text transmission.
2. **User-Initiated Update Check Only:** Network communication occurs **only** when the user explicitly clicks **`Check Updates`** (`↻ Check Online`) inside the Office Ribbon / About dialog (or explicitly checks the optional weekly update-check setting, which is **disabled by default**). That request queries only the public GitHub Releases API (`https://api.github.com/repos/Hashyz/MyanmarSpellChecker/releases/latest`) over TLS 1.2+ to check version metadata and verify `SHA-256` / `RSA-2048` update manifests.
