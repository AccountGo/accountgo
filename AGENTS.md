# Agent rules

## Commits and authorship

- Do **not** inject an agent, tool, or product name into git commits or pushes.
- Do **not** add `Co-authored-by`, `Signed-off-by`, trailer lines, or similar attribution for Cursor, Copilot, Claude, ChatGPT, or any other agent/assistant.
- Do **not** add agents as repository contributors, co-authors, or commit authors.
- Commit **Author** and **Committer** must remain the human operator’s configured git identity only.
- Commit messages describe the change only — no agent branding, model names, or “generated with …” footers.
- After every `git commit`, verify `git log -1 --format=%B` contains no agent trailer. If the environment auto-appended one, rewrite the commit with `git commit-tree` (or equivalent) so the pushed history stays clean — do not leave `Co-authored-by: Cursor` (or similar) on the branch.
- Do **not** put “Made with Cursor” (or similar) in pull request bodies.

## Changelog

- Every commit with a user-visible change adds a line under `## [Unreleased]` in `docs/CHANGELOG.md`, in the same commit. Use the headings Added, Changed, Fixed, Removed, Security or Self-hosting.
- Skip internal-only commits: tests, CI, refactors, review follow-ups, and fixes for problems never in a released version.
- Write for users, not for the code. Never mention private repositories or customer data. The changelog is public.
- Deploying never cuts a release. Only rename `[Unreleased]` to a version when the owner asks for a release, and follow `docs/RELEASING.md`.
