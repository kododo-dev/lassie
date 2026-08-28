# code-review agent

Scripted code-review agent built on `@anthropic-ai/claude-agent-sdk`. Reads a unified
diff on stdin and prints a structured JSON verdict (five scored criteria, an overall
`pass`/`fail`, and a summary) on stdout.

## Usage

```
git diff origin/main...HEAD | npx tsx review.ts
```

Authentication comes from `CLAUDE_CODE_OAUTH_TOKEN` in the environment (a Claude
subscription token from `claude setup-token`), so CI runs do not need a billed API key.

## In CI

`.github/workflows/code-review.yml` runs this on every pull request to `main`: it posts
the verdict as a PR comment, applies the `ai-cr:passed` / `ai-cr:failed` label, and fails
the job on a `fail` verdict.
