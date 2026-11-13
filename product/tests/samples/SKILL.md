---
name: release-notes
description: Draft release notes from merged pull requests since the last tag.
allowed-tools: Bash(git log:*), Read
---

# Release notes skill

Use this skill when the user asks for release notes or a changelog.

## Steps

1. Find the last tag: `git describe --tags --abbrev=0`.
2. List merged pull requests since that tag.
3. Group them by **Features**, **Fixes** and **Chores**.

> [!TIP]
> Keep each line under 80 characters.
