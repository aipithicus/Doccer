# gh-api: Issues and labels

Assumes [[gh-api]](../SKILL.md) preflight (`$repo`). `--project` attaches to
[[projects]](projects.md). Closing keywords: [[linking]](linking.md). REST
fallbacks when these flags cannot: [[api]](api.md).

```nu
gh issue status -R $repo --json number,title,state,url | from json
(
  gh issue list -R $repo --state all --limit 200
    --json number,title,state,labels,milestone,url,issueType,parent,blockedBy,blocking
  | from json
)
(
  gh issue list -R $repo --state open --limit 200
    --label "area:engine" --milestone "Repository bootstrap" --type Bug
    --json number,title,state,labels,milestone,url
  | from json
)
(
  gh issue view -R $repo 12
    --json number,title,body,state,labels,comments,url,parent,subIssues,blockedBy,blocking,issueType
  | from json
)

let issue_url = (
  gh issue create -R $repo
    --title "Repair project topology"
    --label "area:repository"
    --body-file issue.md
  | str trim
)
(
  gh issue create -R $repo --title "Child task" --parent 12 --type Task
    --body "Tracks the child work for #12."
)
(
  gh issue create -R $repo --title "Restore baseline" --blocked-by 13 --blocking 14
    --body "Restore and verify the repository baseline."
)
(
  gh issue create -R $repo --title "Publish tracked work"
    --project "Aipithicus Development" --body-file issue.md
)

gh issue edit -R $repo 12 --add-label "priority:P1" --milestone "Repository bootstrap"
gh issue edit -R $repo 12 --parent 10
gh issue edit -R $repo 12 --add-sub-issue 20,21
gh issue edit -R $repo 12 --add-blocked-by 13 --add-blocking 14
gh issue edit -R $repo 12 --remove-parent --remove-blocked-by 13 --remove-type
gh issue comment -R $repo 12 --body "Project graph has been repaired."
gh issue close -R $repo 12 --comment "Completed and verified."
gh issue reopen -R $repo 12
```

CLI relationship flags accept issue numbers or URLs. `--parent` /
`--blocked-by` / `--blocking` / `--type` exist on create; edit uses `--parent`,
`--add-sub-issue` / `--remove-sub-issue`, `--add-blocked-by` /
`--remove-blocked-by`, `--add-blocking` / `--remove-blocking`, and `--type` /
`--remove-type`.

```nu
gh issue list -R $repo --state all --limit 200 --json number,title,state,labels
  | from json
  | where state == "OPEN"
```

```nu
gh label list -R $repo --limit 200 --json name,color,description,url | from json
gh label create -R $repo "area:engine" --color "1D76DB"
gh label create -R $repo "type:design" --color "5319E7"
gh label edit -R $repo "bug" --description "Confirmed incorrect behavior"
```

JSON fields (`gh issue list` / `view`): `number`, `title`, `body`, `state`,
`stateReason`, `url`, `id`, `labels`, `milestone`, `assignees`, `comments`,
`issueType`, `parent`, `subIssues`, `subIssuesSummary`, `blockedBy`,
`blocking`, `projectItems`, `createdAt`, `updatedAt`
