# gh-api: REST and GraphQL

Assumes [[gh-api]](../SKILL.md) preflight (`$repo`). Prefer typed
[[issues]](issues.md) commands; use REST only when those cannot perform the
operation.

Default method is `GET`; any `-f` / `-F` switches to `POST` unless `-X` is set.
REST fields named `sub_issue_id` or `issue_id` require database IDs, not issue
numbers.

```nu
gh api $"repos/($repo)/issues/12" --jq '{number,id,node_id}'
```

```nu
(
  gh api $"repos/($repo)/issues/12/sub_issues" --paginate --slurp
  | from json
  | flatten
)
gh api $"repos/($repo)/issues/12/parent" | from json
gh api $"repos/($repo)/issues/12/sub_issues" -X POST -F sub_issue_id=1234567890
gh api $"repos/($repo)/issues/12/sub_issue" -X DELETE -F sub_issue_id=1234567890

(
  gh api $"repos/($repo)/issues/12/dependencies/blocked_by" --paginate --slurp
  | from json
  | flatten
)
(
  gh api $"repos/($repo)/issues/12/dependencies/blocking" --paginate --slurp
  | from json
  | flatten
)
gh api $"repos/($repo)/issues/12/dependencies/blocked_by" -X POST -F issue_id=1234567890
```

GraphQL only if REST cannot. Variables via `-F`; `query` via `-f`:

```nu
let repo_parts = ($repo | split row "/")
(
  gh api graphql
    -F $"owner=($repo_parts.0)"
    -F $"name=($repo_parts.1)"
    -f query="
    query($owner: String!, $name: String!) {
      repository(owner: $owner, name: $name) { nameWithOwner }
    }
  "
  | from json
)
```
