# Security

## What this tool does, and what that means

XamlContrast **reads** XAML and C# files as text and writes a report. The audit does not load
assemblies, instantiate XAML, execute anything from the project it scans, or read credentials.
There is no server and no daemon.

It makes **one outgoing network request by default**: after an audit that reaches a verdict, the
anonymous usage statistics — counts and ratio ranges only, nothing from the project's files, sent to
`https://stats.cornhsu.com/v1/xamlcontrast` — see *Anonymous usage statistics* below, including how to
turn it off (`DO_NOT_TRACK=1` is enough). Its only persistent state beyond the files you ask it to write
is the marker file that records the statistics notice was shown on a machine.

The other request is the experimental `--push` mode, and only when you invoke it: it does not
audit; it reads the `XAMLCONTRAST_TOKEN` environment variable and sends an existing report file,
in one request, to the server you name — see *`--push`* below.

That narrows the realistic threat surface to one thing: **it parses input it does not
trust.** People run it in CI, on pull requests, on branches pushed by strangers. A crafted
`.xaml` file is attacker-controlled input reaching an XML parser that runs with the
permissions of your CI job.

### XML external entities

Blocked. XAML is parsed with `XDocument.Load` / `XmlReader.Create` on .NET, where DTD
processing is prohibited and the resolver is null by default. A file declaring an external
entity is refused:

```
!! parse failed: Evil.xaml: External entity 'xxe' reference cannot appear in the attribute value.
```

Note that the refusal is **loud** — the file is named, and it is counted in
`summary.parseErrors`. That matters as much as the block itself: a scan that silently
skipped a file would report health it hadn't verified.

If you find a way to make the parser resolve an external entity, read a file outside the
scanned root, or hang on a crafted input, that is a vulnerability. Please report it.

### File writes

`--json`, `--sarif`, `--md`, `--html` and `--write-baseline` write exactly where you point them.
Nothing in the scanned project can influence those paths. If you find a way for scanned
content to redirect or inject into an output file, that is a vulnerability — SARIF and
Markdown reports get consumed by other tools and rendered into PR comments, so content
that escapes its encoding there is worth reporting.

The HTML report is opened in a browser straight from a CI artifact, so it gets the same care: every
piece of text from the scanned project is escaped (control and bidirectional-override characters are
shown as `\uXXXX`), none of it is placed in an attribute, the only inline styles are validated
`#RRGGBB` colours, and a Content-Security-Policy forbids scripts and every external resource. Content
from a scanned project that runs script, loads anything, or changes the page's structure is a
vulnerability.

### Anonymous usage statistics

- One `POST https://stats.cornhsu.com/v1/xamlcontrast` per audit that reaches a verdict (exit 0, 1 or 3);
  none for `--push`, `--help`, `--version` or exit 2. The body is a small JSON document of counts and
  ranges — the tool version, OS, whether it runs in CI, the verdict and gate, the palette mode, file and
  pair counts, coverage, counts per category and per unresolved reason, and the worst ratios in six
  ranges — plus a project ID only if you put one in the config. `--stats-preview` prints the exact body.
- Never sent: file names or paths, element, style or resource names, colour values, report contents,
  credentials, environment variables, or repository, user or machine names. No User-Agent, cookies or
  custom headers.
- Off with any one of `DO_NOT_TRACK=1`, `XAMLCONTRAST_SHARE_STATS=false`, `CORNHSU_SHARE_STATS=false`,
  `"stats": { "share": false }` in the config, or `share-stats: false` in the Action. Builds from source
  and other development builds (any version starting with `0.0.0-`) never send.
- At most 3 seconds (1.5 to connect), no retries, no redirects; a failure is ignored silently and never
  changes the exit code or any output. Notices go to stderr (and, on GitHub Actions, one line in the job
  summary); the marker file is `cornhsu/xamlcontrast-stats-notice` in the user config folder.

If you find a way for anything from the scanned project to reach the statistics request, for the
request to go anywhere but the collector (or the `CORNHSU_STATS_URL` you set), or for it to be sent
when one of the switches above says not to, that is a vulnerability.

### `--push` (experimental)

- It contacts only the server you name — `--server`, else `XAMLCONTRAST_SERVER` — with one
  `POST {server}/api/runs`. Redirects are not followed, so the token is never forwarded to another
  host.
- The token comes from `XAMLCONTRAST_TOKEN` only (there is no flag, so it never lands in shell
  history or a CI log), travels only in the `Authorization` header, and is never printed.
- The body is the report file unchanged; nothing else from the machine is read or sent.
- The server's reply is untrusted input: whatever of it reaches the console or the GitHub job summary
  is escaped like scanned content, so it cannot start a workflow command.
- Over plain `http` the token travels unencrypted — use `https` for anything but a local server.

If you find a way for `--push` to send the token or the report anywhere other than the server you
named, or for a server reply to inject a workflow command, that is a vulnerability.

### What is *not* a security issue

A wrong contrast verdict — a false alarm or a miss — is a correctness bug, not a
vulnerability, however badly it behaves. Please use the
[issue templates](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/issues/new/choose)
for those; they are the reports this project most wants, and they belong in the open.

## Reporting a vulnerability

Use GitHub's private reporting: **Security → Report a vulnerability** on this repository.
That opens a channel only you and the maintainer can see.

Please don't open a public issue for a vulnerability. Everything else in this project is
discussed in the open, but a working exploit against a tool people run in CI deserves a
fix released before it is described publicly.

This is a single-maintainer project, so expect a human response in days rather than hours.
Tell me what you found, how to reproduce it, and what it lets an attacker do; if you have a
suggested fix, even better. I will credit you in the release notes unless you'd rather I
didn't.

## Supported versions

| Version | Supported |
|---|---|
| The latest `1.x` release | ✅ |
| `0.x` and anything older | ❌ |

Fixes land on the newest release only — there are no patch branches for older versions, and no
further 0.x releases. Upgrading to the newest `1.x` is always the fix.

What a 1.x upgrade keeps and what it may change (the declaration and the 1.x rules are in the
CHANGELOG's 1.0.0 entry):

- **Frozen syntax** — CLI flags and exit codes, the JSON report's fields, the Action inputs, the config
  file, the baseline file format, the SARIF rule, the suppression comment and the hard audit-scope
  rules change only by addition until 2.0. The experimental `--push` mode is not covered until the
  dashboard it talks to is publicly deployed.
- **Audit semantics may change in a minor release** — more pairs found, different counts, a build that
  turns red. Each such release says so in the *Upgrading* note at the top of its CHANGELOG entry.
- **Baselines** — if a release changes how baseline keys are spelled, it bumps `keyVersion`: an older
  baseline is then refused (exit 2) with a note to regenerate it, rather than reporting every known pair
  as new.

Coming from 0.x, work through the 1.0.0 entry's **Upgrading from 0.6.1** list first.

If you reference the Action as `@v1`, you receive new 1.x releases automatically — and only 1.x: with
`version` left empty it installs the newest 1.x tool, never 2.0. Pin `version:` to an exact release to
upgrade on your own schedule.

## Supply chain

- Published to NuGet via **OIDC Trusted Publishing** — no long-lived API key exists in this
  repository or in GitHub secrets, so there is no publishing credential to steal.
- Builds are deterministic, with SourceLink and symbol packages, so the published binary
  can be traced back to the commit that produced it.
- The version number is derived from the git tag at release time; see
  [RELEASING.md](RELEASING.md).
- Dependencies are watched by Dependabot. The tool itself has no runtime package
  dependencies — the analysis is plain XML parsing on the .NET base class library.
