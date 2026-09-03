# Notes for coding agents

Euclid.BVH is an F# library. It targets .NET (`net6.0` and `net472`) and also compiles to
JavaScript and TypeScript with Fable, so everything has to stay Fable compatible.

## CHANGELOG.md must not use wrapped bullets

`Euclid.BVH.fsproj` derives the package version from `CHANGELOG.md` with
`Ionide.KeepAChangelog.Tasks` 0.3.3. Its parser throws
`ArgumentOutOfRangeException` on a bullet that wraps onto an indented
continuation line, and that failure aborts the whole build:

```
error MSB4018: The "Ionide.KeepAChangelog.Tasks.ParseChangeLogs" task failed unexpectedly.
error MSB4018: System.ArgumentOutOfRangeException: Index was out of range.
```

So `dotnet build` fails on a changelog that renders perfectly well on GitHub. Keep
**one bullet on one line**, however long, and use nested `- ` sub-bullets for structure.

Bad, the build fails:

```markdown
- Tree building allocates about 8 times less and is about 2.5 times faster:
  the median split now uses an in place quickselect,
  and the node array is allocated at its exact size.
```

Good:

```markdown
- Tree building allocates about 8 times less and is about 2.5 times faster:
  - the median split now uses an in place quickselect,
  - and the node array is allocated at its exact size.
```

Also good, a single long line:

```markdown
- Tree building allocates about 8 times less and is about 2.5 times faster on big inputs.
```

What was tested against the parser:

| Shape | Result |
| --- | --- |
| One bullet on one line, any length | works |
| Nested `- ` sub-bullets, any depth | works |
| Blank lines between bullets, fenced code blocks | works |
| A bullet wrapping onto a line indented by 2 or more spaces | **breaks the build** |
| The same under a nested sub-bullet | **breaks the build** |

It breaks in released sections just as much as in `## [Unreleased]`, despite the stack
trace naming `parseUnreleasedText`. A continuation line indented by exactly one space
happens to survive, but do not rely on that.

Before pushing a changelog edit, check that no line starts with whitespace that is not a
list marker, and build once:

```bash
grep -nE '^[[:space:]]+[^-[:space:]]' CHANGELOG.md   # should print nothing
dotnet build
```

The `grep` is a heuristic: it would also flag indented lines inside a fenced code block,
which the parser is fine with. The build is the real check.

If `Ionide.KeepAChangelog.Tasks` is ever upgraded past 0.3.3, re-test whether wrapped
bullets are accepted and this note can go.
