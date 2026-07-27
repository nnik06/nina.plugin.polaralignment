# TPPA Council Bridge Runbook

This is the canonical operational contract for Claude and Gemini council reviews
for `C:\Dev\upas-nina-tppa-plugin`. Do not reconstruct bridge commands from chat
history or from the dated council reports.

## Verified configuration

Last verified: 2026-07-27.

| Seat | MCP server | Underlying CLI | Required model | Read-only mode |
|---|---|---|---|---|
| Claude | `claude-cli-bridge` | Claude Code 2.1.214 | `fable` | `--safe-mode --permission-mode plan --tools ""` |
| Gemini | `gemini-bridge` | Antigravity (`agy`) 1.1.7 | `Gemini 3.1 Pro (High)` | `--mode plan --new-project --add-dir <root>` |

Active bridge sources:

- Claude: `C:\Users\nnik0\Documents\Codex\bridges\claude_fable_mcp_server.py`
- Gemini: `C:\Users\nnik0\OneDrive\Documents\Astro\gemini_mcp_server.py`
- Codex MCP configuration: `C:\Users\nnik0\.codex\config.toml`

Canonical repository root:

```text
C:\Dev\upas-nina-tppa-plugin
```

## Mandatory preflight

Run this before the first council call of a work session and after any bridge,
CLI, authentication, or Codex update:

```powershell
pwsh -NoProfile -File .\tools\test_council_bridges.ps1
```

The script verifies:

- the canonical git root and current commit;
- both configured bridge source paths and Python syntax;
- Claude and Antigravity CLI versions;
- Claude authentication;
- a live Claude Fable read-only ping;
- a live Gemini High read-only ping in a new project pinned to this repository.

The Antigravity live ping must run in the same interactive Windows user context
that owns `%USERPROFILE%\.gemini`. A sandbox that blocks that directory can
produce false authentication and access failures; rerun the preflight outside
that sandbox rather than changing credentials.

Use `-SkipLiveCalls` only for offline static checks. A skipped live check does
not qualify a council seat for use.

## Tool discovery in Codex

Use Codex tool discovery at the start of the session. Search for:

```text
Claude CLI bridge Gemini bridge council review
```

The current Codex app exposes:

- `mcp__claude_cli_bridge.consult_claude`
- `mcp__claude_cli_bridge.consult_claude_with_files`
- `mcp__gemini_bridge.consult_gemini`
- `mcp__gemini_bridge.consult_gemini_with_files`

Treat the tool names returned by the current app as authoritative. Historical
documents use hyphenated MCP identifiers and must not be copied literally.

Do not use bare `codex mcp list` as an app-health check. The shell may resolve a
different npm Codex CLI and may omit `CODEX_HOME`. For an external diagnostic:

```powershell
$env:CODEX_HOME = 'C:\Users\nnik0\.codex'
$codex = 'C:\Users\nnik0\AppData\Local\OpenAI\Codex\bin\69066b736e1e17a4\codex.exe'
& $codex mcp list
```

The app's discovered tools and a functional MCP call are the final authority.

## Provenance contract

Every repository-aware prompt must contain the expected canonical root and full
commit:

```powershell
git rev-parse --show-toplevel
git rev-parse HEAD
```

Require the reviewer to repeat both values before its findings. Reject the seat
and rerun when either value differs. Gemini historically retained an obsolete
OneDrive project even when its process working directory was correct.

The Gemini bridge therefore always starts `agy` with:

```text
--mode plan --new-project --add-dir C:\Dev\upas-nina-tppa-plugin
```

## Claude invocation

Use `consult_claude` for evidence already summarized in the prompt. Use
`consult_claude_with_files` only when exact source is necessary.

Recommended parameters:

```text
directory: C:\Dev\upas-nina-tppa-plugin
model: fable
timeout_seconds: 180
```

The Claude bridge disables Claude's tools. File calls inline at most 10 files,
256 KiB per file, and 512 KiB total. Tell Claude to answer from the supplied
material in the current response:

```text
Answer directly from the supplied evidence/files. Do not defer the answer,
announce future inspection, or request another turn.
```

If Claude returns only an intent such as "I will inspect...", that is not a
completed seat. Retry once with a shorter prompt and `consult_claude`, embedding
the decisive evidence directly.

Direct read-only fallback:

```powershell
claude -p --safe-mode --permission-mode plan --tools "" `
  --no-session-persistence --effort high --output-format text `
  --model fable "Reply exactly CLAUDE_BRIDGE_OK and nothing else."
```

Do not replace a failed Claude seat with another model or with Codex's own
opinion. Repair the invocation and rerun Claude.

## Gemini invocation

Use `consult_gemini` for summarized evidence. For repository files, use:

```text
directory: C:\Dev\upas-nina-tppa-plugin
model: Gemini 3.1 Pro (High)
mode: inline
timeout_seconds: 180
```

`inline` passes bounded file contents directly to Antigravity and is the only
supported attachment mode. Headless `@path` handling does not reliably attach
nested files and must not be used. The bridge limits each file to 12 KiB, all
files to 20 KiB, and the Windows command to 26,000 characters. Oversized context
fails closed; reduce the files or put a concise evidence summary in the prompt.

Direct read-only fallback:

```powershell
agy --mode plan --model "Gemini 3.1 Pro (High)" `
  --new-project --add-dir "C:\Dev\upas-nina-tppa-plugin" `
  --print-timeout 2m --print `
  "Reply exactly GEMINI_BRIDGE_OK and nothing else."
```

Do not count a Gemini review that used a stale project, omitted provenance, or
returned `WinError 206`.

## Council completion rule

A council is complete only when:

1. Claude produced a substantive final answer.
2. Gemini produced a substantive final answer.
3. Both used the expected root and commit.
4. Their agreements and disagreements were recorded.
5. Codex adjudicated disagreements conservatively against field evidence,
   safety guards, and tests.

A timeout, setup failure, partial answer, or stale-root answer leaves that seat
open. Fix it and rerun it. Never silently substitute or omit a requested seat.

## Troubleshooting

| Symptom | Cause | Recovery |
|---|---|---|
| Claude call times out | Prompt/file payload is too broad | Run the direct ping, shorten the request, use no files or fewer files, and retry with 180 seconds |
| Claude says it will inspect but gives no findings | Tools are deliberately disabled and the prompt invited exploration | Retry with summarized evidence and the direct-answer instruction |
| Gemini reports that files were not attached or a command permission was denied | Unreliable old `@path` mode, stale bridge process, or a prompt that invited tool use | Use the updated bridge with `mode=inline`, state that supplied evidence is sufficient, forbid tools/commands, and restart Codex if needed |
| Gemini `WinError 206` or bounded-context refusal | Too many or overly large inline files | Reduce the file set or summarize decisive evidence; never fall back to `@path` or broaden permissions |
| Gemini reviews an old tree | Reused Antigravity project | Require `--new-project --add-dir`, root, and full HEAD; reject stale output |
| Gemini says not authenticated in a Codex shell | Sandbox cannot access/write `%USERPROFILE%\.gemini` | Run the preflight in the authenticated interactive user context |
| `codex mcp list` reports no servers | Wrong Codex binary or missing `CODEX_HOME` | Set `CODEX_HOME`, use the bundled binary, then rely on app tool discovery |
| Updated Python bridge behaves unchanged | MCP server process still has old module loaded | Restart Codex, rediscover tools, rerun preflight and both functional pings |

## Bridge update procedure

1. Stop field operations; bridge maintenance must not compete with rig control.
2. Copy the bridge source to a timestamped backup.
3. Make the smallest change needed.
4. Run `python -m py_compile <bridge.py>`.
5. Run the underlying direct CLI ping in read-only mode.
6. Restart Codex so persistent MCP processes reload the source.
7. Rediscover the tools and run both functional MCP pings.
8. Update the verified date, versions, paths, and any new failure mode here.

Never edit authentication data, weaken plan/read-only mode, enable Claude tools,
or use Antigravity `accept-edits` merely to make a council call succeed.
