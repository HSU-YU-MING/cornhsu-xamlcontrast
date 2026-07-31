# XamlContrast

**Static WCAG contrast audit for XAML source. No app launch, whole-project scan, CI-ready exit codes.**

[繁體中文](README.zh-Hant.md)

Every existing desktop contrast checker is runtime + manual + one-element-at-a-time
(Accessibility Insights hovers one element; CCA picks two pixels). XamlContrast flips that:
it parses your XAML source, figures out **what color every piece of text actually sits on**,
computes WCAG 2.x contrast for both your dark and light themes, and fails your CI when
text falls below AA.

```bash
dotnet tool install -g Cornhsu.XamlContrast
xamlcontrast path/to/your/wpf/project
```

> 0.x: pin exact versions (`--version 0.1.0`). Interfaces freeze at 1.0.

## What it looks like

Run against [`samples/demo`](samples/demo) (intentionally broken):

```
palette: auto-detected theme pair: Themes\DarkTheme.xaml + Themes\LightTheme.xaml (6 keys)
files 1 | text-on-background pairs 8
exempted 1 disabled-state pair(s) (IsEnabled=False; WCAG 1.4.3 ...)
suppressed 1 pair(s) via xamlcontrast-ignore comments

  ok          3
  fail        3
  decorative  1

===== fail (3) =====
MainWindow.xaml:30  TextBlock                fg=White                     bg={Surface}  dark=16.67:1 light=   1:1  [light-fails] need 4.5 12px
MainWindow.xaml:26  TextBlock                fg={DynamicResource DimText} bg={Bg}       dark= 2.11:1 light= 1.6:1  [both-low]    need 4.5 12px
MainWindow.xaml:6   Style[HoverBtn]/trigger  fg=#9A9A9A                   bg={Surface}  dark= 5.92:1 light=2.81:1  [light-fails] need 4.5

exit 1: 3 pair(s) below threshold x 2/3 (0 warn)
```

## Why run it while developing

- **Seconds instead of eyeballing.** Without it, checking contrast means launching the
  app, flipping both themes, and judging every screen by eye — on your monitor, at your
  brightness. With it: one command before commit, whole project, every text-on-background pair.
- **It guards the theme you're not looking at.** You tune a color in dark mode and never
  notice light mode just dropped to 1.1:1. Every pair is computed for both themes;
  the symmetry column tells you which side broke.
- **It checks states manual testing can't reach.** Hover, pressed, trigger states,
  what a `BasedOn` chain actually resolves to — all 23 real issues found in one
  validation project were named-Style trigger states the authors never knew existed.
- **It pushes you toward a healthy palette.** Hardcoded colors don't follow themes and
  light up in the report — one validation project went from 572 hardcoded colors to 0.
  The baseline's recorded ratios also catch palette drift (same key, darker value).
- **It turns taste arguments into numbers.** "Is this readable?" is endless;
  "2.54:1, needs 4.5" is a decision. In one real fix the intuitive direction
  (darken the scrim) was mathematically wrong — the numbers settled it.
- **It moves the cost to the PR.** The expensive path is ship → user report / compliance
  audit → sweep the whole app. The cheap path is a red check with inline annotations
  on the lines you just touched — and `--write-baseline` lets legacy projects go green
  on day one, blocking only new debt.

In short: it makes contrast a first-class check, same rank as unit tests —
run on every change, red when broken, pointing at the exact line.

Two independent dimensions per finding:

- **grade** (absolute contrast): `fail` < threshold×2/3, `warn` in between, `ok`, `decorative`
- **symmetry** (across themes): `both-low` = the palette itself is too weak;
  `dark-fails` / `light-fails` = the design intent didn't survive the other theme

Symmetry is **never** used to hide a finding. "Both themes are equally bad" is not evidence
of intent — it's just bad twice. Everything below AA gets reported; a human decides.

## Why the hard part isn't the WCAG formula

The formula is 20 lines. The value is in resolving **what color the text actually sits on** —
twelve parsing rules, each one discovered by auditing real shipped products:

transparent passthrough · alpha compositing · opacity accumulation down the tree ·
ControlTemplate subtrees · Style setter pairing · trigger states · dead-setter filtering ·
named/inline Style resolution with BasedOn chains · per-state trigger merging ·
disabled-state exemption (WCAG 1.4.3) · translucent palette keys ·
template-root backgrounds

Any implementation that just copies the formula misses all of them.

## Zero-config palette detection

XamlContrast scans your project and figures out where the palette lives:

1. **Theme pair** — `DarkTheme.xaml` + `LightTheme.xaml` with overlapping keys
2. **C# source of truth** — `("Key", "#dark", "#light")` tuple arrays
   (beats a single-theme XAML copy: a source that provides both themes wins)
3. **Single theme** — one resource dictionary, both columns get the same value
4. **Nothing found** — falls back to hardcoded colors only, and **says so loudly**

Wrong guess? Override with `xamlcontrast.config.json` ([schema](docs/config-schema.md)).

## CI

```bash
xamlcontrast src/MyApp --json report.json          # exit 1 on any fail
xamlcontrast src/MyApp --fail-on warn              # strict: everything must meet AA
```

Exit codes: `1` on failure, `1` when **zero pairs resolved** (an empty scan is not a pass),
`2` on usage errors, `0` otherwise.

### Adopting on an existing project (baseline ratchet)

A gate that's red on day one gets turned off. Freeze the current debt once, then only
block new or worsened failures — debt may only shrink:

```bash
xamlcontrast src/MyApp --write-baseline xamlcontrast-baseline.json   # once; commit the file
xamlcontrast src/MyApp --baseline xamlcontrast-baseline.json         # in CI
```

Baseline keys contain no line numbers (lines drift) but do record the worst ratio
(so palette drift — same key, darker value — still gets caught).

### Suppressing intentional low contrast

```xml
<!-- xamlcontrast-ignore: watermark, intentionally faint -->
<TextBlock Opacity="0.4" Text="DRAFT" ... />
```

The reason is **required** — an ignore without one is invalid and warned about.
Suppressed pairs are counted in `summary.suppressed`; nothing disappears silently.

### GitHub Action

```yaml
- uses: HSU-YU-MING/cornhsu-xamlcontrast@v0.1.0   # 0.x: pin exact version
  with:
    root: src/MyApp
```

Failing pairs show up as inline annotations on the PR.

## JSON output

`--json report.json` writes a two-layer report: `findings` (one entry per pair) plus a
`summary` block carrying every degradation counter — `paletteSource`, `unresolved`,
`skipped`, `suppressed`, `parseErrors`, `disabledExempt`. If the tool couldn't see
something, that fact is machine-readable. Consumers should check `schemaVersion`.

## Validated against real products

Not "runs on a demo" — this tool found and drove fixes for **250+ real contrast issues
across three shipped WPF apps** (before/after audited by the tool itself):

| project | before | after |
|---|---|---|
| CelFlow | 39 fail / 57 warn | **0** (21 disabled-state exempted) |
| Kindling | 41 fail / 14 warn | **0 real** (3 remaining, all verified false alarms of known classes) |
| QuillNest | 13 fail / 31 warn | **0 real** (7 remaining, same) |

How that trust was earned — the highlights, with the full story in the
[development retrospective](docs/開發歷程回顧.md) (zh-Hant):

- **Two independent implementations cross-verified.** The PowerShell prototype (the spec)
  and this .NET port agree on every number across all four validation projects —
  down to each failing pair's `file:line` and the exit code. Re-run it yourself:
  `scripts/verify-baselines.ps1`.
- **Every parsing rule came from a real false alarm or a real miss** — none were designed
  at a whiteboard. The 12th (template-root backgrounds) was found while verifying the
  fixes the tool itself had driven.
- **The tool produced eight "healthy-looking but wrong" reports during development.
  Each one is now a regression test.** The project's core rule, written in blood:
  **an audit tool's worst failure mode is not missing issues — it's false confidence.**
  Degradations must shout; nothing gets silently excused — exemptions, exclusions,
  suppressions, and parse failures are all counted and reported.
- **Remaining fails are named, not hidden**: every residual finding above maps to a
  documented false-alarm class (see Known limitations) and is absorbed by the
  baseline ratchet on adoption.

## Known limitations

Honest list — see the [blind-spot table](XamlContrast專案規畫書.md) for details:

- `TargetName` setters inside template triggers (background swapped on a named part) — false alarms
- Cross-element correlated triggers (same condition flips fg on one element, bg on another) — false alarms
- Sibling-element backgrounds; text over images; implicit styles
- `Binding` / `TemplateBinding` colors are reported as *unresolved*, never guessed —
  guessing would trade honest uncertainty for false confidence

## See also

[**Parity**](https://github.com/HSU-YU-MING/cornhsu-parity) — sibling project, same
author, same philosophy (numeric checks that gate CI). Parity answers *"does the
implementation match the design?"* (Figma vs rendered values); XamlContrast answers
*"can people actually read it?"* One guards fidelity, the other guards legibility —
they meet in the same PR checks list.

## License

MIT © [許彧銘 Hsu Yu-Ming](https://cornhsu.com/)
