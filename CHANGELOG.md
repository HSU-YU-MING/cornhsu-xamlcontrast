# Changelog

All notable changes to this project will be documented in this file.
Format: [Keep a Changelog](https://keepachangelog.com/) / [SemVer](https://semver.org/).

## [Unreleased]

## 1.0.0 — 2026-10-22

**1.0 freezes the public interface.** From this release on, the nine surfaces listed under
*Interface freeze* below change only in the ways the 1.x compatibility rules allow; anything else
waits for 2.0. Getting there took the breaking changes that had to happen before a freeze — every one
is listed below with what to do about it — and the release also collects every fix since 0.6.1.
There is no 0.7.0: the breaking changes ship once.

### Upgrading from 0.6.1 — what you have to do

1. **The gate is stricter by default.** Without flags, *any* pair below AA now fails the run (exit 1);
   0.6.1 failed only on pairs below the fail line (threshold × 0.67) and let the rest through as `warn`.
   To keep the 0.6.1 gate, pass `--fail-on fail`, set `"failOn": "fail"` in `xamlcontrast.config.json`,
   or `fail-on: fail` in the Action. `--fail-on` is now a real override in both directions
   (flag > config > default); in 0.6.1 it could only tighten, so `--fail-on fail` was a no-op.
2. **Exit 3 means "this result can't be trusted".** Zero pairs, a degraded palette under
   `--strict-palette`, and coverage below `--min-coverage` used to exit 1 — the same code as "found
   violations", although the fix is different (configuration, not colours). They now exit 3. A CI step
   that treats any non-zero code as failure keeps working; a script that tests for exactly `1` has to
   handle `3` as well.
3. **Regenerate your baseline** with `xamlcontrast <root> --write-baseline <path>`. 1.0 refuses a 0.x
   baseline file (exit 2, and the message says to regenerate it): a baseline now records every pair
   below AA, not only fails, and carries a `schemaVersion`. Read as-is, every existing warn would have
   come back as NEW. `--baseline` and `--write-baseline` now require a path and can't be combined.
4. **Fix config mistakes 0.6.1 ignored.** An unknown property in `xamlcontrast.config.json` (a typo
   such as `strictPallete`) is now a config error (exit 2) that names it; so is
   `thresholds.largeText` greater than `thresholds.normalText`. `$schema` is allowed.
5. **JSON consumers: move to `schemaVersion` 6** (field changes below). The shape is published as
   [`docs/report.schema.json`](docs/report.schema.json).
6. **Action users: switch to `@v1`** (a moving tag that follows the latest 1.x). With `version` left
   empty the Action installs the newest 1.x tool, never 2.0. Its `fail-on` input now defaults to empty
   — the config's `failOn`, else `warn` — and every input value is checked: `fail-on: Warn`,
   `strict-palette: True` or `sarif: yes` used to fall back to the default silently and now fail the
   job (exit 2) naming the input.
7. **Expect different numbers on the same code.** Fixes since 0.6.1 find pairs that were silently
   skipped, report problems that were hidden, and count confident-but-wrong ratios as `unresolved`
   instead — see *Changed* and *Fixed* below. Measured on the four validation projects, 0.6.1 → 1.0:

   | project | pairs | unresolved | coverage | fail | warn | exit |
   |---|---|---|---|---|---|---|
   | Kindling | 168 → 182 | 1 → 8 | 99.4% → 95.8% | 0 → 1 | 1 → 1 | 0 → 1 (a real failure now found; under the new gate its warn fails too) |
   | QuillNest | 842 → 906 | 38 → 107 | 95.7% → 89.4% | 3 → 2 | 1 → 1 | 1 → 1 (one of the three is now `unresolved`) |
   | ZeroCel | 468 → 518 | 206 → 188 | 69.4% → 73.4% | 0 → 1 | 3 → 6 | 0 → 1 (a real failure now found) |
   | Cornea | 0 → 0 | 1 → 2 | — | 0 → 0 | 0 → 0 | 1 → 3 (nothing audited, as before; now reported as untrustworthy) |

   Coverage can read lower than in 0.6.1, and a project close to `--min-coverage` (default 50%; it
   applies in `--baseline` mode too) can go from passing to exit 3 on the coverage floor alone.

### Breaking changes

| # | What changed | 0.6.1 | 1.0 | What to do |
|---|---|---|---|---|
| 1 | Default gate | fail only below threshold × 0.67 | every pair below AA (`failOn` default `warn`) | nothing, or `--fail-on fail` / `"failOn": "fail"` / `fail-on: fail` to keep 0.6.1's gate |
| 2 | `--fail-on` | could only tighten the config (`flag \|\| config`) | overrides the config both ways (flag > config > default) | nothing; `--fail-on fail` now loosens a config `warn` |
| 3 | Untrustworthy result (0 pairs, degraded palette with `--strict-palette`, coverage below the floor) | exit 1 | exit 3 | treat 3 as failure in scripts that test for exactly 1 |
| 4 | Baseline contents | fail pairs only | every pair below AA (fail + warn), sorted by file, element, fg, bg; compared against the pairs the current gate blocks | re-run `--write-baseline` |
| 5 | Baseline format | no version; missing `count`/`worst` read as 0 | `"schemaVersion": 1` required, plus `"keyVersion"` (how the keys were generated; missing = 1); a missing field, a value out of range (`count` below 1, `worst` outside 1–21), a duplicate key (0.6.1: the later one silently won), an unknown version, keys from an older `keyVersion`, or a 0.x file → exit 2 naming the problem | re-run `--write-baseline` |
| 6 | Baseline in `--fail-on warn` mode | every warn blocked, known or not | warns are in the ratchet like fails: only new or worsened ones block | nothing |
| 7 | `--baseline [path]`, `--write-baseline [path]` | path optional (default `xamlcontrast-baseline.json`; the next word was taken as the path); `--write-baseline` silently won over `--baseline` | path required, a value starting with `-` is refused; using both → exit 2 | pass the path explicitly |
| 8 | Unknown config properties | named in a warning, ignored | config error (exit 2); `$schema` allowed | fix or remove the property |
| 9 | `thresholds.largeText > normalText` | accepted | config error (exit 2) | swap the two values |
| 10 | JSON report | `schemaVersion` 5 | `schemaVersion` 6 (below) | update consumers |
| 11 | Action `fail-on` | default `fail`; only `warn` was passed on, so the config's `failOn` never applied | default empty = config's `failOn`, else `warn`; `warn` and `fail` both passed on | set `fail-on: fail` to keep 0.6.1's gate |
| 12 | Action input values | unknown values silently became the default; `root: --version` printed the version and passed without auditing | unknown values fail the job (exit 2) naming the input; a line break in `root`/`working-directory`/`version`/`baseline`/`min-coverage`, or a `root`/`baseline` starting with `-`, is refused; `root` is passed after `--` | fix the value (`./-name` for a path that starts with `-`) |
| 13 | Action `version` empty | latest stable tool, any major | latest 1.x (`--version "[1.*,2.0.0)"`; without the upper bound, `1.*` would install 2.0.0 when no stable 1.x exists) | nothing; pin `version:` for fully reproducible runs |
| 14 | Baseline keys of styles resolved per theme | `TextBlock⊕Style[Tag]/base` | `TextBlock⊕Style[Tag]@dark/base` and `…@light/…` | re-run `--write-baseline` |
| 15 | A translucent background with nothing underneath | `no-ancestor-background` | `translucent-uncomposited` | nothing; scripts counting reasons see the shift |
| 16 | `xamlcontrast-ignore` on a container | suppressed the container's elements, not the `Style` definitions in its `Resources` | covers the `Style` definitions inside it too | nothing; check that the comment means what you intended |

**JSON `schemaVersion` 6** — every field is always present: no value is `null`, an empty list `[]`,
an empty map `{}` (5 omitted some and printed others).

- Top level: `unresolved` → **`unresolvedSites`** (it shared a name with the `summary.unresolved`
  count); new **`toolVersion`** (no build metadata) and **`exitCode`** (the audit verdict 0/1/3; a later
  output-file error still exits 2). Both are `null` when the report is produced through the library.
- `summary`: new **`failOn`**, **`minCoverage`**, **`strictPalette`** (the gate in effect, after
  flag > config > default), **`paletteDegraded`** (palette detection fell back to hard-coded colours
  or couldn't tell the themes apart — what `--strict-palette` blocks; until now only readable from the
  console line) and **`invalidIgnoreCount`** (next to `invalidIgnores`, like
  `parseErrors`/`parseErrorFiles`); **`paletteSource` removed** (it was `none` exactly when
  `paletteMode` is `none`).
- `findings[]`: `largeText` → **`isLargeText`** (it shared a name with `thresholds.largeText`);
  **`fontSize` is a number** (DIP, as written; 5 had a `"12px"` string rounded half-to-even) or
  `null`; new **`theme`** (`"dark"`/`"light"` for a pair that exists in one theme only — a style in a
  one-theme dictionary or resolved per theme — whose other theme's ratio is **`null`**: `ratioDark` and
  `ratioLight` are `number | null`, and the console, Markdown, SARIF and annotations print
  `n/a (dark theme only)` instead of repeating the one theme's ratio; `null` otherwise) and **`baseline`** (`known`/`new`/`worsened` for the pairs the gate
  blocks in `--baseline` mode; `null` otherwise). `symmetry` is `null` for ok/decorative.
- Kept on purpose: `findings[].threshold` (a number, unlike the `thresholds` object — no real
  ambiguity); `fg` as written in the source and `bg` normalised (both are part of the baseline key, and
  `fg` is the text you can grep for; `bg` is resolved from ancestors, styles and compositing and has no
  single source text); no `root` field (a machine-specific absolute path would make the same commit
  produce different reports; every path in the report is already relative to it).

### Interface freeze (1.x compatibility rules)

1.0 separates two things: the **syntax** people write and parse (frozen — breaking it is 2.0), and the
**audit semantics** that decide what the tool finds (improved in minor releases, with a notice).

**Frozen syntax.** Breaking any of these is a 2.0 change:

1. **CLI** — `xamlcontrast <project-root> [options]` and `xamlcontrast [options] -- <project-root>`;
   the flags `--json <path>`, `--sarif <path>`, `--md <path>`, `--fail-on <warn|fail>`,
   `--strict-palette`, `--min-coverage <0-100>`, `--baseline <path>`, `--write-baseline <path>`,
   `--show-ok`, `--show-unresolved`, `--version`, `-h`/`--help`, with their value forms and defaults
   (`--fail-on` default `warn`, `--min-coverage` default 50, flag > config > default).
2. **Exit codes** — `0` pass, `1` pairs below the gate (or new/worsened ones in `--baseline` mode),
   `2` usage, config or input-file error (including an output file that can't be written), `3` the
   result can't be trusted. Order: usage/config/input errors → the three trust checks (before either
   baseline mode) → baseline modes → findings.
3. **JSON report** — `schemaVersion` 6: every field name and type in `docs/report.schema.json`.
   `category`, `symmetry` and `baseline` are **closed sets** (a new member is 2.0). The unresolved
   reasons (`unresolvedBy` keys, `unresolvedSites[].reason`), `paletteMode`, `summary.failOn` and
   `findings[].theme` are **open sets**: 1.x may add a value (a new blind spot, a detection path,
   `--fail-on none`, a high-contrast theme), existing values keep their spelling, and the schema writes
   them as strings with the current values in the description.
4. **Action inputs** — names, defaults and accepted values of `root`, `working-directory`, `version`,
   `fail-on`, `min-coverage`, `baseline`, `strict-palette`, `comment`, `upload-report`, `sarif`,
   `artifact-name`; `@v1` installs only 1.x tools (`[1.*,2.0.0)`). The wording of annotations and of the
   PR comment is presentation, not frozen — and so is the CLI's console and Markdown output: read the
   JSON report when a program needs the results.
5. **Config file** — `xamlcontrast.config.json` at the project root: every property, its type, range
   and default (`thresholds` 4.5 / 3.0 / 0.67, `failOn` `warn`, `minCoverage` 50, `ignore.requireReason`
   `true`, `layout` names without a namespace prefix, …), unknown properties rejected, `$schema`
   allowed (see `docs/config-schema.md`).
6. **Baseline file** — `schemaVersion` 1 and `keyVersion` (missing = 1); `entries[]` of `file`,
   `element`, `fg`, `bg`, `count` (≥ 1) and `worst` (1–21), no duplicate keys; the key is
   `file|element|fg|bg` with no line numbers, `file` relative to the root with `/`. Every 1.x reads
   every baseline its own `keyVersion` writes. **The text inside `element`, `fg` and `bg` is audit
   semantics, not frozen syntax** — element labels (`Name⊕Style[Key]@dark/state`), the composite
   background description (`{Hi} over {Bg}`, `#88000000 over #FFFFFF`), the ` ×0.5` opacity note. A
   minor that changes how they are spelled bumps `keyVersion`, and an older baseline is then refused
   with "regenerate it with --write-baseline" instead of a wall of false NEW entries.
7. **SARIF** — 2.1.0; the `wcag-contrast` rule and its level mapping (`error` = fail, `warning` = warn)
   are frozen, and 1.x may add rules; `uri` is relative to the scanned root. **A SARIF level states
   severity, not the gate**, deliberately: under the default `warn` gate a warn pair fails the run yet
   stays `warning` in code scanning, which has its own severity scale. The Action's inline annotations
   are the ones that follow the gate (`error` for what the gate blocks, `warning` for warns it lets
   through, `notice` for known baseline debt).
8. **Suppression comment** — `<!-- xamlcontrast-ignore: reason -->`: case-sensitive, at the start of
   the comment, reason required (unless `ignore.requireReason: false`) and on the same line; applies to
   the immediately following element and its whole subtree, including `Style` definitions inside it;
   malformed variants never suppress and are listed in `summary.invalidIgnores`.
9. **Audit scope (hard rules, default behaviour)** — by default `*.xaml` under the root are audited;
   `bin` and `obj` directories below the root (any case) are skipped; `*.cs` files are read only to
   find a C# palette; the config is read from `<root>/xamlcontrast.config.json` only. Opt-in scope
   extensions (a `--config` path, extra exclude paths, `.axaml` files) are additions → minor; changing
   what is audited by default is 2.0.

**Audit semantics — not frozen, improved in minor releases** (rule 3 below): which pairs exist and what
they are graded against — the parsing rules, palette detection (including which files count as test
or sample paths: today a folder or file named exactly `test`, `sample`, `demo`, `example` or `spec`,
singular or plural, digits allowed, or whose last word is `Tests`/`Test`/`Specs` — so `MyApp.Tests`,
`UnitTests` and `Tests2` count, `ExampleCorp.Core`, `TestRunner.UI` and `Contoso.Demographics` don't), which dictionaries are palette sources or belong to
one theme, how an unresolved pair is classified, and the text of element labels and composite
backgrounds.

**Evolution rules** (a promise from this release on):

1. Breaking any frozen surface above → **major**.
2. Additions only — a new flag, input, config property, JSON field, SARIF rule, or a value in an open
   set → **minor**. `schemaVersion` does not change for additions; it changes only with a breaking
   change, i.e. a major. **JSON consumers must ignore fields and open-set values they don't know.**
3. Audit-semantics changes — a rule that finds more pairs, a confident ratio that becomes an honest
   `unresolved`, a backdrop resolved differently, a better test-path heuristic — are **minor**, never
   patch, and the CHANGELOG opens with an *Upgrading* note giving the measured effect on the
   validation projects. If baseline keys change, the release bumps `keyVersion` and the note says to
   re-run `--write-baseline`.
4. **Changing a default** (the gate, the thresholds, `minCoverage`, `requireReason`) **is 2.0.** This
   is deliberately stricter than the Parity template it follows, where tolerance defaults may move in a
   minor with a notice: here a default *is* the gate, so it does not change in a minor. An audit-semantics
   improvement (rule 3) can still turn a build red in a minor — its *Upgrading* note says so.
5. Fixes that change no output → **patch**.
6. When an old artifact stops being valid, the tool fails loudly and says what to do — a 0.x baseline,
   an unknown `schemaVersion`, a baseline from an older `keyVersion` — never a silently wrong result.

Forward compatibility is not promised: a config that uses a property added in 1.y is refused by an
older 1.x (exit 2, naming the property).

### Added
- **`--` ends the options**: everything after it is the project root, so a root that starts with `-`
  can be audited (`xamlcontrast --json r.json -- -legacy-ui`). The Action passes its `root` this way.
- **JSON**: `toolVersion`, `exitCode`, `summary.failOn` / `minCoverage` / `strictPalette` /
  `paletteDegraded` / `invalidIgnoreCount`, `findings[].theme` / `baseline`; the shape is published as
  `docs/report.schema.json`.
- **Action**: a `min-coverage` input (empty = the config's `minCoverage`, else 50), and an
  `artifact-name` input (default `xamlcontrast-report`) that names the uploaded artifact and keys the PR
  comment — give each call its own name when the action runs more than once in a workflow run (with
  `sarif: true` a non-default name is also the code-scanning `category`); the same name twice collides on
  upload and the comments overwrite each other. Annotations follow
  the gate: pairs the gate blocks are `error` (with the default gate, warns too), warns it lets through
  are `warning`, and known debt in `--baseline` mode is `notice` — 0.6.1 marked known debt as `error`
  on a green job.
- **Config `layout`** declares how your own containers lay out their children, so rule 17 doesn't have
  to guess: `"layout": { "overlap": ["MyCardHost"], "sideBySide": ["MyToolbar"] }`. A name in
  `overlap` stacks its children in one cell (the earlier siblings can be the text's backdrop); a name in
  `sideBySide` places them apart. Declarations win over the built-in rules; a name in both lists, or a
  name with a namespace prefix (`l:CardGrid` — names are matched without one), is a config error; a
  declared name that matches no element is named on the console and in the Markdown report.

### Changed
- **Text inside a style's `Template` setter is no longer silent.** The tree walk never enters a style, so a
  text element with its own `Foreground` inside `<Setter Property="Template"><Setter.Value><ControlTemplate>`
  was graded nowhere and counted nowhere — coverage read as if it didn't exist. It is now counted as
  unresolved under a new reason, `in-style-template`, with its location and foreground. Grading it is
  audit semantics for a 1.x release; the host's foreground, rendered through `{TemplateBinding Foreground}`
  or a `ContentPresenter`, is graded through the style chain as before. Measured: Kindling +3,
  QuillNest +12, ZeroCel +4 unresolved.
- **A composited background has one spelling** in `bg` and in baseline keys: each layer as `{Key}` or
  `#RRGGBB`/`#AARRGGBB`, joined by ` over `, with no outer braces — `{SelectionHi} over {Bg1}`, `#88000000 over #FFFFFF`.
  0.6.1 mixed the source text with `{Key}` and wrapped the whole thing in another pair of braces
  (`{{DynamicResource SelectionHi} over {Bg1}}`, `{#8000 over #FFFFFF}`). Baselines are rewritten for 1.0 anyway.
- **A translucent background with nothing underneath now reads `translucent-uncomposited`**, the reason
  that describes it, instead of `no-ancestor-background`. The `rootBackground` hint on the coverage-floor
  and zero-pairs exits counts these sites too — a declared root background is what they are missing.
- **A `xamlcontrast-ignore` comment on a container also covers the `Style` definitions in its
  `Resources`.** 0.6.1 suppressed the container's elements but still reported the definition-site
  pairs of styles declared inside it.
- `--help` documents `--fail-on <warn|fail>` and the four exit codes; the exit lines read
  `exit 1: N pair(s) below AA` (no `(--fail-on warn)` suffix) and `exit 3: …`.
- The CLI decides the exit code before writing `--json`/`--sarif`/`--write-baseline`/`--md`, so the
  JSON can carry it; `json written: …` now prints after the `exit …` line.

### Changed — translucent text colours are graded, not skipped
- **A translucent foreground is composited onto its resolved background and graded**, like element
  `Opacity` always was. Until now every translucent text colour (`#AARRGGBB`, a palette brush with
  `Opacity`, a palette key translucent in only one theme) was "skipped" — and skipped pairs are not in
  the coverage denominator, so a 20%-white hint on black (1.66:1) never reached the report while
  coverage read 100%, and a key translucent in one theme hid the *other* theme's real failure too.
  Fully transparent text is still skipped; a translucent colour over a background the tool can't
  resolve is counted as `unresolved`. This reverses a deliberate earlier design; expect more pairs.
- **A style whose foreground and background share a definition but whose background is transparent or
  translucent is graded where it is used**, against the element's real backdrop — the element path
  used to skip same-source pairs, so these pairs were graded nowhere. The definition site, which has
  nothing underneath to composite against, counts the pair as `unresolved`
  (`translucent-uncomposited`) instead of `skipped` **when no use site grades it** — an implicit style
  (no `x:Key`), or a named style no element references with `Style=` (applied from code-behind or
  `ItemContainerStyle`, or unused); as `skipped` it vanished from the report with coverage still at
  100%. A style some element uses is graded there and not counted twice.
- **`skipped` now means only text you cannot see** (transparent, alpha ≈ 0, `Opacity="0"`) —
  whatever the background, so an `Opacity="0"` toast over a bound background is skipped, not
  `unresolved` — and the console/Markdown label says so: `skipped (invisible: transparent or opacity 0)`
  (was `skipped (translucent, invisible)`). The JSON field is unchanged.
- **Element `Opacity` on the background's owner (or above it) fades the background too.** WPF renders
  an element with `Opacity` as one layer and blends the whole layer; the tool used to fade only the
  text, so white text in a 50%-opaque black `Border` on white was reported 5.32:1 (ok) when it is
  3.95:1. When the background sits outside every `Opacity` layer the numbers are unchanged; a faded
  layer over nothing is `translucent-uncomposited`. A translucent state background over a bound one
  is now `bound-or-gradient`, naming the binding (it read `no-ancestor-background`).

### Fixed — findings that silently disappeared or were graded against the wrong colour
- **A screen with one local brush is no longer dropped from the audit.** Merged-dictionary detection
  treated any XAML with a keyed `SolidColorBrush`/`Color` as a palette file and excluded it whole —
  a `UserControl.Resources` brush or an `App.xaml` with `Application.Resources` colours made the
  file vanish (`files` short, no message). Only files whose root is a `ResourceDictionary` are now
  left out of the file count, and even those are still walked (data templates included).
- **A screen's local resources are visible only in that screen**, as in WPF's resource lookup. Inside
  it they override the global value — literal brushes, and brushes referencing a `Color` (resolved per
  theme against the screen's own and the global colours); a brush bound to `{Binding}` reads as
  `bound-or-gradient`, and one referencing a colour that can't be found (a library's
  `MahApps.Colors.Accent`) is `unknown-palette-key` naming that colour. A key only some screen defines
  is `unknown-palette-key` everywhere else. Before, a screen's local literal could override a theme key
  project-wide, an unreadable local value could knock the key out everywhere, one screen's local brush
  answered for another's, and a local reference to a library colour was reported as an unreadable theme
  value.
- **Styles defined in theme dictionaries are audited.** The element path skips a pair whose
  foreground and background come from the same style, expecting the definition-site check to cover
  it — but palette files never got that check, so a 1.2:1 button style in `Themes/Dark.xaml` was in
  no finding and no counter. Palette files now get the style check (they still don't count as screens).
  When the palette comes from separate dark/light XAML dictionaries, a dictionary belongs to one theme
  when its file name or last folder contains *dark* or *light* as a whole word (`Dark.xaml`,
  `DarkTheme.xaml`, `Colors.Dark.xaml` — not `Highlight.xaml` or a `Starlight/` folder) and it defines
  colours, or when the other theme's counterpart exists (`Themes/Dark/Styles.xaml` ↔
  `Themes/Light/Styles.xaml`, `darktheme.xaml` ↔ `lighttheme.xaml`); a styles-only `DarkButton.xaml`
  with no `LightButton.xaml` is shared. Its styles are graded against that theme only (single-theme
  symmetry), and a style key defined in both themes' dictionaries is resolved per theme where it is
  used (`Button⊕Style[Chip]@dark/base`) — pairing a dark style with light values reported failures for
  combinations that never exist at runtime. Such findings carry a ratio for that theme only (the other
  is `null`), and the JSON's `theme` names that theme; a style key only one theme defines is `unknown-palette-key` (naming the
  style) for the other theme. Grouping the *palette* into dark and light still uses the looser 0.6.1
  rule (any part of the path), so `darktheme.xaml`, `Dark2.xaml` or `深色Dark.xaml` still form a pair;
  when file names can't tell the themes apart at all (`Ocean.xaml` / `Nord.xaml`) and three or more
  keys are defined with different values, detection is reported as degraded (`--strict-palette`
  blocks it) instead of silently grading one theme.
- **An `App.xaml` whose root is an `Application` subclass** (`<prism:PrismApplication>`, or
  `<local:App>` with `<Application.Resources>`) is the application, not a screen: its resources are
  global, and it bounds a multi-app scope.
- **Property-element syntax is read.** `<Border.Background>` / `<TextBlock.Foreground>` /
  `<Setter.Value>` were ignored: a gradient background was graded against the ancestor (confident
  wrong `ok`), and a property-element foreground vanished from every counter. Solid brushes are
  resolved (with `Opacity`); gradients, image brushes and resource-plus-opacity count as
  `bound-or-gradient`.
- **`SolidColorBrush Opacity` in the palette is read.** A 20%-white hint brush on black (~1.66:1)
  was graded as opaque white (21:1); it is now a translucent colour, graded as described above
  (1.66:1, fail). Property elements named after a base class (`<Panel.Background>`,
  `<Control.Foreground>`) are read too.
- **A value one theme can't read is no longer filled from the other theme.** In merged dictionaries a
  typo like `#EEEEEEE`, a bound colour or an unreadable `Opacity` on one side made the tool borrow the
  other theme's value and report a fabricated ratio; so did a brush whose `Color` reference exists in
  only one theme. The key is now dropped (uses count as `unknown-palette-key`, marked "dropped:
  unreadable definition" on the console) and named on the palette line (`!! N key(s) dropped — …`).
  A dropped key still counts towards the three-key detection threshold, so dropping one can't push
  detection down to "pick one theme file for both columns"; if *every* key is unreadable, detection is
  reported as failed (degraded — `--strict-palette` blocks it). Hex values with non-hex digits
  (`#GGGGGG`) are rejected too — they used to crash the audit.
- **Commented-out definitions are no longer part of the palette** (XAML `<!-- -->`, and C# tuple lines
  starting with `//`); an old value in a comment used to override the live one.
- **Test projects can't hijack the palette.** C# tuple detection now skips test/sample paths like the
  XAML side does (`[InlineData("Bg", "#..", "#..")]` matched the tuple format and produced 1:1 false
  fails while making detection look healthy), and the path rule now looks at whole folder names — exactly `test`, `sample`, `demo`,
  `example` or `spec` (plural and digits allowed), or a last word `Tests`/`Test`/`Specs` — so `MyApp.Tests`,
  `UnitTests`, `Foo_IntegrationTests`, `MyApp.Specs` and `Tests2` count, while `Contoso.Demographics`,
  `Testimonials.Module`, `ExampleCorp.Core`, `TestRunner.UI`, `DemoScene` and `SampleRate` don't (0.6.1
  excluded those as samples, so a project whose only palette lived there audited 0 pairs). When no palette
  is found, the palette line names the candidate files skipped this way and how to name them in the config
  (it used to say "no ResourceDictionary color keys").
- **`DataTrigger` disabled-state exemption is no longer a substring match.** `{Binding IsEnabledByLicense}`
  or a converter named `IsEnabledConverter` counted as WCAG 1.4.3 "disabled". The binding's path must
  now be exactly `IsEnabled` (or `(IsEnabled)` / `(UIElement.IsEnabled)`), point at the element itself, its templated
  parent or an ancestor, and have no converter; a view-model flag (`{Binding IsEnabled}` without
  `RelativeSource`, or `DataContext.IsEnabled`) is graded normally.
- **A palette key translucent in only one theme** crashed the audit (dark side translucent) or had its
  alpha stripped (light side translucent, 3.91:1 reported as 1.3:1). Each side now carries its own alpha.
- **Rule 17 no longer invents backdrops in a `Canvas`** (nothing stretches there; an empty `Border` is
  0×0) or in side-by-side panels with derived names (`VirtualizingWrapPanel`, `MyDockPanel`); in a
  custom `*Panel`, or a derived `*Canvas` / `*Grid`, whose layout is unknown, text after a possible
  backdrop is `over-sibling-content` instead of a guess, and `InkCanvas` overlaps like `Canvas`.
  **Translucent full-cell siblings** — through the brush's alpha or the element's own `Opacity` — are
  composited, and **bound / gradient ones** are reported as unresolved, instead of letting the text
  fall through to the ancestor's colour; a transparent sibling stays transparent whatever its
  `Opacity`.
- **A trigger state that differs only in the light theme is no longer dropped as a duplicate.** The
  "trigger changed nothing visible" check compared dark values only.
- **Definition-site style checks merge same-condition triggers and follow `BasedOn`**, like the element
  path already did — no more phantom "hover foreground × base background" fails.
- **Nested applications' dictionaries no longer leak into the outer application's scope**, and files
  outside every application no longer borrow an application's palette when the shared area has none
  (an app-only key is `unknown-palette-key` there — its value depends on the host).
- **`FontSize` with a unit is read** the way WPF's converter reads it — `18pt` (= 24 DIP, large text),
  `0.25in`, `1cm`, `12px`. 0.6.1 read plain numbers only, so `FontSize="18pt"` text was held to 4.5:1 as if it had
  no size, and the JSON's `fontSize` was `null`.
- **Short colour literals and the full WPF named-colour table** (`#AAA`, `#8000`, `Navy`, `Teal`, …) are
  resolved instead of being filed under `bound-or-gradient`.
- **`bin`/`obj` exclusion looks only below the scanned root, whole segments, any case** — a project that
  itself lives under a `bin` directory scanned 0 files; `Bin\` build output was scanned on Windows.
- **Config-forced palette files are excluded whatever the root spelling** (relative, forward-slash,
  `sub/..`); previously only an absolute normalised root worked, which the Action never passes.

### Fixed — exit-code contract and guard rails
- `--json`/`--sarif`/`--md` no longer swallow the next flag as a file name (`--json --strict-palette`
  wrote a file called `--strict-palette` and silently disabled the guard). A value starting with `-` is
  a usage error (exit 2); write `./-name` if you really mean such a file.
- `--min-coverage NaN` is rejected (exit 2) — NaN compared false everywhere and switched the floor off.
- A `null` config section, array or array element, an empty element name in `classification`, a NUL in a
  configured path, or an unreadable config file now exits 2 with the field named, instead of an
  unhandled .NET exception outside the 0/1/2 contract.
- A locked/unreadable `*.cs` file no longer crashes the run: it is skipped and listed as a parse error.
- Numbers written into baseline keys (`fg ×0.5`), SARIF messages and config errors use the invariant
  culture; on comma-decimal machines baselines no longer change keys.

### Changed — reporting details
- The baseline's top-level keys are case-insensitive like the entry fields (`Entries` was refused).
- Misspelled `xamlcontrast-ignore` markers (wrong case, empty or missing reason, missing colon, reason on
  a second line) still don't suppress anything, but are now listed in `summary.invalidIgnores`.
- `unresolvedSites[].value` / `--show-unresolved` name the **background** value when the background is what
  couldn't be resolved (it used to repeat the foreground value).
- The exit-1 line prints the fail factor in effect (`below threshold x 0.67`) instead of a hard-coded
  `2/3`; `--help` lists `--version` and `-h, --help`; a usage error prints the
  usage to stderr, with a note when `--baseline`/`--write-baseline` consumed the project root.
- The zero-pairs exit also prints the `rootBackground` hint when it applies.
- Action: the annotation/SARIF path prefix is normalised correctly for `root: .` and `/./` segments.
- Action annotations escape their values as workflow commands require (`%`, CR and LF in the message;
  also `:` and `,` in the `file=` property): a comma in a file name cut the property short, and a line break
  let the rest of the value start a new workflow command. The Markdown report escapes backticks, and its
  code spans use a longer fence when the text contains one.
- `scripts/verify-baselines.ps1` follows the CelFlow → ZeroCel rename and keeps going (with a red line)
  when a validation project is missing, instead of aborting under Windows PowerShell 5.1. Its expected
  exit code follows the 1.0 rules, or the snapshot's own `exitCode` when the snapshot has one.

## [0.6.1] - 2026-08-12

### Added
- **The `rootBackground` escape hatch is now offered at the moment people hit the wall.**
  0.6.0 shipped the fix but documented it in the README only — and a theme-library user
  following the README's first command lands on the coverage-floor exit code (NETworkManager
  measured 8.8%) *before* ever reading that far. Nobody goes back to the README after being
  rejected; they conclude the tool doesn't support their project and close it. When the run
  fails the coverage floor **and** `no-ancestor-background` is at least half the unresolved
  pairs, the exit message now carries the config line itself. Deliberately silent when
  `bound-or-gradient` dominates (`rootBackground` cannot help there — pointing at it would
  burn the user's one attempt and bury the real conclusion) and when `rootBackground` is
  already declared (repeating it reads as "your config didn't take effect").

### Fixed
- **README's sample output no longer lies.** The showcase block still showed the pre-0.5
  palette line and was missing the coverage row — the first thing a new user sees did not
  match what they get. Now verbatim from an actual `samples/demo` run.
- **README's palette-detection list was missing the path that fires most often.** It listed
  four shapes; the tool tries six, and merged-dictionary detection (added in 0.5, and what
  `samples/demo` itself hits) was not among them. All six are now documented in tried-order,
  with the mode each reports as.
- **zh-Hant README claimed seventeen parsing rules and listed thirteen** — the four rules
  found in the August public-project sweep were never added to the translated list.
- **The sample output can no longer drift silently.** `scripts/verify-readme-sample.ps1`
  runs `samples/demo` and diffs the result against the fenced block in both READMEs
  (whitespace-normalised, so trimming columns for the page is fine but a changed, missing
  or extra line is red); CI runs it on every push. It had drifted for two releases before
  anyone noticed — the documentation form of the failure mode this whole project exists to
  prevent. `-Update` re-pastes from a live run.
- **The version number now has exactly one source: the git tag.** The CLI csproj still
  hardcoded `0.5.1` after v0.6.0 shipped. Packages published from CI were correct (the
  release workflow overrides the version from the tag), but anything built from source
  reported the stale number — including the `toolVersion` it stamps on JSON reports. The
  field is now `0.0.0-dev`: unmistakably not a release, and nothing to keep in sync.
  (Deleting it outright would fall back to MSBuild's `1.0.0` default — worse, since the
  project deliberately stays on 0.x until its interfaces freeze.) `RELEASING.md` now
  states the rule, because the drift came from someone bumping the field by hand.

## [0.6.0] - 2026-08-10

### Added
- **Every unresolved pair now carries its location.** `--show-unresolved` lists
  `file:line · reason · offending value` for each one, and the JSON gains a top-level
  `unresolved` array (schemaVersion 5). The reason breakdown said *why* coverage was lost but
  not *where* — "57 pairs have no ancestor background" is a diagnosis without an address.
- **Unknown palette keys are named, with counts** (`summary.unknownKeys`, top ones on the
  console). Each is a dead resource reference, a typo, or a palette file detection missed —
  a free lint that may fire more often than the contrast math itself.
- **README sets coverage expectations by project type**: applications resolve well (validation
  apps 89–100%, public apps 26–57%); control libraries are inherently low (5–19%) because
  their colours flow through `{TemplateBinding}`/`{Binding}` — a hard boundary of static
  analysis, not a configuration problem.

### Added
- **`rootBackground` config — the escape hatch for theme-library apps.** Scanning three
  *new* public projects surfaced the largest shape yet: apps built on MahApps/HandyControl/
  MaterialDesign keep their window-background *key* in their own theme files (dark and light
  both), but the implicit style connecting it to their windows lives inside the NuGet
  package — invisible to static analysis, so every view lost its floor
  (NETworkManager: 726 `no-ancestor-background` sites across 100 files; QuickLook: 269).
  The tool must not guess; the user knows. One config line declares it:
  `{ "rootBackground": "MahApps.Brushes.Window.Background" }` — a palette key or a literal
  colour. Measured effect on NETworkManager: coverage **8.8% → 87.2%**, surfacing 284
  genuine light-theme failures (hand-verified: Gray5 `#B9B9B9` on white = 1.96:1, Gray3
  `#9D9D9D` = 2.71:1 — exact to the digit). A key missing from the detected palette is a
  config error (exit 2), never a silent no-op.
- **Rule 17: sibling backdrops in overlapping containers.** In a Grid cell (or Canvas /
  custom `*Panel`), earlier siblings paint beneath later ones. Two halves, closing the
  "sibling backgrounds" blind spot the prototype header has documented from day one:
  *(a)* a full-cell solid sibling (a `Border`/`Rectangle` with no explicit size and stretch
  alignment) **is** the text's real backdrop — previously these pairs were mispaired with an
  ancestor or lost to `no-ancestor-background`; *(b)* a sibling `Image`/media element means
  the backdrop is a picture, statically unknowable — now honestly `over-sibling-content`
  instead of a fabricated ratio against the ancestor (HandyControl's carousel: white text
  over a photo was reported light=1 against `RegionBrush`). Accent bars (`Width="4"`,
  non-stretch alignment) are excluded from backdrop candidacy.

  Re-freezing the validation baselines surfaced something worth recording: **Kindling's
  single long-standing fail — carried in the baselines since v0.1.0 — was a false positive
  of exactly this shape.** The "空" label sits on a sibling `<Border Background="Black"/>`
  (the source comment even says "covers the thumbnail"); grey-on-black is 7.46:1, passing.
  The rule also surfaced one genuinely marginal new warn (missing-asset label, light theme
  4.32:1) and honestly reclassified eight CelFlow canvas overlays as `over-sibling-content`.
- **Application-level resource scoping.** When a repo contains two or more `<Application>`
  roots (ScreenToGif ships a second Translator app; Playnite has Desktop and Fullscreen),
  each App.xaml's directory becomes a scope: files in an app resolve against *its subtree ∪
  the shared area*, and shared files (typically the library itself) against the shared area
  only. Without this, identical key names with different per-app values cross-contaminate —
  Translator's `#FF003399` heading was paired with the *main* app's dark
  `Panel.Background.Level4` and reported **1.2:1 fail** where its own palette gives
  **10.4:1 ok**. Single-app repos take the exact same path as before. Element-level
  `Resources` and merge order remain out of scope — the multi-app cut is where the observed
  false positives actually came from.

  Implementation note, recorded because it is the project's least favourite shape: the first
  build of this feature was **silently inert** — scope directories were normalised to
  backslashes by `GetDirectoryName` while enumerated file paths kept the caller's
  forward-slash root, so `StartsWith` never matched, every file fell into the shared area,
  and every scope quietly received the global palette. No error anywhere; found only by
  probing the three scopes and seeing three identical palettes. Path comparisons now
  normalise via `GetFullPath`, and the regression test runs with a forward-slash root.

### Fixed
- **Same-brush style pairs are unresolved, not failures.** MaterialDesign's navigation items
  set `Foreground` *and* `Background` to `MaterialDesign.Brush.Primary` — deliberately: the
  template paints the background borders at 0.1–0.12 opacity via VisualState animations, so at
  runtime it's full-strength text over a 12% tint, not 1:1. The style-pair audit's premise
  (the Background setter is the backdrop) doesn't hold when the template composites it, and
  the opacity lives in animation storyboards static analysis cannot see — so per the "don't
  guess" rule these pairs are now classified `same-brush-pair` under `unresolved`, naming the
  brush, instead of producing guaranteed-false 1:1 failures. Six such pairs in
  MaterialDesignInXaml (28 → 22 fails); identical-literal pairs (`#333` on `#333`) still fail,
  as those are far more likely genuine.
- **Derived control names now classify by suffix.** The text/decorative element lists matched
  by exact name only, so `MetroProgressBar` (MahApps) missed the `ProgressBar` entry in the
  decorative list and its `Foreground` — the progress fill, not text — was held to 4.5:1.
  Found by hand-verifying sampled findings: MahApps' single reported fail was exactly this.
  Names now fall back to longest-suffix matching against both lists (`MetroProgressBar` →
  `ProgressBar`, `ExtendedTextBlock` → `TextBlock`); exact matches still win.
- **Three false-report mechanisms in the merged palette, caught by hand-verifying findings on
  the public-project scans** (the merged palette shipped earlier in this same release; none of
  this is in any published version):
  1. *Fabricated light column.* Literal-value brushes were treated as theme-neutral, so in a
     `Dark.xaml`/`Light.xaml` literal pair the dark file (sorting first) filled **both** theme
     columns and the light file was ignored entirely — all 456 ScreenToGif findings came out
     with `ratioDark == ratioLight`. QuillNest couldn't catch this: its pair goes through
     `Color` references, which took the correct path; the literal-brush pair shape doesn't
     exist in the four validation projects.
  2. *Neutral files stealing themed values.* Brush maps were filled in file order (the colour
     maps correctly went themed-first — same file, two standards). ScreenToGif's repo contains
     a second app (`Other/Translator`) whose neutral-named light palette sorts before the main
     app's `Dark.xaml`, so the dark column got a white background: `Element.Foreground` on
     `Panel.Background` reported **1.23:1 fail** where the real value is **13.3:1 ok**.
     Brush merging is now themed-first/neutral-fills-holes, and same-key conflicting
     definitions are named in the palette description (the resource-scoping warning the plan
     has required since §4.2) instead of being silently resolved.
  3. *Named colours in palette definitions.* `<Color x:Key="…">White</Color>` (HandyControl's
     dark theme) parsed as nothing — the key's dark value went missing and fell back to the
     light value, pairing dark-on-dark: **1.17:1 fail** where the real value is **13.9:1 ok**.
     Named colours now resolve through the same table the usage side already had.

  Effect on the scans: ScreenToGif fails 254 → 65, HandyControl 248 → 21 — roughly **416
  fabricated failures gone** while coverage held at 19.4%. A tool that floods a first-time
  user with hundreds of confident false fails gets uninstalled, not fixed.

### Changed
- **Palette detection now merges every colour dictionary in the project instead of picking one
  file.** "Pick the file with the most brushes" was a heuristic grown on four validation projects
  that happen to keep their swatches in a single file; it degrades badly everywhere else. Scanning
  eight public WPF projects: HandyControl spreads colours over 8 files with theme-neutral brush
  names in one file referencing theme-specific `<Color>`s in another (and via `DynamicResource`),
  and detection found **no palette at all**; MaterialDesignInXamlToolkit has 154 files carrying
  colour definitions and only 1 was used; MahApps picked `Theme.Template.xaml`, a build-time
  template whose values are `{{placeholders}}`. The new model follows WPF's merged-dictionary
  semantics: colour dictionaries are grouped into dark / light / neutral by path hint, the dark
  and light colour maps are built as neutral-then-override, and every brush key across every
  dictionary resolves against both. A dark-hinted file with no light-hinted counterpart makes the
  neutral files the light theme — HandyControl's shape, where only the dark variant is marked and
  the default `Colors.xaml` carries no `light` in its name. Key conflicts resolve first-wins over
  a fixed (lexicographic) file order, so results are reproducible.

  Coverage across the eight public projects went from **9.6% to 19.2%**, with no project losing
  ground: Playnite +32.7, HandyControl +14.7, wpfui +14.4, MaterialDesign +3.1, MahApps +1.3.
- **The brush and colour parsers no longer assume attribute order or single-line elements.**
  `<SolidColorBrush o:Freeze="True" x:Key="…" …>` (HandyControl) and a `<Color x:Key="…">` whose
  value sits on the next line (MahApps) were both invisible. The parser now matches the element
  and reads attributes out of it. This also recovered 2 keys and 6 real pairs in QuillNest, one
  of the original validation projects — the same defect was costing coverage at home, unnoticed.
- **`DynamicResource` is accepted wherever `StaticResource` was**, and brush→colour references
  now resolve across files (same file first, then project-wide), matching WPF lookup order.
- **Test, sample, demo, example, fixture and mock directories are excluded from palette
  candidacy.** ILSpy's detected "palette" was a decompiler test fixture under
  `ILSpy.BamlDecompiler.Tests.Windows\Cases\`. Those files are still audited; they just no longer
  get to define the project's colours.

### Added
- **A coverage floor: `--min-coverage <0-100>` / `minCoverage`, default 50.** The run now fails
  when fewer than N% of the colour pairs it saw could actually be resolved, and `summary.coverage`
  reports the figure. The zero-pairs guard ("an empty scan is not a pass") was binary, and that
  line turned out not to hold: scanning eight public WPF projects found three that exited **0**
  at 0.2%, 1.7% and 10.2% coverage. HandyControl has 342 XAML files, resolved 7 pairs out of
  2922, and printed `exit 0: all pairs meet AA`. "Barely looked at anything" and "looked at
  everything and it's fine" were indistinguishable in the exit code — the same lie the zero-pairs
  rule exists to prevent, one step up. Sits beside `--strict-palette`, before the mode branches,
  so it applies in `--baseline` mode too. `--min-coverage 0` is the escape hatch; the zero-pairs
  guard stays non-configurable underneath it. The four validation projects run at 89–100% and
  are unaffected.
- **`unresolved` now comes with a reason breakdown**, in the console, the Markdown report and
  `summary.unresolvedBy`: `no-ancestor-background`, `bound-or-gradient`, `unknown-palette-key`,
  `translucent-uncomposited`. A total on its own says how much was missed but not whether
  anything can be done about it — and the split is wildly uneven in practice, so the total
  alone is close to useless for deciding. On ScreenToGif, 1295 of 1373 fell into a single
  bucket; a user staring at "1373" had no way to see that.
- **The document root element now picks up a `Background` from an implicit style**
  (`<Style TargetType="{x:Type Window}">` with no `x:Key`). This is deliberately *only* the
  root element — the "floor" the tree walk lands on — and not general implicit-style
  resolution, which would mean modelling WPF's full resource lookup and would pull inherited
  `Foreground` into every element. Projects that set `Background` directly on their root
  (the four validation projects do, 71–86% of the time) are unaffected; projects that rely on
  an app-level implicit style previously lost every pair in the file.

### Changed
- **`schemaVersion` is now 3** (was 2): `summary.unresolvedBy` added.
- **`schemaVersion` 2 (earlier in this release).** `ok` and `decorative` findings no longer carry a `symmetry`
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
