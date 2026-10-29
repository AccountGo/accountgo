# Third-party agent skills

These skills are copied unchanged from [anthropics/knowledge-work-plugins](https://github.com/anthropics/knowledge-work-plugins) at commit `2574e29377c7173d0cecffef29a6ecbd7f8ccb5d`. They are licensed under the Apache License 2.0 (see `LICENSE-knowledge-work-plugins.txt`), not under this repository's AGPL-3.0-only license.

| Skill | Upstream path | Used for |
|-------|---------------|----------|
| `reconciliation` | `finance/skills/reconciliation` | Bank and GL-to-subledger reconciliation, reconciling-item categories, aging |
| `close-management` | `finance/skills/close-management` | Month-end close checklist, sequencing, period lock |
| `financial-statements` | `finance/skills/financial-statements` | Income statement, balance sheet and cash flow presentation and variance review |
| `journal-entry-prep` | `finance/skills/journal-entry-prep` | Journal entry types, documentation and review checklist |
| `write-spec` | `product-management/skills/write-spec` | Feature specs and PRDs: goals, non-goals, user stories, P0/P1/P2 requirements |

Some upstream skills link to a `CONNECTORS.md` that is not copied here; those links can be ignored.

To update, copy the `SKILL.md` files from a newer upstream commit and change the commit above.
