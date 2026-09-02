
## Identity and target preflight

Before a GitHub operation, verify the API login and exact repository. For the
Doccer pilot:

```nu
let expected_login = "aipithicus"
let repo = "aipithicus/Doccer"

gh auth status --active --hostname github.com
let actual_login = (gh api user --jq .login | str trim)
if ($actual_login | str downcase) != ($expected_login | str downcase) {
  error make {msg: $"Refusing GitHub operation: expected ($expected_login), got ($actual_login)"}
}
gh repo view $repo --json nameWithOwner,url | from json
```