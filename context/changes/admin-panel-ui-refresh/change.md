---
change_id: admin-panel-ui-refresh
title: Admin panel ui refresh
status: impl_reviewed
created: 2026-08-11
updated: 2026-08-11
archived_at: null
---

## Notes

**2026-08-11 — implemented, impl-reviewed, design deviation in Phase 5.** All 5 phases
shipped (commits `a6737ba`, `79287e6`, `4bb7c10`, `508c2aa`, `e0cf71b`, epilogue
`aa9ec01`). During Phase 5 manual verification, a real MudBlazor limitation surfaced:
`MudThemeProvider`/`MudPopoverProvider` don't work as a single instance in
`MainLayout.razor` under this app's per-page `@rendermode` (required so `Login.razor`
can stay static SSR for `HttpContext.SignInAsync`). Fixed by moving the providers into a
new `src/Components/Shared/MudProviders.razor` component added on each interactive page,
backed by a new scoped `src/Components/ThemeState.cs` service — see `plan.md`'s Phase 1
superseded-note and `context/foundation/lessons.md` for the recorded rule. Impl-review
(`reviews/impl-review.md`) confirmed the fix is correct with no leaks/regressions;
verdict APPROVED with 1 warning (documentation gap, now closed by this note + the
lessons.md entry).
