# Milestone 6: learning analytics, assessment, and replay

Analytics are instructor-only projections over authoritative persisted session data. The analytics service reads the frozen session manifest, immutable submissions, immutable events, and persisted snapshots/results through `IClassroomWorkflow.InspectAsync`; it never invokes `ShortRunMacroModel` and never recalculates a historical quarter.

## Typed assessment dimensions

The economics plugin owns `MacroAssessmentDimension`: directional prediction, causal mechanism, trade-off awareness, lag recognition, objective achievement, policy reasoning, and economic outcome. These are returned as separate fields in student, role, team, and session slices. Conceptual score, policy-reasoning score, objective score, and economic-outcome score are never collapsed into an opaque grade. Scenario configuration can select dimensions through its typed plugin-owned configuration; it cannot submit executable grading code.

## Replay contract

`MacroSessionReport.Replay` is an immutable quarter sequence. Each quarter includes the frozen state snapshot, submitted predictions, submitted decisions, persisted causal contributions, policy conflicts/trade-offs, prediction assessments, objective results, and event history. Because replay is projection-only, identical stored data always produces identical replay output and historical model changes cannot alter it. The existing REST recovery path remains the reconnect source of truth.

## Analytics dimensions

Student and aggregate slices report prediction counts/directions, conceptual understanding, role policy reasoning, objectives, economic outcomes, shock response evidence, and lag recognition. Aggregates are grouped by role, team, and session. Instructor routes expose the full comparison and cohort summary while student projections remain capability-filtered through the existing gameplay/recovery contracts.

## Comments and auditability

Instructor assessment comments are generic persisted learning records keyed to a session and target (`Student`, `Role`, `Team`, or `Session`). They use idempotent creation, optimistic version checks on edits, ownership-scoped reads/writes, and immutable audit records for create/update operations. Comment text is bounded and never includes secrets.

## Reports

`GET /api/v1/economics/macro/sessions/{sessionId}/report?format=json|csv` returns a structured report suitable for archival or spreadsheet analysis. JSON includes analytics and replay; CSV contains stable student analytics columns. The report is generated from recorded data and therefore is safe for debrief/research replay.

## Authorization and invariants

All analytics, replay, comparison, report, and comment routes require the platform Instructor policy. Resource ownership is resolved from the authenticated principal through the classroom/course owner relationship. Simulation roles and capabilities do not grant platform instructor authority. Existing serializable runtime transactions, idempotency records, concurrency tokens, immutable event history, frozen manifests, and transactional outbox behavior remain unchanged.
