# Milestone 4: macro classroom gameplay

## Boundary decision

The economic model and typed classroom views remain in `SimulationPlatform.Simulations.Economics`. The module consumes a discipline-neutral `SessionInspection` application contract and translates frozen JSON state into macroeconomic console/debrief records. Application and Infrastructure contain no macro identifiers, indicators, roles, shocks, or theory.

Two generic lifecycle defects were corrected:

- Runtime scenario resolution now reads the immutable session manifest. A published template edit can no longer alter action or transition behavior in a running session.
- Readiness was previously only available during session setup. Generic per-round/per-phase readiness and round rollover now support any simulation whose manifest declares readiness-gated phases.

`ScenarioManifest.ReadinessRequiredPhases` and `MaximumRounds` are generic configuration. A transition back to the manifest's first phase starts the next numbered round. Submissions record their phase so readiness cannot be satisfied by work from an earlier phase.

The declarative rule fact `submission.count` exposes only the count for the current role assignment, action code and round. A high-priority deny rule can therefore enforce one prediction or decision per role without hard-coding macro action names into the runtime; the idempotent retry check still returns the original accepted submission before this constraint is evaluated.

## Recommended macro lifecycle

`Briefing -> Prediction -> Decision -> Locked -> Simulation -> Results -> Discussion`

From Discussion, the instructor either returns to Briefing for the next quarter or transitions to Completed. Prediction and Decision are configured as readiness-required. Students submit a standalone directional prediction before their role decision; both commands remain idempotent authoritative action submissions.

## Student result contract

Students continue to use the generic recovery endpoint. `ShortRunMacroModel.GenerateVisibleStateAsync` removes unauthorized fields before returning JSON. All roles receive headline output, inflation and unemployment. Role capabilities unlock institutional indicators, the student's own action assessment, relevant plain-language causal explanations, detected policy trade-offs, and—for monetary roles—a qualitative statement of inherited lagged effects.

The student projection does not include model configuration, coefficients, raw mechanism contributions, full state, scheduled future shocks, or other roles' conceptual assessments. `MACRO_VIEW_ALL` is an explicit scenario-granted exception.

## Instructor economy console

`GET /api/v1/economics/macro/sessions/{sessionId}/console` is protected by the Instructor platform policy and persisted course ownership. It returns, per team:

- full authoritative state and configuration;
- current role decisions and predictions;
- phase submission/readiness status;
- scheduled shocks;
- raw causal contributions;
- detected conflicts and stagflation trade-offs;
- conceptual scores and economic objectives;
- complete event history.

The console is read-only. It cannot replace authoritative state or execute calculations.

## Debrief and replay

`GET /api/v1/economics/macro/sessions/{sessionId}/debrief` reconstructs each team's completed quarters from immutable snapshots, submissions and append-only events. Each replay includes the authoritative macro state/report, decisions, predictions and quarter events. Conceptual-score averages and counts of achieved economic objectives are reported separately; neither is derived from the other.

## Transaction and multiplayer guarantees

Readiness changes, phase changes and round rollover append events and outbox notifications in the same serializable transaction. Round execution claims remain unique by session/team/round. A multi-team session remains in Simulation until every team has committed exactly one execution, then advances once to Results. Reconnect recovery always reads persisted state.
