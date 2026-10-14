# Git workflow

How every change reaches this repository. Back to [AGENTS.md](../../AGENTS.md). The owner can
override any of this for a single task by saying so.

## Repository

- `origin` is the private repo `whisperers26/BatteryInfo`. Every `gh pr` command names it with
  `--repo whisperers26/BatteryInfo`.
- `main` is the only long-lived branch. Never commit to it directly, never force-push it.

## Branches

Each feature or fix gets its own branch, cut from an up-to-date `main`, and is pushed as soon as
it exists. When one request holds several independent changes, each gets its own branch and PR.

```bash
git switch main && git pull --ff-only
git switch -c <type>/<description>
git push -u origin HEAD
```

`<type>` and `<description>` follow the owner's global rules: `feature/`, `bugfix/`, `hotfix/`,
`release/`, `chore/`; lowercase words joined by single hyphens, two to five words.

If `git switch -c` says the branch already exists, it is left over from a merged PR: delete it with
`git branch -d` and create it again from `main`.

## Pull requests

1. If `main` moved on, rebase the branch onto it.
2. Run `dotnet build BatteryInfo.slnx` and `dotnet test BatteryInfo.slnx`.
3. Push and open the PR. The description says what changed, why, how it was verified (including
   live device output for provider changes), and lists any bug found but not fixed; add those to
   [known-issues.md](known-issues.md) too.

   ```bash
   gh pr create --repo whisperers26/BatteryInfo --base main --title "<what changed>" --body-file <file>
   ```

4. Wait for the CI check (`.github/workflows/ci.yml`: Windows build + test) to pass.
5. Merge with a merge commit. Never squash: squashing collapses the small commits into one.

   ```bash
   gh pr merge <number> --repo whisperers26/BatteryInfo --merge --delete-branch
   git switch main && git pull --ff-only && git branch -d <branch>
   ```

6. If the owner explicitly asks to review a PR, open it and stop. Do not merge.
