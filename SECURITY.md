# Security

## What this tool does, and what that means

XamlContrast **reads** XAML and C# files as text and writes a report. It does not load
assemblies, instantiate XAML, execute anything from the project it scans, make network
requests, or read credentials. There is no server, no daemon, and no persistent state
beyond the files you ask it to write.

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

`--json`, `--sarif`, `--md` and `--write-baseline` write exactly where you point them.
Nothing in the scanned project can influence those paths. If you find a way for scanned
content to redirect or inject into an output file, that is a vulnerability — SARIF and
Markdown reports get consumed by other tools and rendered into PR comments, so content
that escapes its encoding there is worth reporting.

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

The latest 0.x release only. The project is pre-1.0 and interfaces are not frozen yet;
fixes ship forward, not as patches to older versions. Pin an exact version and upgrade
deliberately — see [RELEASING.md](RELEASING.md).

## Supply chain

- Published to NuGet via **OIDC Trusted Publishing** — no long-lived API key exists in this
  repository or in GitHub secrets, so there is no publishing credential to steal.
- Builds are deterministic, with SourceLink and symbol packages, so the published binary
  can be traced back to the commit that produced it.
- The version number is derived from the git tag at release time; see
  [RELEASING.md](RELEASING.md).
- Dependencies are watched by Dependabot. The tool itself has no runtime package
  dependencies — the analysis is plain XML parsing on the .NET base class library.
