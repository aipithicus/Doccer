---
name: gh-api
description: >
  Use GitHub CLI and REST/GraphQL APIs for noninteractive, identity-checked
  issue tracking in a specific repository: list, create, edit, or close issues;
  manage labels, apply milestones, manage Projects, sub-issues, and dependencies; and link
  commits or PRs to issues. Do not use for Git transport or credential setup,
  release management, or general PR review.
---

Treat the Git commit author, SSH remote identity, active `gh` API account, and
any GitHub MCP account as separate identities. SSH routing does not prove the
identity used by `gh` or an MCP.

# Github MCP 

A connected GitHub MCP has a separate credential boundary. Use it only when its
account and repository can be verified as the intended target. 

# gh-api

```
git              commits, branches, tags, remotes
gh <noun>        issues, labels, Projects, milestone application, and PR links
gh api           REST fallback with an explicit repository path
gh api graphql   only when REST cannot
```


Do not use `--show-token`. Do not run `gh auth login`, `gh auth switch`,
`gh auth refresh`, `gh repo set-default`, or `git config` as part of an
operational task. If authentication, scope, or identity is wrong, stop and tell
the user the exact user-run remediation needed.

otherwise use
the verified `gh` chain.

## Execution rules

- Never invoke an interactive `gh` flow. Supply titles, bodies, repository,
  owner, and output fields explicitly.
- A read or planning request does not authorize a GitHub mutation. Perform only
  the write the user requested against the verified target.
- Pass `-R $repo` to repository-scoped `gh` commands. Pass an explicit owner to
  Project commands ([[projects]](resources/projects.md)).
- For `gh` noun reads, request explicit `--json` fields and pipe through
  `from json`. Project reads use `--format json` instead.
- Raw `gh api` responses are JSON and can use `from json`. After `--jq`, output
  may be plain text; parse it only when the selected value is JSON.
- Give every list an intentional `--limit`. Use `gh api --paginate` for REST
  pagination ([[api]](resources/api.md)). State the bound when it matters; never
  infer completeness from a default-sized result.
- Capture the identifier or URL returned by a creation. After an ambiguous
  failure, re-read the target before retrying; do not blindly replay a mutation.
- After a write, read back the affected object and report its number or URL.

## Topics

Load only the file needed. Topics assume the preflight variables (`$repo`,
`$expected_login`).

- [[issues]](resources/issues.md): list, view, create, edit, close; labels; relationship flags; JSON fields
- [[linking]](resources/linking.md): commit/PR closing keywords; contributed PRs
- [[projects]](resources/projects.md): list, create, link, fields, items; `project` scope
- [[api]](resources/api.md): REST/GraphQL fallbacks; database ids vs issue numbers
