# Changelog

All notable changes to this project will be documented in this file.
Format: [Keep a Changelog](https://keepachangelog.com/) / [SemVer](https://semver.org/).

## [Unreleased]

## [0.1.0] - TBD

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
