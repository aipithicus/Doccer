# gh-api: Link git work

Assumes [[gh-api]](../SKILL.md) preflight (`$repo`). Issue numbers come from
[[issues]](issues.md).

Direct-to-`main`:

```nu
git commit -m "Repair project ownership" -m "Refs #12"
git commit -m "Repair project ownership" -m "Fixes #12"   # closes when it reaches default branch
```

Optional linked branch: `gh issue develop -R $repo 12 --checkout`.

For a contributed PR, pass explicit head and base branches so `gh` does not
offer an interactive push or fork flow:

```nu
let head_branch = (git branch --show-current | str trim)
(
  gh pr create -R $repo --base main --head $head_branch
    --title "Repair project ownership" --body "Closes #12"
)
```

GitHub closing keywords in commit messages and PR bodies: `Fixes` / `Closes` /
`Resolves` `#N`. `Refs #N` links without closing.
