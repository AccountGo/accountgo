# Agent rules

## Commits and authorship

- Do **not** inject an agent, tool, or product name into git commits or pushes.
- Do **not** add `Co-authored-by`, `Signed-off-by`, trailer lines, or similar attribution for Cursor, Copilot, Claude, ChatGPT, or any other agent/assistant.
- Do **not** add agents as repository contributors, co-authors, or commit authors.
- Commit **Author** and **Committer** must remain the human operator’s configured git identity only.
- Commit messages describe the change only — no agent branding, model names, or “generated with …” footers.
- After every `git commit`, verify `git log -1 --format=%B` contains no agent trailer. If the environment auto-appended one, rewrite the commit with `git commit-tree` (or equivalent) so the pushed history stays clean — do not leave `Co-authored-by: Cursor` (or similar) on the branch.
- Do **not** put “Made with Cursor” (or similar) in pull request bodies.
