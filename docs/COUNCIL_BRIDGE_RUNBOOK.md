# TPPA Council Bridge Supplement

Generic bridge invocation, model selection, file limits, troubleshooting, and
repair are governed by:

```text
C:\Users\nnik0\.codex\bridge\COUNCIL_BRIDGE_RUNBOOK.md
```

This file contains only TPPA-specific additions. Do not copy bridge mechanics
from dated council reports or chat history.

## Canonical Repository

```text
C:\Dev\upas-nina-tppa-plugin
```

Before the first council call in a Codex run, use the local wrapper:

```powershell
pwsh -NoProfile -File .\tools\test_council_bridges.ps1
```

To pin an already committed checkpoint:

```powershell
pwsh -NoProfile -File .\tools\test_council_bridges.ps1 `
  -ExpectedHead <full-commit>
```

The wrapper delegates to the codex-wide preflight and supplies this repository's
canonical root. A skipped live check does not qualify the council seats.

## TPPA Provenance

Every TPPA review prompt must state:

- canonical root `C:\Dev\upas-nina-tppa-plugin`;
- full current HEAD;
- clean or dirty worktree state;
- exact relevant dirty files or patch when uncommitted work is under review;
- field-log/session paths and timestamps when findings depend on hardware data.

Require both reviewers to repeat root and checkpoint before findings. Reject and
rerun a seat that used OneDrive, another checkout, an earlier commit, or
incomplete field evidence.

## TPPA Disclosure Authorization

For this private TPPA project, the user has standing authorization to send the
branch code, patches, logs, hardware observations, and workflow details to
Claude and Gemini for council review. This authorization is limited to TPPA and
these two reviewers; it does not generalize to other private work or services.

Send only evidence relevant to the question. Keep secrets, credentials, tokens,
personal data, and unrelated private files out of prompts and attachments.

## TPPA Council Completion

A TPPA council remains incomplete until both the codex-wide Claude Opus 5 High
seat and Gemini 3.1 Pro High seat return substantive, provenance-verified final
answers. Record agreements, disagreements, and Codex's conservative adjudication
against tests, field evidence, and hardware safety guards.

Historical files such as dated `COUNCIL_REVIEW_*.md` describe the models and
results used at that time. They are evidence, not current invocation guidance.
