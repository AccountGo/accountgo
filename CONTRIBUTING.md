# Contributing to AccountGo

Thank you for your interest in contributing to **AccountGo**! Whether you are reporting a bug, proposing a new feature, improving documentation, or writing code, your contributions help build a world-class, open-source accounting and ERP platform.

Please take a few moments to review these guidelines before getting started.

---

## Code of Conduct

All contributors and participants are expected to adhere to the [Code of Conduct](CODE_OF_CONDUCT.md). Please report any violations or inappropriate behavior directly to the project owner at [hello@marvinperez.com](mailto:hello@marvinperez.com).

---

## How to Contribute

### 1. Reporting Bugs

Before creating a bug report, please check existing [GitHub Issues](https://github.com/AccountGo/accountgo/issues) to avoid duplicates.

If you find a new bug:
- Open an issue using the [Bug Report Template](.github/ISSUE_TEMPLATE/bug_report.md).
- Provide a clear, descriptive title.
- Include step-by-step instructions to reproduce the issue.
- Describe expected behavior vs. actual behavior.
- Include relevant error logs, screenshots, and environment details (.NET version, Node.js version, browser, OS).

> [!NOTE]
> If you discover a security vulnerability, do NOT open a public issue. Follow our [Security Policy](SECURITY.md) instead.

### 2. Suggesting Features & Enhancements

Have an idea for AccountGo?
- Check the [Issue Tracker](https://github.com/AccountGo/accountgo/issues) and existing discussions.
- Submit a proposal using the [Feature Request Template](.github/ISSUE_TEMPLATE/feature_request.md).
- Clearly explain the problem the feature solves, target use cases, and proposed design or API changes.

### 3. Submitting Code Contributions

1. **Find or open an issue**: Before writing significant code, please link or open an issue so maintainers can discuss and approve the approach.
2. **Fork and branch**:
   - Create feature branches originating from `main`.
   - Use descriptive branch naming conventions:
     - `feat/feature-name` (e.g., `feat/recurring-invoices`)
     - `fix/bug-description` (e.g., `fix/tax-rounding-error`)
     - `docs/doc-updates` (e.g., `docs/api-reference`)
     - `refactor/clean-architecture`
3. **Set up local development**: See [Development Setup](#development-setup) below and [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).
4. **Follow commit conventions**: Follow [Conventional Commits](https://www.conventionalcommits.org/).
5. **Open a Pull Request**: Submit your pull request against the `main` branch using the [Pull Request Template](.github/PULL_REQUEST_TEMPLATE.md).

---

## Development Setup

### Prerequisites

- **.NET SDK**: .NET 10.0+ (or latest supported SDK)
- **Node.js**: v22+
- **Package Manager**: `pnpm` (v9+)
- **Git**
- **Docker** (optional, for containerized local services or MSSQL)

### Quick Start

```bash
# 1. Clone repository
git clone https://github.com/AccountGo/accountgo.git
cd accountgo

# 2. Build .NET Backend Solution
dotnet restore
dotnet build

# 3. Start Backend API (defaults to local SQLite and http://localhost:8001)
dotnet run --project src/Api/Api.csproj --urls "http://localhost:8001"

# 4. In a separate terminal, install and run Frontend Web
cd src/Web
pnpm install
pnpm dev
# Open http://localhost:3000 in your browser
```

For more in-depth development setup and debugging guidance, see [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

---

## Branching Strategy & Workflow

1. Always branch off the latest `main` branch:
   ```bash
   git checkout main
   git pull origin main
   git checkout -b feat/your-feature-name
   ```
2. Keep your branch focused on a single responsibility.
3. Regularly rebase your branch against upstream `main` to avoid merge conflicts:
   ```bash
   git fetch origin
   git rebase origin/main
   ```

---

## Commit Message Conventions

We adhere to the [Conventional Commits v1.0.0](https://www.conventionalcommits.org/) specification:

```
<type>(<scope>): <short description>

[optional body]

[optional footer(s)]
```

### Allowed Types

- `feat`: A new feature for the user or system
- `fix`: A bug fix
- `docs`: Documentation changes only
- `style`: Formatting, missing semicolons, etc. (no production code changes)
- `refactor`: Code change that neither fixes a bug nor adds a feature
- `perf`: Code change that improves performance
- `test`: Adding missing tests or correcting existing tests
- `build`: Changes that affect the build system or external dependencies
- `ci`: Changes to CI/CD configuration files and scripts
- `chore`: Other non-src or non-test changes

### Examples

- `feat(sales): add recurring invoice scheduling support`
- `fix(auth): resolve token refresh loop on mobile browsers`
- `docs(api): document multi-tenancy header resolution`

---

## Coding Standards & Style

### Backend (.NET / C#)
- Follow Microsoft's official [.NET C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).
- Adhere to Clean Architecture / Domain-Driven Design principles.
- Use explicit type declarations or sensible `var` where type is evident.
- Keep domain entities isolated from persistence and presentation frameworks.
- Ensure all nullable reference types are properly handled (`#nullable enable`).

### Frontend (React / TypeScript / Tailwind CSS)
- Use functional React components with React 19 hooks.
- Strict TypeScript: avoid `any`; declare precise interface definitions and types.
- Follow Tailwind CSS utility-first styling; use project design tokens (`[var(--bg-surface)]`, `[var(--brand-primary)]`).
- Clean separation between presentation UI and API communication services.

---

## Testing Requirements

Before submitting a pull request, ensure all tests pass:

```bash
# Run all automated tests across the solution
dotnet test

# Validate Web frontend build and TypeScript types
cd src/Web
pnpm build
```

- When adding new business logic or API endpoints, include unit tests under `test/`.
- Ensure tests are deterministic and independent.
- Do not introduce mock data into production pathways.

---

## Pull Request Process & Review

1. **Self-Review**: Review your own diff before submitting. Verify there are no leftover debug statements or unintentional changes.
2. **PR Template**: Complete all sections of the [Pull Request Template](.github/PULL_REQUEST_TEMPLATE.md), including the checklist.
3. **CI Checks**: Ensure all automated CI workflows (build, test, lint) pass.
4. **Code Review**: At least one maintainer review is required. Address reviewer feedback constructively and push follow-up commits to your branch.
5. **Merge**: Once approved and checks pass, maintainers will squash-and-merge your contribution into `main`.

Thank you for helping make AccountGo better for everyone!
