---
change_id: license-deactivate-reactivate
title: License deactivate / reactivate
status: impl_reviewed
created: 2026-08-12
updated: 2026-08-12
archived_at: null
---

## Notes

Plan review (2026-08-12): 1 critical (wrong MudBlazor API name — fixed), 2 warnings. F2 (unsaved-edit
data loss risk in the original two-button design) resolved by replacing separate Deactivate/Reactivate
buttons with an Active/Inactive switch inside the existing edit form, persisted only via the existing
Save button — see `reviews/plan-review.md` for the full triage record. F3 (Migration Notes omitting the
app's auto-migrate-on-startup behavior) skipped, accepted as-is.
