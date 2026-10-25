# <img src="https://raw.githubusercontent.com/HSU-YU-MING/cornhsu-xamlcontrast/main/icon.png" width="28" alt=""/> XamlContrast

**Static WCAG contrast audit for XAML source. No app launch, whole-project scan, CI-ready exit codes.**

[![NuGet](https://img.shields.io/nuget/v/Cornhsu.XamlContrast)](https://www.nuget.org/packages/Cornhsu.XamlContrast)
[![CI](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/actions/workflows/ci.yml/badge.svg)](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/blob/main/LICENSE)

[繁體中文](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/blob/main/README.zh-Hant.md)

Every existing desktop contrast checker is runtime + manual + one-element-at-a-time
(Accessibility Insights hovers one element; CCA picks two pixels). XamlContrast flips that:
it parses your XAML source, figures out **what color every piece of text actually sits on**,
computes WCAG 2.x contrast for both your dark and light themes, and fails your CI when
text falls below AA.

```bash
dotnet tool install -g Cornhsu.XamlContrast
xamlcontrast path/to/your/wpf/project
```

> Requires the **.NET 10 runtime** (the analysis itself is pure XML — runs on Linux CI too).
> **1.x freezes the syntax, not the findings.** The CLI flags, exit codes, JSON report, Action inputs,
> config file, baseline file format, SARIF rule, suppression comment and audit scope change only by
> addition until 2.0 ([the full list](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/blob/main/CHANGELOG.md)); the experimental `--push` mode is not covered yet
> ([below](#push-to-a-parity-dashboard-experimental)). What the audit *finds* can improve in a minor release —
> counts may move and a build may turn red; each such release says so in its CHANGELOG *Upgrading*
> note. If a release changes how baseline keys are spelled, it bumps `keyVersion` and an older baseline
> is refused with a note to regenerate it. Coming from 0.x? The [1.0.0 notes](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/blob/main/CHANGELOG.md) list what you
> have to change.

> **Anonymous usage statistics are on by default** (from 1.1): after each audit, one request with counts
> and ratio ranges only — no file names, paths, element names or colours — supports a master's thesis.
> [What is sent, and how to turn it off](#anonymous-usage-statistics) (`DO_NOT_TRACK=1` is enough).

## What it looks like

Run against [`samples/demo`](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/tree/main/samples/demo) (intentionally broken):

```
palette: merged colour dictionaries: 2 file(s), 6 keys (dark + light)
files 1 | text-on-background pairs 8 | coverage 100.0% of pairs seen
unresolved (colour bound at runtime / gradient / key not in palette): 0 | skipped (invisible: transparent or opacity 0): 0
exempted 1 disabled-state pair(s) (IsEnabled=False; WCAG 1.4.3 does not require contrast for disabled controls)
suppressed 1 pair(s) via xamlcontrast-ignore comments

  ok          3
  fail        3
  decorative  1

  below AA: 3, by cause:
    light-fails   2
    both-low      1

  both-low = the palette itself is too weak, switching theme won't save it;
  dark-fails / light-fails = the design intent didn't survive the other theme.

===== fail (3) =====
MainWindow.xaml:30  TextBlock                fg=White                      bg={Surface}  dark= 16.67:1 light=     1:1  [light-fails] need 4.5 12px
MainWindow.xaml:26  TextBlock                fg={DynamicResource DimText}  bg={Bg}       dark=  2.11:1 light=   1.6:1  [both-low] need 4.5 12px
MainWindow.xaml:6   Style[HoverBtn]/trigger  fg=#9A9A9A                    bg={Surface}  dark=  5.92:1 light=  2.81:1  [light-fails] need 4.5

exit 1: 3 pair(s) below threshold x 0.67 (0 warn)
```

(Column widths trimmed to fit the page; everything else is verbatim.)

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

- **grade** (absolute contrast): `fail` < threshold×0.67 (≈2/3), `warn` in between, `ok`, `decorative`.
  By default both `fail` and `warn` fail the run — anything below AA; `--fail-on fail` lets `warn` through
- **symmetry** (across themes): `both-low` = the palette itself is too weak;
  `dark-fails` / `light-fails` = the design intent didn't survive the other theme

Symmetry only answers "would switching theme rescue this?", so it is carried **only by
`fail` and `warn` findings** — the JSON's `symmetry` is `null` on `ok` and `decorative`
ones. A passing pair has no problem to rescue, and labelling a
21:1 pair `both-low` — "the palette is too weak" — would be nonsense.

Symmetry is **never** used to hide a finding. "Both themes are equally bad" is not evidence
of intent — it's just bad twice. Everything below AA gets reported; a human decides.

## Why the hard part isn't the WCAG formula

The formula is 20 lines. The value is in resolving **what color the text actually sits on** —
seventeen parsing rules, every one of them discovered by auditing real shipped products:

transparent passthrough · alpha compositing · opacity accumulation down the tree ·
ControlTemplate subtrees · Style setter pairing · trigger states · dead-setter filtering ·
named/inline Style resolution with BasedOn chains · per-state trigger merging ·
disabled-state exemption (WCAG 1.4.3) · translucent palette keys ·
template-root backgrounds · root-targeted trigger setters ·
`MultiTrigger` combined-condition states · `#FFRRGGBB` is opaque, not translucent ·
implicit-style background on the document root ·
sibling backdrops in overlapping containers

The first thirteen came from four shipped WPF apps. The last four came from scanning eight
public WPF projects (1909 XAML files) in August 2026 — and two of them cost **nothing** on the
original four, because all four are same-author and share a house style. A blind spot's severity
can't be measured on a sample that never triggers it.

Any implementation that just copies the formula misses all of them.

## Zero-config palette detection

XamlContrast scans your project and figures out where the palette lives. The paths are
tried in this order, and the one that ran is always named on the first line of the report:

1. **Merged dictionaries, both themes** — every XAML file that defines keyed `Color` /
   `SolidColorBrush` resources is read into one palette, and each key ends up with a dark *and*
   a light value. Tried first because it beats picking a single file: real projects split a
   palette across several dictionaries. `Application.Resources` counts as a dictionary; a window's
   local brushes (`Window.Resources`, `UserControl.Resources`) are visible only inside that window,
   as in WPF's resource lookup: there they override the palette (a local brush referencing a `Color`
   is resolved per theme), and a key only some window defines is `unknown-palette-key` everywhere else.
   Dictionary files don't count as screens in `summary.files`, but their styles (and the templates of
   their data and control templates outside styles) are still audited. Files are grouped into dark and light by *dark* / *light* anywhere in their path
   (`darktheme.xaml`, `Dark2.xaml` and `Themes/Dark/Colors/Palette.xaml` all count); if no name tells
   the themes apart (`Ocean.xaml` / `Nord.xaml`) and three or more keys have different values in
   different dictionaries, detection is reported as degraded rather than grading one theme silently.
   A dictionary **belongs to one theme only** when its file name or last folder contains *dark* or
   *light* as a whole word (`Dark.xaml`, `DarkTheme.xaml`, `Colors.Dark.xaml` — not `Highlight.xaml`
   or `Starlight/`) and it defines colours, or when the other theme's counterpart exists
   (`Themes/Dark/Styles.xaml` ↔ `Themes/Light/Styles.xaml`); its styles are graded against that theme
   only, and a style key defined in both themes' dictionaries is resolved per theme where it's used
   (the element reads `Button⊕Style[Chip]@dark/base`; a key only one theme defines is
   `unknown-palette-key` for the other).
2. **Theme pair** — one `DarkTheme.xaml` × one `LightTheme.xaml`, keys overlapping ≥ 50%
3. **C# source of truth** — `("Key", "#dark", "#light")` tuple arrays
   (beats a single-theme XAML copy: a source that provides both themes wins)
4. **Merged dictionaries, single theme** — same union as (1), but only one value per key
5. **Single theme file** — one resource dictionary, both columns get the same value
6. **Nothing found** — falls back to hardcoded colors only, and **says so loudly**

(1) and (2) both report as palette mode `pair`; (4) and (5) as `single`. In a repo with two or
more `<Application>` roots (typically `App.xaml`), the merged paths (1) and (4) additionally
resolve each application subtree against its own palette; files outside every application use only
the shared area's own dictionaries (a key that only an application defines is reported as
`unknown-palette-key` there — its value depends on the host app). A path below the scanned root
with a folder (or file) named exactly `test`, `sample`, `demo`, `example` or `spec` — singular or
plural, digits allowed (`Tests2`) — or one whose last word is `Tests`/`Test`/`Specs` (`MyApp.Tests`,
`UnitTests`, `e2e-tests`; below the top level also `Fixtures`/`Mocks`) is never taken as a palette
source (XAML dictionary or C# tuple file) or an application root; its XAML is still audited.
`ExampleCorp.Core`, `TestRunner.UI`, `DemoScene` or `Contoso.Demographics` don't count. When nothing
else is found, the palette line names the files it skipped this way and how to point the config at
them.

Wrong guess? Override with `xamlcontrast.config.json` —
[schema](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/blob/main/docs/config-schema.md) (zh-Hant; the JSON example at the top is self-explanatory).
A property name the tool doesn't know (a typo such as `strictPallete`) is a config error
(exit 2) that names it, rather than silently ignored.
If your app is built on a theme library (MahApps, HandyControl, MaterialDesign…), the palette
is usually found fine but the window background isn't — see
[`rootBackground`](#json-output) below; the tool tells you this when it happens.

## CI

```bash
xamlcontrast src/MyApp --json report.json          # exit 1 on any pair below AA
xamlcontrast src/MyApp --fail-on fail              # looser: only pairs below the fail line block
xamlcontrast src/MyApp --sarif audit.sarif         # GitHub code scanning format
xamlcontrast src/MyApp --md report.md              # Markdown report (for a PR comment)
xamlcontrast src/MyApp --html report.html          # self-contained HTML report with colour swatches
```

Exit codes:

| code | meaning |
|---|---|
| `0` | pass |
| `1` | pairs below the gate — any `fail` or `warn` by default (only `fail` with `--fail-on fail`), or new / worsened pairs vs `--baseline` |
| `2` | usage, configuration or input-file error — bad flags, invalid config, a report or baseline file that can't be written or read |
| `3` | **the result can't be trusted** — zero pairs resolved (an empty scan is not a pass), coverage below `--min-coverage`, or a degraded palette with `--strict-palette` |

The gate is `--fail-on`, else the config's `failOn`, else `warn`. The three trust checks run before
either baseline mode, so a broken palette can't pass itself off as paid-off debt.

### Adopting on an existing project (baseline ratchet)

A gate that's red on day one gets turned off. Freeze the current debt once — every pair below
AA — then only block new or worsened pairs; debt may only shrink:

```bash
xamlcontrast src/MyApp --write-baseline xamlcontrast-baseline.json   # once; commit the file
xamlcontrast src/MyApp --baseline xamlcontrast-baseline.json         # in CI
```

Both flags take a path, and they can't be combined. Baseline keys contain no line numbers (lines
drift) but do record the worst ratio (so palette drift — same key, darker value — still gets
caught). The file carries a `schemaVersion` and a `keyVersion`; one with a missing field, one
written by 0.x, or one whose keys an older version generated (a later release may change how keys
are spelled) is refused (exit 2) with a note to regenerate it — rather than reporting every known
pair as NEW.

### Suppressing intentional low contrast

```xml
<!-- xamlcontrast-ignore: watermark, intentionally faint -->
<TextBlock Opacity="0.4" Text="DRAFT" ... />
```

The reason is **required** — a bare `<!-- xamlcontrast-ignore -->` is invalid and warned about
(the pair is still reported). The marker has to be exact — lowercase, a colon, the reason on the
same line. A misspelled one (wrong case, a colon with nothing after it, a missing colon, a reason
that wraps onto a second line) does not suppress anything and is warned about the same way
(`summary.invalidIgnores`). On a container, the marker covers everything inside it — its elements
and the `Style` definitions in its `Resources`. Suppressed pairs are counted in
`summary.suppressed`; nothing disappears silently.

### GitHub Action

```yaml
# .github/workflows/contrast.yml (in your project)
name: Contrast
on: [pull_request]
jobs:
  xamlcontrast:
    runs-on: ubuntu-latest        # the analysis is pure XML — no Windows needed
    permissions:
      contents: read
      pull-requests: write        # lets the action post the audit report as a PR comment
      # security-events: write    # only if you also pass `sarif: true`
    steps:
      - uses: actions/checkout@v7
      - uses: HSU-YU-MING/cornhsu-xamlcontrast@v1   # follows the latest 1.x
        with:
          root: src/MyApp
          # version: 1.0.0        # optional: pin the tool exactly; empty installs the latest 1.x
```

> **`pull-requests: write` is what makes the PR comment work.** Without it that step gets a 403.
> It is deliberately `continue-on-error`, so the run still gates correctly and you only lose the
> comment — but the failure is quiet, so it is worth getting right the first time.
> (Forked PRs get a read-only token regardless; that case is expected and not a misconfiguration.)

Failing pairs show up as inline annotations on the PR, and the audit report is posted as a
PR comment that updates in place instead of piling up. Alongside the findings table, the comment
lists what was **not** evaluated — exemptions, suppressions, parse failures, degraded palette
detection — because the real risk with an audit tool is not missing a finding, it is falsely
reporting health.

Annotations follow the gate: pairs it blocks are errors, warns it lets through (`fail-on: fail`)
are warnings, and known debt in baseline mode is a notice.

The JSON, Markdown and HTML reports are uploaded as the run's `xamlcontrast-report` artifact — open
`xamlcontrast-report.html` from it for colour swatches of every failing pair ([HTML report](#html-report)).

Inputs: `root` (required), `working-directory`, `version` (empty = the latest 1.x), `fail-on`
(`warn` / `fail`; empty = the config's `failOn`, else `warn`), `min-coverage` (empty = the config's
`minCoverage`, else 50), `baseline`, `strict-palette`, `comment` (turn off the PR comment),
`upload-report`, `sarif`, `share-stats` (default `true`; `false` sends no
[anonymous usage statistics](#anonymous-usage-statistics) and says so in the job summary), `artifact-name`
(default `xamlcontrast-report`). The switches take `true` or
`false`; an unknown value fails the job and names the input instead of quietly falling back to the
default. `root` and `baseline` must not start with `-` (write `./-name`).

**Running the action more than once in a workflow run** (a matrix, or two calls in one job)? Give
each call its own `artifact-name` — it names the uploaded artifact and keys the PR comment, so each
name keeps its own comment. With `sarif: true`, a non-default name is also passed as the code-scanning
`category`, so the two analyses don't replace each other. Same name twice means the second upload
collides and the comments (and SARIF analyses) overwrite each other.

### Push to a Parity dashboard (experimental)

`--push` uploads a report the audit already wrote to a [Parity](https://github.com/HSU-YU-MING/cornhsu-parity)
team dashboard (`Parity.Server`, which you host). It never audits — run the audit with `--json <file>`
first, then push that file:

```bash
xamlcontrast src/MyApp --json report.json                              # the audit — the report stays on this machine
xamlcontrast --push report.json --server https://dashboard.example.com  # needs XAMLCONTRAST_TOKEN in the environment
```

- **What it sends:** one `POST {server}/api/runs` whose body is the report file exactly as `--json`
  wrote it — file paths relative to the scanned root, line numbers, element names, colour values and
  keys, counts. Look at the file before your first push if that matters to you. As headers: the token,
  the commit (`--commit`, else `GITHUB_SHA`), the branch (`--branch`, else `GITHUB_HEAD_REF`, else
  `GITHUB_REF_NAME`), the actor and repository URL on GitHub Actions, and the gate from the report's
  `exitCode` (0 = pass, 1 or 3 = fail).
- **Environment:** `XAMLCONTRAST_SERVER` (or `--server`) is the dashboard's absolute `http`/`https`
  URL; `XAMLCONTRAST_TOKEN` is the project token the server's `create-project` command printed. There
  is deliberately no `--token` flag — flag values end up in shell history and CI logs.
- **Opt-in:** the report never leaves the machine unless you run `--push`; the audit's only request is
  the [anonymous usage statistics](#anonymous-usage-statistics) (counts only, and easy to turn off). The
  push goes only to the server you name and redirects are not followed, so the token
  is never forwarded elsewhere. Use `https`: over plain `http` the token travels unencrypted.
- **Exit codes:** `0` uploaded, `2` anything else — no token, a file that isn't an XamlContrast
  `schemaVersion` 6 report (checked before anything is sent), an unreachable server (60-second
  timeout) or an error reply. Never `1` or `3`; those are audit verdicts. `--push` together with a
  project root or any audit flag is a usage error.
- **Experimental:** the dashboard is not publicly deployed yet, so these flags, the environment
  variables, push-mode exit codes and the server protocol are outside the 1.x freeze and may change in
  a minor release. They join the freeze once the dashboard ships.

In a workflow, push after the action. The action leaves the tool on `PATH` and writes its JSON
report to `$RUNNER_TEMP/xamlcontrast-report.json` whenever the audit reaches a verdict (not on a usage
or configuration error, exit 2):

```yaml
      - uses: HSU-YU-MING/cornhsu-xamlcontrast@v1
        with:
          root: src/MyApp
      - name: Push to the dashboard (experimental)
        if: always()                # after a red gate too — that is the run worth seeing
        continue-on-error: true     # a dashboard outage, or a fork PR without secrets, doesn't fail the build
        shell: bash
        run: xamlcontrast --push "$RUNNER_TEMP/xamlcontrast-report.json"
        env:
          XAMLCONTRAST_SERVER: https://dashboard.example.com
          XAMLCONTRAST_TOKEN: ${{ secrets.XAMLCONTRAST_TOKEN }}
```

On GitHub Actions the step also appends a dashboard link to the job summary. On `pull_request` events
`GITHUB_SHA` is the merge commit, not the PR head; pass `--commit` to record another one. Calling the
action more than once in a job? Push after each call — the next call replaces the report file.

## JSON output

`--json report.json` writes `schemaVersion` 6 — frozen for 1.x, with the shape published as
[`docs/report.schema.json`](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/blob/main/docs/report.schema.json).
Every field is always present: `null`, `[]` or `{}` when there's nothing to report. Within 1.x,
fields are only added and `schemaVersion` stays 6 — **ignore fields you don't know**. Some values
are **open sets** that a 1.x release may extend — the unresolved reasons (`unresolvedBy` keys,
`unresolvedSites[].reason`), `paletteMode`, `summary.failOn` and `findings[].theme` — so **accept values you don't know** there too; the schema
lists them as strings with the current values in their description.

- Top level: `toolVersion`, `exitCode` (the audit verdict: 0, 1 or 3), `summary`,
  `unresolvedSites`, `findings`.
- `summary` carries the gate in effect (`failOn`, `minCoverage`, `strictPalette`) and every
  degradation signal — `paletteDegraded`, `coverage`, `unresolved` (with `unresolvedBy`), `skipped`,
  `deadForeground`, `disabledExempt`, `suppressed`, `parseErrors` / `parseErrorFiles`,
  `invalidIgnoreCount` / `invalidIgnores`, `unknownKeys`. If the tool couldn't see something, that
  fact is machine-readable.
- Each finding carries `file`, `line`, `element`, `fg`, `bg`, `ratioDark`, `ratioLight`, `colors`,
  `threshold`, `category`, `symmetry` (fail/warn; `null` otherwise), `isText`, `isLargeText`,
  `fontSize` (DIP, or `null`), `theme` and `baseline` (`known` / `new` / `worsened` for the pairs
  the gate blocks in `--baseline` mode). Suppressed pairs are counted in `summary`, not listed.
- `colors` (added in 1.1; `schemaVersion` stays 6) is what each ratio was computed on — the two opaque
  colours on screen, after compositing: the text colour already blended over its backdrop with its own
  alpha and the accumulated `Opacity`, the backdrop with its translucent layers blended. `fg` / `bg` say
  how the colours are *written*; `colors` says what they *are*:

  ```json
  "fg": "#FFFFFF ×0.5", "bg": "{Surface}", "ratioDark": 5.09, "ratioLight": 1,
  "colors": { "dark": { "fg": "#8E8E8E", "bg": "#1E1E1E" }, "light": { "fg": "#FFFFFF", "bg": "#FFFFFF" } }
  ```

  Recomputing the contrast of `colors.dark.fg` on `colors.dark.bg` gives exactly `ratioDark`. A theme is
  `null` exactly when its ratio is.

A pair that exists in **one theme only** (a style in a one-theme dictionary, or a use site whose
style resolves differently per theme — see palette detection above) has a ratio for that theme only:
`theme` says which (`"dark"` / `"light"`; `null` for pairs that exist in both), the other theme's
ratio and `colors` entry are `null`, and the console, Markdown, HTML, SARIF and Action annotations print
`n/a (dark theme only)` instead of a number. Failing and warning ones also read `symmetry: single-theme`.
`skipped` counts only text you cannot see (transparent, alpha ≈ 0, `Opacity="0"`); a translucent
colour is composited and graded.

`unresolved` is broken down by cause in `summary.unresolvedBy`, because the total on its own
doesn't tell you whether anything can be done about it:

| reason | meaning |
|---|---|
| `no-ancestor-background` | nothing up the tree declares a background — often a root container relying on an app-level implicit style, or vector artwork in a resource dictionary |
| `bound-or-gradient` | the colour comes from `{Binding}` / `{TemplateBinding}` / a gradient or image brush — only knowable at runtime. Also anything else the tool can't read as a colour (every WPF colour literal — `#RGB`, `#ARGB`, `#RRGGBB`, `#AARRGGBB`, the named colours — is read) |
| `unknown-palette-key` | the resource key isn't in the detected palette — a typo, or palette detection missed a file |
| `translucent-uncomposited` | a transparent or translucent background with nothing underneath to composite against — a translucent `Background` with no background above it, a style's own `Background="Transparent"` at its definition when no use site grades it (an implicit style, or a named style no element references with `Style=`; a used style is graded where it's used instead), or an `Opacity`-faded layer over nothing. `rootBackground` (below) fixes the first kind |
| `same-brush-pair` | a style sets `Foreground` and `Background` to the *same* brush — the Material template-opacity idiom (the template paints the background at 10–12% opacity as a tint); literally 1:1, not at runtime, and the opacity animation is invisible statically |
| `over-sibling-content` | the text sits on a sibling `Image`/media element in the same Grid cell — the real backdrop is a picture, unknowable statically; pairing with the ancestor background would fabricate a ratio |
| `in-style-template` | a text element with its own `Foreground` inside a style's `Template` setter (`<Setter Property="Template"><Setter.Value><ControlTemplate>`) — not graded yet (see Known limitations), counted so the gap shows in coverage |

`summary.coverage` is the share of pairs that resolved. **A run below `--min-coverage`
(default 50%) exits 3**, because a pass over a fraction of the project is not a pass — the same
principle as the non-configurable zero-pairs rule, which it generalises. Use
`--min-coverage 0` to opt out (`min-coverage: 0` in the Action, or `"minCoverage": 0` in
`xamlcontrast.config.json`).

Every unresolved pair is listed with its location: `--show-unresolved` prints
`file:line · reason · the value it got stuck on` (the background's value when the background is
what couldn't be resolved), and the JSON carries the same list in the top-level
`unresolvedSites` array (`file`, `line`, `reason`, `value`). Unknown keys are additionally named
with counts (`summary.unknownKeys`, and the top ones on the console) — each one is a dead
resource reference, a typo, or a palette file detection missed, which makes this a free lint
even before any contrast math.

**Expect the first run to stop at the coverage floor.** How much resolves depends on how the app
sets its colours. The four validation apps run at 73–96% (measured with 1.0: Kindling 95.8%,
QuillNest 89.4%, ZeroCel 73.4%; the fourth, Cornea, is a screen-capture overlay whose text sits on
nothing the tool can see — 0 pairs, exit 3). Public projects measured lower: applications 26–57%,
control libraries (MahApps.Metro, MaterialDesignInXaml, HandyControl) 5–19% — so **many projects
exit 3 out of the box**, below the 50% `--min-coverage` floor. That exit is the floor saying the result
doesn't represent the project, not a contrast verdict. Look at `unresolved by reason`:
`no-ancestor-background` dominating usually means a theme library supplies the window background —
declare it with `rootBackground` (below); `bound-or-gradient` dominating is the hard boundary of
static analysis (colours that flow through `{TemplateBinding}` and `{Binding}` exist only at runtime,
which is why control libraries resolve poorly) — lower the floor to what the project can reach
(`--min-coverage 20`, or `"minCoverage"` in the config) and treat the resolved subset as a sample.

**Apps built on a theme library** (MahApps, HandyControl, MaterialDesign…) are a special,
fixable case: your window background *key* lives in your repo's theme files, but the implicit
style that connects it to your windows lives inside the NuGet package, invisible to static
analysis. Declare it once in `xamlcontrast.config.json`:

```json
{ "rootBackground": "MahApps.Brushes.Window.Background" }
```

Measured on NETworkManager (a MahApps app, 154 XAML files): coverage went from **8.8% to
87.2%** with that one line — surfacing 284 genuine light-theme failures the tool was
previously blind to. A key not present in the detected palette is a config error (exit 2),
never a silent no-op.

`--md report.md` writes the human-readable Markdown version — this is what the Action posts as a
PR comment, and you can use it directly too.

### HTML report

`--html report.html` writes one self-contained page — inline CSS, no scripts, no external files — that
opens straight from a CI artifact (the Action uploads it as `xamlcontrast-report.html`). It shows:

- the verdict and why (exit 0, 1 or 3), and every degradation warning first — degraded palette
  detection, zero pairs, non-default thresholds, parse failures, invalid ignore comments;
- a summary: counts, coverage, the gate, palette mode and detection detail, and every not-evaluated
  counter (unresolved by reason, skipped, excluded, exempted, suppressed, parse errors) — zeros included;
- one card per `fail` and `warn` pair: location, element, foreground and background as written, and for
  each theme a swatch of the text colour and of the backdrop, a sample set in those two colours, the
  ratio, the hex values and whether it meets the threshold (category and pass/fail are always spelled
  out, never shown by colour alone);
- every unresolved pair with its reason and the value it got stuck on;
- the `ok` and `decorative` pairs, all of them, in collapsed sections.

The swatches are the JSON's `colors`. Everything taken from the scanned XAML is escaped, and the page's
Content-Security-Policy forbids scripts and external resources. It follows your light/dark setting. Like
the console and Markdown output, its layout is presentation rather than a frozen interface — programs
should read the JSON.

## Anonymous usage statistics

From 1.1, after each audit that reaches a verdict (exit 0, 1 or 3), XamlContrast sends **one** small
request to `https://stats.cornhsu.com/v1/xamlcontrast`. It is **on by default — in CI too** — and one
setting turns it off. It supports the author's master's thesis on what contrast problems in real XAML
apps look like; [stats.cornhsu.com](https://stats.cornhsu.com/) explains what is collected, what it is
used for and how long it is kept.

**What is sent** — counts and ranges, nothing else:

| field | what it is |
|---|---|
| `schema`, `tool`, `toolVersion` | `1`, `"xamlcontrast"`, the tool version (no build metadata) |
| `os` | `windows`, `linux`, `macos` or `other` |
| `ci` | whether `CI` or `GITHUB_ACTIONS` is `true` |
| `projectId` | `null` — unless *you* add one (below) |
| `data.exitCode` | the audit verdict: 0, 1 or 3 |
| `data.failOn` | the gate in effect: `warn` or `fail` |
| `data.paletteMode`, `data.paletteDegraded` | which palette detection path ran, and whether it degraded |
| `data.files`, `data.pairs`, `data.coverage` | how many files and pairs, and the coverage percentage |
| `data.counts` | how many pairs are `fail`, `warn`, `ok` and `decorative` |
| `data.unresolvedBy` | how many pairs are unresolved, per reason (the reasons in the table above) |
| `data.ratios` | non-decorative pairs counted by their worst ratio in six ranges: < 1.5, 1.5–2, 2–3, 3–4.5, 4.5–7, ≥ 7 |

**What is never sent:** file names or paths, element, style or resource names, colour values, the report,
your config, repository, user or machine names, or anything else from your project. The request carries
no User-Agent, cookies or custom headers. The collector stores no IP address (Cloudflare, which hosts it,
sees the connection the way any network provider does), keeps only the date it arrived, and rejects any
request with a field it doesn't expect.

**There is no ID unless you add one.** Without it, nothing links one run to another or to you — which
also means there is no way to find or delete "your" data individually. If you want your project's runs
grouped together, invent an ID (16–64 characters of `A-Z a-z 0-9 _ -`; don't derive it from your
repository's name) and put it in the config — a malformed one is a config error, exit 2:

```json
{ "stats": { "projectId": "pick-your-own-random-id-2026" } }
```

**Turn it off** — any one of these is enough:

- `DO_NOT_TRACK=1` — any value except `0` / `false`; it always wins
- `XAMLCONTRAST_SHARE_STATS=false`, or `CORNHSU_SHARE_STATS=false` (also covers the sibling tool
  Parity). Any value other than `1`, `true`, `yes` or `on` turns it off, so a typo errs toward not sending
- `"stats": { "share": false }` in `xamlcontrast.config.json`
- in the GitHub Action, `share-stats: false`

**See it before it's sent:** `xamlcontrast <root> --stats-preview` runs the audit as usual, sends
nothing, and prints to stderr the exact JSON it would send and whether — or why not — it would be sent
(`would not be sent: DO_NOT_TRACK is set`).

**What you'll see:** on your machine, a short notice on stderr the first time it sends (a marker file,
`cornhsu/xamlcontrast-stats-notice` in your user config folder — `%APPDATA%` on Windows, `~/.config` on
Linux — keeps it from repeating); in CI, one line on stderr on every run, and on GitHub Actions one line
in the job summary. Nothing goes to stdout, and nothing at all is printed when it doesn't send.

**It never changes your run.** The request waits at most 3 seconds (1.5 to connect), is never retried or
redirected, and any failure is ignored silently — the exit code and every output are exactly what they
would be without it. Development builds — any version starting with `0.0.0-`, such as `0.0.0-dev` when you
build from source — never send.

## Validated against real products

Not "runs on a demo" — this tool found and drove fixes for **250+ real contrast issues
across three shipped WPF apps** (before/after audited by the tool itself, at the time of the
fixes — July 2026, the v0.1.0-era tool):

| project | before | after (July 2026) |
|---|---|---|
| CelFlow | 39 fail / 57 warn | **0** (21 disabled-state exempted) |
| Kindling | 41 fail / 14 warn | **0 real** (3 remaining, all verified false alarms of known classes) |
| QuillNest | 13 fail / 31 warn | **0 real** (7 fail / 1 warn remaining, same) |

The "after" column is a record of that moment, not a live figure. Audited by the 1.0 tool
(October 2026) the three read ZeroCel (formerly CelFlow) 1 fail / 6 warn, Kindling 1 fail / 1 warn,
QuillNest 2 fail / 1 warn — the new failures are real ones the earlier tool couldn't see, and the
one that disappeared had been graded against the wrong background (details in the CHANGELOG).

How that trust was earned — the highlights, with the full story in the
[development retrospective](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/blob/main/docs/開發歷程回顧.md) (zh-Hant):

- **Two independent implementations cross-verified.** At v0.1.0 the PowerShell prototype (the spec)
  and this .NET port agreed on every number across all four validation projects —
  down to each failing pair's `file:line` and the exit code. The prototype has been frozen
  since; the .NET port is regression-checked against its own frozen snapshots of those projects
  (`scripts/verify-baselines.ps1` — maintainer-only, the four apps' source isn't public).
- **Every parsing rule came from a real false alarm or a real miss** — none were designed
  at a whiteboard. The 12th (template-root backgrounds) was found while verifying the
  fixes the tool itself had driven.
- **The tool produced eight "healthy-looking but wrong" reports during development.
  Each one is now a regression test.** The project's core rule, written in blood:
  **an audit tool's worst failure mode is not missing issues — it's false confidence.**
  Degradations must shout; nothing gets silently excused — exemptions, exclusions,
  suppressions, and parse failures are all counted and reported.
- **Remaining fails are named, not hidden**: every residual finding in the July 2026 table
  above mapped to a documented false-alarm class (see Known limitations) and is absorbed by
  the baseline ratchet on adoption.

## Known limitations

Honest list — full blind-spot table with per-case evidence in the
[planning doc](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/blob/main/XamlContrast專案規畫書.md) (zh-Hant):

- `TargetName` setters aimed at **inner** template parts (root-targeted ones are resolved)
- Cross-element correlated triggers (same condition flips fg on one element, bg on another) — false alarms
- Sibling backdrops are resolved for full-cell siblings in a `Grid` cell (solid ones directly;
  translucent ones — through the brush's alpha or the element's own `Opacity` — composited; bound
  or gradient ones reported as unresolved), and text over a sibling `Image` is honestly
  `over-sibling-content`. In a custom `*Panel`, or a derived `*Canvas` / `*Grid`, whose layout the
  tool can't know, text after a possible backdrop is `over-sibling-content` too (`InkCanvas` overlaps
  like `Canvas`) — unless you declare the container in the config's `layout`
  (`"overlap": [...]` or `"sideBySide": [...]`). Partial-coverage siblings (including anything in a `Canvas`, where nothing stretches)
  and images nested inside a backdrop element are still invisible
- Text elements with their own `Foreground` inside a style's `Template` setter are **not graded yet** —
  the tree walk doesn't enter styles. They are counted as unresolved (`in-style-template`, with
  `file:line` and the foreground), so coverage shows the gap; the host's own foreground, which the
  template renders through `{TemplateBinding Foreground}` or a `ContentPresenter`, is graded through
  the style chain as before
- Implicit styles (beyond the document root's background) — e.g. a button inside a `DataGrid` row
  is graded against the grid's background, not the selected row's highlight that the implicit
  `DataGridRow` style paints (measured: a ghost button reported ok is 4.06:1 on a selected row)
- `VisualStateManager` colour animations (`ColorAnimation` in state storyboards) are not
  audited **and not yet counted** — a Blend-era styling dialect (measured: dozens of sites
  in kaxaml/snoop-class projects, zero in modern ones). A counter is planned; until then
  this line is the disclosure.
- `Binding` / `TemplateBinding` colors are reported as *unresolved*, never guessed —
  guessing would trade honest uncertainty for false confidence

## Ran it on your project? I want to hear about it

Every one of the seventeen rules exists because a real codebase proved one was missing.
That method has a built-in weakness, and it bit this project: the first four validation
projects share one author and one house style, so two whole classes of bug cost *nothing*
on them and only surfaced when eight unrelated public projects were scanned.

**Your project is a sample I don't have.** The most valuable report is a false alarm —
somewhere it claims text fails and it doesn't. A tool people stop believing gets turned off,
so that is a more serious defect here than a miss.

[Open an issue](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/issues/new/choose) —
the templates ask for exactly what's needed to reproduce, English or 中文.
[CONTRIBUTING.md](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/blob/main/CONTRIBUTING.md) has the details, including what a pull request needs.

## See also

[**Parity**](https://github.com/HSU-YU-MING/cornhsu-parity) — sibling project, same
author, same philosophy (numeric checks that gate CI). Parity answers *"does the
implementation match the design?"* (Figma vs rendered values); XamlContrast answers
*"can people actually read it?"* One guards fidelity, the other guards legibility —
they meet in the same PR checks list.

## License

MIT © [許彧銘 Hsu Yu-Ming](https://cornhsu.com/)
