# Final integrated course assignment UI design

The course detail screen owns the workflow and has six sections: course information, learners, training groups, teachers/vehicles, Excel import/export and history.

It supports create/inactivate group, multi-select learners, put learners into a group, bulk assignment, per-student overrides, filter unassigned learners and view immutable history. Course/teacher/vehicle source fields are read-only.

Putting learners in a group previews copied group defaults and creates `GROUP` snapshots. Changing group defaults requires `UNOVERRIDDEN_ONLY`, `REPLACE_ALL` or `NO_CURRENT_CHANGE`. Per-student SET/CLEAR/INHERIT updates field-level override flags and creates a new snapshot after RowVersion revalidation.

API scope includes dossier receiver catalog, source read-only lookups, course groups, current/history assignment, single/bulk/group preview-confirm and Excel export/import. Every write is policy-protected; UI hiding alone is insufficient.

Exact workflow and testable labels are in `handoff/HOCVIEN_ASSIGNMENT_REVIEW/07_UI_WORKFLOW.md` and `11_API_UI_AND_TEST_MATRIX.md`.
