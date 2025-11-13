# Jira To GitHub Issues

(c) 2025 Beem Media. All rights reserved.

Pushes issues that have been exported from Jira in CSV format to GitHub issues.

This is an ad-hoc solution created specifically for Beem Media's needs, but might be a good reference for anyone wanting to move issues from Jira to GitHub. The basic usage is:

```
JiraToGitHubIssues.exe -in "$InputFile" -repo "$RepoUrl" [-t "$TempDir"]

$InputFile: Path to CSV file that has been exported from Jira.
$RepoUrl: Either the partial or full URL the the repro that should be pushed to.
$TempDir (optional): The directory where all temporary files will be written, if not specified the systems temp directory will be used.
```

You will need GitHub CLI (gh) in that PATH. You should authorize GitHub CLI prior to usage.

Since this is an ad-hoc solution it's not meant to be robust. If the process fails, it can't be resumed and everything already pushed to GitHub will be there, possibly in an incomplete state (comments missing, not close, e.g.).

Note that I've had problemsm with bulk imports and I think that GitHub might have some kind of limit to how many issues you can create in a given time period, so watch for failures. Since there is no recovery the only option is to modify the CSV and delete any rows that were already imported and delete any partial issue creations (issues where comments weren't added or weren't closed, e.g.) from GitHub, then try again later.
