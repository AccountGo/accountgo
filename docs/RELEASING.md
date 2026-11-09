# Releasing AccountGo

Deploying and releasing are separate steps.

- **Deploying** puts a commit from `main` into production. It can happen many times a day and announces nothing.
- **Releasing** is a decision to announce a version. It tags a commit that is already in production, names the changes collected since the last release, and publishes release notes.

## Day to day: keep `[Unreleased]` current

Every commit with a user-visible change adds a line under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md), in the same commit. Waiting until release time means changes get forgotten.

**What counts as user-visible:** anything a user of the hosted app or a self-hoster would notice. That includes features, behaviour changes, bug fixes that users hit, security changes, and new configuration or migrations.

**What to skip:** tests, CI, refactors, review follow-ups, and fixes for problems that were never in a released version.

Group the lines under these headings, and add only the ones you need:

| Heading | Use for |
|---|---|
| `### Added` | New features |
| `### Changed` | Changes to existing behaviour |
| `### Fixed` | Bug fixes |
| `### Removed` | Features taken out |
| `### Security` | Security fixes and hardening |
| `### Self-hosting` | New or renamed environment variables, database migrations, or anything else a self-hoster must do when upgrading |

Write for the person using AccountGo, not for the code. For example:

- Good: "Members are signed out after a period of inactivity set per workspace."
- Not: "Add IdleTimeoutMiddleware and session policy endpoint."

The changelog is public. Don't mention private repositories, internal tooling, or customer data.

## Choosing a version

AccountGo follows [Semantic Versioning](https://semver.org/). Before 1.0:

- **`0.x.Y` (patch):** fixes only.
- **`0.X.0` (minor):** new features, behaviour changes, or anything that needs action from self-hosters.
- **`1.0.0`:** when we commit to stable behaviour and upgrade paths.

## Release checklist

1. **Decide to release.** Pick the version using the rules above.
2. **Cross-check the changelog.** Run `git log v<previous>..HEAD --format='%h %s'` and compare it with `[Unreleased]`. Add anything user-visible that's missing.
3. **Cut the section.** Rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD`. Add a link line to the release notes. Put a new, empty `## [Unreleased]` above it.
4. **Bump the web version.** Set `"version"` in `src/Web/package.json` to `X.Y.Z`.
5. **Commit and push** as "Release vX.Y.Z". It changes only docs and the version, so it ships no new code.
6. **Wait for CI** on that exact commit.
7. **Confirm production matches.** The production API and web app must already be running the code being tagged. Compare the `version` field of `https://api.accountgo.dev/health` and the latest web deploy with `git log`. If production is behind, deploy first, verify it, then tag.
8. **Tag and publish.**
   ```bash
   gh release create vX.Y.Z --target <release-commit-sha> \
     --title "AccountGo vX.Y.Z: <headline>" --notes-file <notes.md> --latest
   ```
9. **Announce.** Build social posts from the release notes, not from the changelog.

## Changelog vs release notes

| | Changelog (`docs/CHANGELOG.md`) | Release notes (GitHub release) |
|---|---|---|
| Audience | Anyone checking what changed | People deciding whether to try or upgrade |
| Content | Every user-visible change, one line each | Two to five highlights, why they matter, and upgrade steps |
| Tone | Terse and factual | Readable and inviting, but every claim must be true |
| Lifetime | Permanent and cumulative | One per release |

Release notes link to the matching changelog section for the full list. Before publishing, check every claim against the code and the repository: features, setup steps, labels, and roadmap issues.
