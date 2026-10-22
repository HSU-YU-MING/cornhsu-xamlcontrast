# Contributing

This project is maintained by one person. The most useful thing you can send is not a
pull request — it is **a report from a codebase I have never seen**.

Here is why. Every one of the seventeen parsing rules exists because a real project proved
one was missing: the tool either invented a failure that wasn't there, or stayed quiet about
one that was. None were designed at a whiteboard. That method has a built-in weakness, and
it bit this project already — the first four validation projects share one author and one
house style, so two whole classes of bug cost *nothing* on them and only surfaced when eight
unrelated public projects were scanned. **A blind spot's severity cannot be measured on a
sample that never triggers it.**

Your project is a sample I don't have.

## The most valuable reports

**A false alarm** — the tool says text fails, and it doesn't. This is the highest-value
report there is. A tool people stop believing is a tool people turn off, so a false alarm
is a more serious defect here than a missed one.

**A miss** — text that is genuinely unreadable and the tool said nothing.

**Coverage that collapses** — the tool resolved so little of your project that the result is
meaningless. Sometimes this is one config line away from fixed; sometimes it is a real
boundary of static analysis. Either way I want to know which.

Use the [issue templates](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/issues/new/choose)
— they ask for exactly what I need to reproduce, which saves both of us three rounds of
questions. **Write in English or 中文, whichever is easier for you.**

**One exception: security.** If you found something an attacker could use — the XML parser
resolving an external entity, a crafted file reaching outside the scanned root, content
escaping its encoding in a SARIF or Markdown report — report it privately instead.
See [SECURITY.md](SECURITY.md). A wrong contrast verdict is not that, however badly it
behaves; those belong in the open.

### Before you open one

Check [Known limitations](README.md#known-limitations). Some gaps are deliberate and
documented — text over a sibling image, `VisualStateManager` colour animations, cross-element
correlated triggers, `TargetName` setters aimed at inner template parts. If your case is one
of those, an issue is still welcome (it tells me the gap hurts in practice, which is how
things get reprioritised) — just say so, so we skip the diagnosis.

### What makes a report reproducible

The templates ask for these, but the short version:

- **The finding line exactly as printed.** It carries the file, line, both colours, both
  ratios and the symmetry verdict — that one line usually tells me where the logic went wrong.
- **The smallest XAML that reproduces it.** Strip everything unrelated. If you can't share
  the source, rename the resource keys and change the hex values; the *shape* is what matters.
- **The first line of the report** (`palette: ...`). It names which of the six detection paths
  ran, and a surprising number of problems are really palette-detection problems.
- **The tool version** (`xamlcontrast --version`).

## If you want to send code

Rules come from evidence. **A pull request that adds a parsing rule needs the real-world
case that proves the rule is missing** — a repository, a file, a finding. "XAML can also do
X, so we should handle X" is not enough on its own; the whole point of the method is that
speculation gets filtered out.

Two things every change needs:

- **A test.** New rule → a test for the shape it resolves. Bug fix → a test that fails
  before your change.
- **A regression test if it touches what gets excluded.** During development this tool
  produced eight reports that looked healthy and were wrong — every one is now a test.
  Anything that lets a pair be exempted, suppressed, skipped or filtered gets one too.
  The project's core rule: **an audit tool's worst failure is not missing issues, it is
  false confidence.** Ask of any new "this one is fine" logic: could it make a real problem
  disappear from the report?

### Running the checks

```bash
dotnet format XamlContrast.slnx --verify-no-changes
dotnet test XamlContrast.slnx -c Release
```

```bash
pwsh scripts/verify-readme-sample.ps1
```

The format check is its own CI job: a formatting difference turns the PR red even when every
test passes. Drop `--verify-no-changes` to have it fix the formatting for you.

The script re-runs `samples/demo` and diffs it against the sample output in both READMEs.
If you changed console output, run it with `-Update` to re-paste, and include that in your PR.

There is one more script, `scripts/verify-baselines.ps1`. **You cannot run it** — it audits
four closed-source applications on the maintainer's machine and compares against frozen
snapshots. It runs before every release. If your change moves those numbers I will see it
there, so don't be surprised if I come back with questions about a project you can't see.

CI runs the tests on Linux, Windows and macOS, checks formatting (`dotnet format`), packs
the tool and installs it, and audits the deliberately-broken demo project expecting exit 1.

### What won't be merged

- **A version bump.** The git tag is the only source of truth; the csproj field stays
  `0.0.0-dev` on purpose. See [RELEASING.md](RELEASING.md).
- **Guessing a colour the tool cannot know.** `{Binding}` and `{TemplateBinding}` values
  exist only at runtime. They are reported as unresolved and counted, never inferred.
  Trading honest uncertainty for a plausible number is the one thing this tool must not do.
- **Quieter output.** The degradation counters (exemptions, suppressions, unresolved,
  coverage, parse failures) are loud deliberately. A tidier report that hides what wasn't
  looked at is a worse report.

## Scope

XamlContrast audits **colour contrast in XAML source**. Other accessibility checks —
screen reader names, keyboard order, focus visuals — are real problems and share almost no
machinery with this one; they belong in a different tool.

Platforms beyond WPF (WinUI 3, Uno, Avalonia) are on the roadmap and gated on the same rule
as everything else: **there has to be a real project to validate against.** If you maintain
one and would be willing to be the guinea pig, say so in an issue — that unblocks it.

## Interfaces and versions

1.0 froze the public interface. Nine surfaces change only by addition within 1.x:

1. CLI flags (names, value forms, defaults)
2. exit codes (`0` pass, `1` below the gate, `2` usage/config/input error, `3` result can't be trusted) and the order they are decided in
3. the JSON report (`schemaVersion` 6, [`docs/report.schema.json`](docs/report.schema.json))
4. the Action inputs
5. the config file
6. the baseline file, including how its keys are spelled
7. the SARIF `ruleId` (`wcag-contrast`) and levels
8. the `xamlcontrast-ignore` comment syntax
9. which files get audited

The full declaration and the evolution rules are in the
[CHANGELOG's 1.0.0 entry](CHANGELOG.md). What that means for a pull request:

- **Additions are minor and welcome** — a new flag, Action input, config property, JSON field, or a
  new value in an open set (an `unresolved` reason, a palette mode). Adding a JSON field does **not**
  change `schemaVersion`; consumers are required to ignore fields they don't know.
- **Renaming, removing, changing a default, or tightening what is accepted** on any of the nine
  surfaces is a 2.0 change, however small it looks. It waits.
- **A change to audit semantics that moves counts** — more pairs found, a confident ratio turned into
  an honest `unresolved` — is a minor release, never a patch. It needs an *Upgrading* note at the
  top of the CHANGELOG entry with the measured effect, and has to say so if baseline keys move.

## License

Contributions are under [MIT](LICENSE), same as the project.
