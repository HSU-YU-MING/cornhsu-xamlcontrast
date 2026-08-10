# Changelog

All notable changes to this project will be documented in this file.
Format: [Keep a Changelog](https://keepachangelog.com/) / [SemVer](https://semver.org/).

## [Unreleased]

### Changed
- **`schemaVersion` is now 2.** `ok` and `decorative` findings no longer carry a `symmetry`
  field in `--json` output; it is omitted rather than set to a placeholder. Consumers keying
  off `symmetry` should treat absence as "not applicable" — see Fixed below for why.

### Fixed
- **Dotted palette keys (`Panel.Background`) were invisible to palette detection.** The
  definition-side regexes matched keys with `\w+`, which excludes `.`, while the usage-side
  resolver accepts `[\w.]+` — so the tool could see a key being *referenced* but could never
  find where it was *defined*. Dotted keys are the dominant naming convention in the WPF
  ecosystem. Measured on ScreenToGif: its dark theme declares 93 brushes and the detector
  recognised 0 of them, which cascaded into picking an unrelated 3-key DataGrid style file as
  "the palette" and resolving 0 auditable pairs across 120 XAML files.
- **`#FFRRGGBB` was treated as translucent.** An alpha of `FF` is 255 — fully opaque — but
  every 8-digit literal was classified as `Alpha`, and an `Alpha` foreground goes to `skipped`.
  This is the default output format of Blend and the Visual Studio designer, so any project
  whose colours came from a designer rather than by hand had its foregrounds skipped wholesale.
  Measured on ScreenToGif: 79 of its 97 palette values are `#FF`-prefixed. Genuinely
  translucent values (`alpha < FF`) still composite over the resolved background as before.

  Together these two took ScreenToGif from **0 auditable pairs to 406** (53 fail, 24 warn) and
  MahApps.Metro from 17 to 67 — MahApps having previously exited **0** while resolving 4% of
  its pairs. Neither defect could surface on the four validation projects: all four are
  same-author and hand-write short-form `#RRGGBB` with undotted keys (QuillNest 32/32 six-digit,
  CelFlow 47/51), so the designer-generated convention was never once exercised. The four
  snapshots are byte-identical after the fix.
- **`--strict-palette` was silently inert in baseline mode.** The check sat at the very end of
  the exit-code decision, but the `--baseline` and `--write-baseline` branches return before
  reaching it — and `--baseline` is the adoption path the README recommends for existing
  projects, so the guard was dead in its most common pairing. The failure chain is worse than
  a missed flag: move the theme files, palette detection degrades, every palette-keyed pair
  becomes `unresolved`, those pairs vanish from `findings`, and the ratchet reads their absence
  as debt repaid. The run prints `known debt 0, paid off 2` and exits 0 — breaking your theme
  file looks exactly like fixing every contrast problem in the project. The check now sits
  immediately after the zero-pairs guard, before any mode branch, so both "this result is not
  trustworthy" rails apply everywhere. `--write-baseline` now also refuses to freeze a baseline
  computed from a degraded palette.
- **Malformed palette colours are no longer accepted and silently misread.** The palette
  regexes match `#[0-9A-Fa-f]{6,8}`, which also accepts a seven-digit typo like `#FF0000A`;
  `Wcag.Luminance` then reads the first six digits and discards the rest, producing a
  confident, wrong contrast ratio with no warning anywhere. Only 6- and 8-digit values are
  meaningful, so anything else is now kept out of the palette — uses of that key resolve to
  `UnknownKey` and surface through the existing `unresolved` counter instead. Applies to
  XAML `<Color>`/`<SolidColorBrush>` definitions, the C# tuple source, and user-supplied
  `palette.csharpPattern`.
- **Report write failures now exit 2 with a message instead of a stack trace.** `--json
  out/report.json` with no `out/` directory threw an unhandled `DirectoryNotFoundException`,
  printing a .NET stack trace and exiting 127 — a value outside the documented 0/1/2 contract.
  A malformed baseline has produced a friendly message since 0.4.0; the output side now
  matches. Directories are still not created implicitly: quietly inventing a directory for a
  mistyped path is harder to debug than an error.
- **Symmetry is no longer classified for passing pairs.** Every finding was assigned a
  symmetry, so a pristine 21:1 pair (gap < 1.5) came out as `both-low` — a label whose
  documented meaning is "the palette itself is too weak, switching theme won't save it".
  The dimension only answers "would switching theme rescue this?", which is meaningless
  where there is nothing to rescue. Console and Markdown reports print symmetry only for
  `fail`/`warn`, so this was invisible to humans and leaked exclusively into `--json` —
  every downstream consumer read a confident, wrong classification on the majority of rows.
- **`paid off` in the baseline summary now counts occurrences, matching `known debt`.**
  `KnownDebt` sums per-key occurrence counts while `PaidDebt` counted distinct keys, so
  clearing one key that covered 5 occurrences reported "known debt 0, paid off 1". The two
  numbers are printed side by side on one line, in what reads as the same unit; during
  adoption — exactly when visible progress matters most — the ratchet under-reported it.
- **SemiBold no longer qualifies for the WCAG large-text exemption.** The bold check was a
  substring match (`Bold` matches `SemiBold`), so 14pt+ SemiBold(600) text was graded against
  3:1 instead of 4.5:1 — text with a ratio between 3.0 and 4.5 passed when it should have
  failed. WCAG's "bold" means weight ≥ 700; the check is now an anchored whole-word match
  (Bold/ExtraBold/UltraBold/Black/ExtraBlack/UltraBlack/Heavy, or numeric 700–999). This can
  surface new failures in existing projects — they were always failures, just unreported.
- **`MultiTrigger` / `MultiDataTrigger` states are now audited.** They were on the "not part
  of the visual tree" skip list but absent from every trigger-collection site, so combined-
  condition states (hover + selected and the like) were silently unchecked — with no counter
  hinting at the gap. Conditions are AND-ed: a state whose conditions include
  `IsEnabled=False` counts as disabled (WCAG 1.4.3 exemption), same as a plain trigger.
- **Unresolvable foregrounds now count as `unresolved`, not `skipped`.** A foreground bound
  at runtime (`{Binding}`), a gradient, or a key missing from the palette went into the
  `skipped` bucket — whose label reads "translucent, invisible", i.e. *legitimately exempt* —
  hiding half the tool's blind spot inside a bucket that claims there is nothing to see.
  The background side already classified these as `unresolved`; both sides now share one rule:
  can't-resolve → `unresolved`, translucent/invisible → `skipped`.
- **Style pairs with an unresolvable side are now counted.** They were dropped with a bare
  `continue` — absent from findings *and* from every degradation counter, which violates the
  project's own "silent degradation is lying" rule. Report labels updated to match the
  sharpened bucket meanings.

  The frozen PowerShell prototype carries all four defects identically — the .NET port
  inherited them from it — a concrete reminder that two implementations agreeing proves
  consistency, not correctness. It stays frozen regardless (governance decision, M5): it is a
  historical spec, not a verification source, and `prototype/baseline-*.txt` goes on recording
  v0.1.0 behaviour. Verification is C# snapshot regression via `scripts/verify-baselines.ps1`.

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
