# Changelog

All notable changes to this project will be documented in this file.
Format: [Keep a Changelog](https://keepachangelog.com/) / [SemVer](https://semver.org/).

## [Unreleased]

## [0.5.1] - 2026-08-08

### Fixed
- The README's GitHub Action example is now a complete, copy-pastable workflow including the
  `permissions:` block. The previous minimal example omitted `pull-requests: write`, so anyone
  copying it would have the PR comment step 403 — and because that step is deliberately
  `continue-on-error` (a forked PR always gets a read-only token, which is expected rather than
  a misconfiguration), the failure is quiet: the gate still works, the comment just never appears.
  Documentation only; no behaviour change.
- The `comment` action input description now states the permission requirement.
- The Chinese README's CI command list was missing `--sarif` and `--md`.

## [0.5.0] - 2026-08-08

### Added
- `--md <path>`: a Markdown audit report, and the GitHub Action now posts it as a **PR comment**
  that updates in place instead of piling up (new `comment` input, on by default). Until now the
  Action only produced inline `::error` annotations, which mark the offending line but give the
  reviewer no overview — no "how many failed in total", no "did palette detection degrade", no
  "how many were exempted or suppressed". The comment leads with the verdict and, crucially, ends
  with everything that was **not** evaluated: an audit tool's real risk is not a missed finding,
  it is falsely reporting health. The sibling project Parity has posted PR comments since 0.2.0;
  this closes that gap.
- Chinese README caught up with the English one (117 → 165 lines). It was missing the sample
  output, the GitHub Action section, and the JSON/Markdown output reference — the other three
  sibling repos keep both languages within ~15% of each other, only this one had halved.

## [0.4.0] - 2026-08-07

### Added
- `working-directory` action input, matching the sibling Parity action. `root` is resolved
  relative to it, and both the inline `::error` annotations and the SARIF paths are prefixed
  accordingly (with `./` and duplicate slashes normalized away, or GitHub fails to match the file).
- `.editorconfig` (single source of truth for formatting) and `.github/dependabot.yml`
  (nuget + github-actions).
- CI gained a `dotnet format` check, a three-OS build/test matrix, and a
  pack → `dotnet tool install` → run smoke test. The matrix matters here because the audience
  is WPF developers, whose machines and CI are overwhelmingly Windows — "Linux also works"
  is a selling point, not a reason to skip the primary platform.

### Changed
- **All eight `action.yml` input descriptions are now English.** The action's own `description`
  was already English, so the Marketplace listing was half-translated: an English summary
  above a list of Chinese parameter docs.

### Fixed
- **Stale version references in the docs.** The README pinning example said `--version 0.2.0`
  and the zh-Hant one said `0.1.0` (two different stale values), while the GitHub Action example
  still pinned `@v0.1.0` — three versions behind. All now say 0.4.0.
- **The parsing-rule count disagreed with itself across four places.** README (en) said thirteen,
  README (zh-Hant) said thirteen in one paragraph and twelve in another, ROADMAP said eleven,
  and Parity's ROADMAP cited twelve. The code has a rule 13; everything now says thirteen.

## [0.3.0] - 2026-07-31

### Added
- `--sarif <path>`: SARIF 2.1.0 output for GitHub code scanning (fail/warn only —
  ok/decorative would be noise in the Security tab; degradation counters remain in
  `--json`'s summary). Action input `sarif: true` uploads it (needs
  `security-events: write`).

## [0.2.0] - 2026-07-31

### Added
- `xamlcontrast.config.json` is now actually loaded (schema: docs/config-schema.md):
  forced palette modes (`pair`/`csharp`/`single`/`none`), threshold and classification
  overrides (non-default thresholds are announced in output and JSON), `failOn`,
  `strictPalette`, `ignore.requireReason`. Invalid config exits 2 with a field-level
  message — a silently ignored config would mean you think you changed the audit
  standard when you didn't. Precedence: CLI flags > config > defaults.
- NuGet package icon (split-theme "A" mark).
- Rule 13: trigger setters with `TargetName` aimed at the **template root** are resolved
  as host visuals (and, per WPF precedence, beat the host's local value fed in via
  `TemplateBinding`). Eliminates all six documented false alarms of this class across
  two validation projects; inner-part `TargetName` remains explicitly out of scope.
  First C#-first rule after the prototype freeze — verification switched to
  C#-snapshot regression (`baselines/*.json`, `verify-baselines.ps1 -Update`).

## [0.1.0] - 2026-07-31

First public release.

### Added
- Whole-project static WCAG 2.x contrast audit for XAML source (eleven parsing rules;
  see README "Why the hard part isn't the WCAG formula")
- Zero-config palette detection: theme pair / C# source of truth / single theme /
  hardcoded-only fallback (loud)
- Dual-theme grading with independent grade × symmetry dimensions
- Disabled-state exemption (WCAG 1.4.3), counted and reported
- `--json` two-layer report (`schemaVersion` 1): findings + summary with every
  degradation counter (`paletteSource`, `unresolved`, `skipped`, `suppressed`,
  `parseErrors`, `disabledExempt`)
- Exit-code contract: fail → 1, zero pairs → 1 (an empty scan is not a pass),
  usage error → 2; `--fail-on warn`, `--strict-palette`
- Baseline ratchet (`--write-baseline` / `--baseline`): freeze known debt, only block
  new or worsened failures; keys are line-number-free and carry the worst ratio
- `<!-- xamlcontrast-ignore: reason -->` suppression comments (reason required,
  suppressed pairs counted)
- Validated against four shipped WPF products; numbers reproduced by two independent
  implementations (PowerShell prototype = spec, .NET port)
- Findings report root-relative, forward-slash file paths (duplicate file names no
  longer collide; baselines are portable across OSes)
- `--baseline` / `--write-baseline` default to `xamlcontrast-baseline.json`;
  a malformed baseline exits 2 with a helpful message instead of a stack trace
- `--version`; JSON carries `schemaVersion`
