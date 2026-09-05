---
name: git
description: Use when the user asks to commit or push in this repo. Covers commit conventions and the pre-commit code review against project rules.
---

# Git Workflow

All git operations follow these rules directly — no delegation.

## Branch Model

Everything goes straight to `main`. No feature branches, no PRs.

## Pre-Commit Code Review

Before any commit, review changed files against the project rules in `.claude/rules/`. Check every changed file fully, grouped by file with line numbers. Distinguish **must fix** (convention violations, bugs) from **suggestions** (minor improvements). If everything looks good, say so — don't invent issues.

## Committing

- Only commit when explicitly asked — never proactively
- Stage and commit every change; separate unrelated changes into multiple commits
- Write concise, descriptive commit messages
- Do not include Claude as co-author — no `Co-Authored-By` trailer
- Never force-push or modify commit history unless explicitly asked
