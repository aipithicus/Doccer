# gh-api: Projects

Assumes [[gh-api]](../SKILL.md) preflight (`$repo`, `$expected_login`). Items
are [[issues]](issues.md) (or PRs) addressed by URL.

Projects require the `project` scope. The verified API account remains the
authority; do not assume `@me` names the intended owner.

```nu
let project_owner = $expected_login

gh project list --owner $project_owner --limit 100 --format json | from json
gh project create --owner $project_owner --title "Aipithicus Development" --format json | from json
gh project link 1 --owner $project_owner --repo $repo
gh project field-list 1 --owner $project_owner --limit 100 --format json | from json
gh project item-list 1 --owner $project_owner --limit 200 --format json | from json
(
  gh project item-add 1 --owner $project_owner
    --url $"https://github.com/($repo)/issues/12"
    --format json
  | from json
)
```

If the scope is missing, stop and ask the user to run
`gh auth refresh --hostname github.com -s project`.
