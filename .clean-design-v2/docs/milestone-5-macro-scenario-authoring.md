# Milestone 5: macro scenario authoring

## Boundary and invariants

Scenario authoring is a typed adapter owned by `SimulationPlatform.Simulations.Economics`. The generic Application layer stores opaque draft documents and publishes generic `ScenarioManifest` values. Domain, Application, Identity, and Infrastructure contain no macroeconomic types or references to the economics assembly.

Instructors configure pedagogy and bounded model inputs, not equations or coefficients. `Economics.ShortRunMacro:1.0.0` remains the sole owner of its mechanisms, transmission coefficients, state bounds, shock behavior, causal contributions, and scoring behavior.

The following invariants are enforced server-side:

- A draft belongs to one instructor and one simulation definition.
- Only its owner can read, update, validate, preview, clone, archive, or publish it.
- Draft updates and terminal transitions require an expected version.
- Published and archived drafts cannot be edited.
- Publication is blocked unless the typed authoring validator and generic manifest validator succeed.
- Creating the immutable scenario version, marking the draft published, writing the audit record, and enqueueing publication notifications occur in one serializable database transaction.
- A session copies the published manifest into its own hashed `session_manifest`; later draft, template, or scenario-version activity cannot alter that run.

## Persistence

`definitions.scenario_drafts` stores the authoring lifecycle:

| Column | Purpose |
|---|---|
| `Id` | Draft identity |
| `SimulationDefinitionId` | Generic owning definition FK |
| `OwnerUserId` | Authorization boundary derived from the principal |
| `Name` | Instructor-facing title |
| `Status` | `Draft`, `Published`, or `Archived` |
| `ContentJson` | Versioned module-owned authoring document |
| `Version` | Optimistic concurrency token |
| `PublishedScenarioVersionId` | Result of the one-way publication transition |
| timestamps | Audit-friendly lifecycle timestamps |

Published runtime material continues to use immutable `definitions.scenario_versions`. Sessions reference that row and freeze a byte-equivalent manifest plus SHA-256 hash in `runtime.session_manifests`.

## Authorization flow

The API first requires the platform `Instructor` policy. The authenticated user ID is then passed as the actor; no instructor ID is accepted from request bodies. Persistence queries scope every draft and simulation definition by `OwnerUserId`. Publication also resolves the registered model identifier/version and the session creation flow verifies ownership of both the classroom and published definition. Simulation role capabilities remain unrelated to platform authorization.

## Authoring controls

The typed document exposes starting aggregate conditions, a 1–20 quarter horizon, the four model-supported institutional roles, role and team objectives, supported actions and qualitative intensities, scheduled supported shocks, role visibility choices, briefing text, learning objectives, and discussion/debrief prompts. It deliberately exposes no formulas, executable rules, mechanism coefficients, arbitrary expressions, international variables, or empirical forecasting inputs.

Reusable built-in templates are read-only typed documents. Cloning creates an instructor-owned draft; templates are never mutated.

## Readiness validation

Blockers prevent publication. They cover missing briefing/objectives, unsupported or duplicate roles, missing role decisions, unsupported actions or intensities, invalid horizons, invalid shock types/intensities/timing, out-of-bound starting conditions, and objectives outside the model's achievable state bounds.

Warnings remain publishable and cover missing role objectives, no scheduled shocks, missing discussion/debrief prompts, and duplicate accumulating shocks. Validation is repeated during publication, so a client cannot bypass it.

## Deterministic private preview

Preview first performs ownership and readiness checks, then initializes and executes the registered macro model directly using the requested seed. It uses only supported qualitative decisions and scheduled scenario shocks. The response includes immutable per-quarter projected states and diagnostics for:

- repeated state-bound saturation;
- abrupt/unstable trajectories;
- negligible first-quarter player agency;
- impossible configuration and invalid shock timing through readiness blockers.

Preview is private, does not create a classroom session, and does not publish or modify the draft. Identical content and seed produce identical results.

## Transaction and outbox design

Draft creation, cloning, updates, and lifecycle changes write their audit/outbox records with their state changes. Publish uses a serializable transaction spanning draft version/status verification, immutable scenario-version insertion, generic rule parsing, publication linkage, audit insertion, and both `ScenarioPublished` and `ScenarioDraftPublished` outbox records. SignalR remains notification-only; reconnecting clients recover authoritative state through REST.

## API surface

All routes are under `/api/v1/economics/macro/scenario-authoring` and require the Instructor platform policy:

- `GET /templates`
- `POST /drafts`
- `GET /drafts/{id}`
- `PUT /drafts/{id}`
- `POST /drafts/{id}/clone`
- `POST /drafts/{id}/archive`
- `POST /drafts/{id}/validate`
- `POST /drafts/{id}/preview`
- `POST /drafts/{id}/publish`

Creation and cloning require `Idempotency-Key`. Mutations use expected versions. Domain/concurrency failures continue through the platform RFC Problem Details pipeline.
