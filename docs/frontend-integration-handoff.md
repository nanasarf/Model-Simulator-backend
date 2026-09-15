# Simulation Platform frontend integration handoff

This is the frontend contract for the backend currently implemented after Milestones 1–7. The API is an ASP.NET Core modular monolith. Generic classroom/runtime contracts live in Domain/Application/Infrastructure; economics assemblies are plugins registered through `ISimulationModel`. The authoritative sources are the OpenAPI document at `/swagger/v1/swagger.json` (Development) and the endpoint files listed in §33.

## 1. Platform overview

Users authenticate through ASP.NET Core Identity. Platform roles are `PlatformAdministrator`, `Instructor`, and `Student`; a simulation role (for example `GOVERNMENT` or `BUYER`) never grants platform authority. Instructor operations are ownership-scoped through the course owner resolved from the authenticated subject. Students must be enrolled in the classroom and separately added to a session team. A session contains teams, participants, role assignments, rounds/phases, submissions, snapshots, immutable events, and a frozen manifest copied from one published scenario version.

Generic reusable concepts are: model descriptor, simulation definition, scenario manifest/version, session, team, participant, role/capability, action submission, phase/round, readiness, state projection, snapshot, event, result, objective, metric, assessment, and outbox notification. A published version and its session manifest are immutable. `Version` is an optimistic-concurrency token on sessions and drafts; expected draft versions are required for edits/archive/publish.

The normal phase sequence is `Briefing → Prediction → Decision → Locked → Simulation → Results → Discussion → Briefing` (or `Completed`). The exact phase names come from the frozen manifest. Students submit predictions/actions only in their allowed phase and mark round readiness. Instructors create/start/pause/resume/advance phases and initiate execution. Execution persists snapshots/events/results transactionally; racing execution requests yield one success and a conflict/already-executed result. REST recovery is authoritative after refresh/reconnect; SignalR only invalidates/refetches state.

ShortRunMacro-specific concepts are macro roles (`GOVERNMENT`, `CENTRAL_BANK`, `BUSINESS`, `HOUSEHOLD_LABOR`), qualitative policy actions/intensities, macro state, shocks, causal contributions, policy conflicts, stagflation/lag explanations, macro objectives, and typed `MacroAssessmentDimension`. CompetitiveMarket-specific concepts are buyer/seller/government roles, bids/asks, market clearing, transactions, surplus/welfare, price controls/taxes/subsidies, and typed `MarketAssessmentDimension`. Generic code understands neither GDP nor bids.

Draft/scenario authoring writes state plus audit/outbox records. Outbox messages are dispatched to SignalR. RFC Problem Details are returned for domain/security failures. State-changing create/clone/comment operations require `Idempotency-Key`; same key and request replays the original result, while a different request is `idempotency.conflict`. Stale versions are `concurrency.conflict` (409).

## 2. Authentication and client behavior

| Method/route | Auth | Request | Response | Retry |
|---|---|---|---|---|
| `POST /api/v1/auth/register` | Anonymous | `{email,password}` | `201 {id,email}` | Do not retry blindly |
| `POST /api/v1/auth/login` | Anonymous | `{email,password}` | `200 {accessToken,refreshToken,accessTokenExpiresAt}` | User action only |
| `POST /api/v1/auth/refresh` | Anonymous bearer not required | `{refreshToken}` | Rotated `IssuedTokens` | One attempt; reuse/revocation requires sign-in |
| `POST /api/v1/auth/logout` | Anonymous bearer not required | `{refreshToken}` | `204` | Safe to retry |

Access tokens default to 10 minutes; refresh tokens default to 14 days. Tokens are returned in JSON, not cookies. Store refresh tokens in the most protected client storage available; never log either token. JWT contains `sub`, `jti`, `security_stamp`, and role claims. Refresh rotation revokes the presented token and issues a replacement in the same family. Expired/revoked/reused tokens revoke the whole family; a changed security stamp also invalidates refresh. A 401 from an API should trigger one refresh attempt, then clear local credentials and route to login. Refresh failure returns Problem Details `authentication.invalid_token`; never loop.

Typical login response:

```json
{"accessToken":"eyJ...","refreshToken":"base64...","accessTokenExpiresAt":"2026-09-05T12:10:00Z"}
```

Register validation failures are Identity validation errors (HTTP 400). Invalid credentials are HTTP 401 `authentication.invalid_credentials`. Invalid refresh is HTTP 401 `authentication.invalid_token`. Logout is intentionally idempotent and returns 204 even for an unknown token. The current user is the JWT `sub`; platform roles are JWT role claims (`Instructor`, `Student`, `PlatformAdministrator`). There is no `/me` endpoint.

## 3. Courses, classrooms, enrollment

| Method/route | Auth | Request/response |
|---|---|---|
| `POST /api/v1/courses` | Instructor | `{code,name}` → `201 {id}` |
| `POST /api/v1/courses/{courseId}/classrooms` | Instructor, course owner | `{name}` → `201 {id}` |
| `POST /api/v1/classrooms/{classroomId}/enrollments` | Instructor, classroom owner | `{userId}` → `204` |

There are no list/get course/classroom/enrollment endpoints or pagination. Enrollment is distinct from session participation: enrollment authorizes adding a student to a session, while a participant is created when the instructor adds that student to a session team. Ownership violations are deliberately not disclosed and generally appear as 404/domain not-found.

## 4. Models and scenarios

`GET /api/v1/models` is anonymous and returns descriptors such as:

```json
[{"identifier":"Economics.ShortRunMacro","version":"1.0.0","name":"Short-Run Macroeconomics"},{"identifier":"Economics.CompetitiveMarket","version":"1.0.0","name":"Competitive Market"}]
```

Scenario and definition discovery endpoints are documented in §4a. Published versions are created through `POST /api/v1/simulation-definitions/{definitionId}/scenarios` or plugin authoring publish routes. A manifest permanently pins model identifier/version, configuration version, phases, actions, roles, transitions, and presentation. Sessions copy that manifest; frontend must use the manifest rather than hard-code model behavior. Compare the identifier strings exactly; model-specific UI may branch on them, while generic infrastructure should remain model-neutral.

## 4a. Definition, scenario, version, and template discovery

`SimulationDefinition` is an instructor-owned naming/version container and is not itself bound to one plugin model. Model identity becomes immutable on a published scenario version. Do not treat definition IDs as model identifiers.

| Method/route | Auth | Result |
|---|---|---|
| `GET /api/v1/simulation-definitions/models` | Anonymous | `AuthoringModelSummary[]` safe plugin metadata |
| `GET /api/v1/simulation-definitions` | Instructor | Current instructor's `SimulationDefinitionSummary[]` |
| `GET /api/v1/simulation-definitions/{definitionId}` | Instructor owner | One definition or hidden 404 |
| `GET /api/v1/scenarios` | Instructor | Paged current-instructor `ScenarioSummary` library |
| `GET /api/v1/scenarios/{scenarioId}/versions` | Instructor owner | Published versions linked to that typed authoring draft |
| `GET /api/v1/scenario-versions/{versionId}` | Instructor owner | One immutable `PublishedScenarioVersionSummary` |
| `GET /api/v1/simulation-definitions/{definitionId}/scenario-versions` | Instructor owner | All published versions in the definition, newest version first |
| `GET /api/v1/scenario-templates` | Instructor | Safe metadata for all authoring templates |
| `GET /api/v1/scenario-templates/{modelIdentifier}/{templateIdentifier}` | Instructor | One template metadata item |

`AuthoringModelSummary` is `{identifier,version,displayName,scenarioAuthoringSupported,templatesAvailable,availability}`. Availability is currently `Available`. The response intentionally excludes implementation type names, configuration, coefficients, assembly paths, and plugin internals. Registered models without typed authoring may appear with both authoring booleans false.

`SimulationDefinitionSummary` is `{id,displayName,authorableModelIdentifiers[]}`. Only definitions whose `OwnerUserId` equals the authenticated subject are returned. The current authorable identifiers are `Economics.ShortRunMacro` and `Economics.CompetitiveMarket`.

`GET /api/v1/scenarios` query parameters:

- `status`: exact `Draft|Published|Archived`; omitted returns Draft and Published.
- `modelIdentifier`: exact `Economics.ShortRunMacro|Economics.CompetitiveMarket`.
- `search`: case-insensitive title substring.
- `includeArchived`: default false; applies only when `status` is omitted.
- `page`: one-based, default 1.
- `pageSize`: default 25, maximum 100.

Results are deterministically ordered by `updatedAt` descending and ID ascending. The envelope is `{items,page,pageSize,totalCount}`. `ScenarioSummary` is `{scenarioId,draftId,simulationDefinitionId,modelIdentifier,modelVersion,title,summary,lifecycleStatus,publishedVersionId,publishedVersionNumber,maximumRounds,createdAt,updatedAt,publishedAt,concurrencyVersion,launchable,launchabilityReason}`. `maximumRounds` represents either rounds or quarters according to the model. Collection results contain no full authoring content or owner identity. Archived rows remain available through `status=Archived` or `includeArchived=true`, but are excluded by default.

`PublishedScenarioVersionSummary` is `{publishedVersionId,simulationDefinitionId,title,modelIdentifier,modelVersion,scenarioVersionNumber,publishedAt,isLatest,immutable,sourceScenarioArchived,launchable,launchabilityReason}`. Published version resources expose metadata only and have no mutation routes. Archiving the source draft does not mutate or technically invalidate its immutable published version, so `sourceScenarioArchived` is separate from `launchable`; archived source scenarios are excluded from the default library/launch-picker query. Definition-level history reflects the persistence model's version sequence. A typed authoring draft currently links to one published version, so its scenario-specific version list contains zero or one item; use definition history for the complete persisted definition sequence.

Template summaries are `{templateIdentifier,modelIdentifier,modelVersion,title,description,learningPurposeSummary,systemProvided,canCreateDraft}`. They intentionally omit template content and model internals. Use the existing typed model template route when the create editor needs the authoring content. No template mutation route exists.

Example requests and abbreviated responses:

```http
GET /api/v1/scenarios?status=Published&modelIdentifier=Economics.ShortRunMacro&page=1&pageSize=25
Authorization: Bearer eyJ...
```

```json
{"items":[{"scenarioId":"...","draftId":"...","simulationDefinitionId":"...","modelIdentifier":"Economics.ShortRunMacro","modelVersion":"1.0.0","title":"Oil disruption","lifecycleStatus":"Published","publishedVersionId":"...","publishedVersionNumber":2,"maximumRounds":4,"concurrencyVersion":6,"launchable":true}],"page":1,"pageSize":25,"totalCount":1}
```

```http
GET /api/v1/scenario-templates?modelIdentifier=Economics.CompetitiveMarket
Authorization: Bearer eyJ...
```

```json
[{"templateIdentifier":"BASIC_MARKET","modelIdentifier":"Economics.CompetitiveMarket","modelVersion":"1.0.0","title":"Basic competitive market","systemProvided":true,"canCreateDraft":true}]
```

Owner identity always comes from JWT `sub`; no discovery endpoint accepts an owner/user ID. Cross-owner detail/version lookups return `scenario.not_found`, `scenario_draft.not_found`, `scenario_version.not_found`, or `definition.not_found` as a non-disclosing 404. Invalid filters return 422 `scenario.lifecycle_filter_invalid`, `scenario.page_size_invalid`, or `simulation_model.unsupported`. Missing template lookup returns 404 `scenario_template.not_found`. Problem Details includes the normal trace ID.

# ShortRunMacro

## 5. Authoring endpoints

Base: `/api/v1/economics/macro/scenario-authoring`; all routes require `Instructor` and draft/definition ownership.

| Method | Route | Body/headers | Result |
|---|---|---|---|
| GET | `/templates` | — | `MacroTemplate[]` |
| POST | `/drafts` | `CreateMacroDraftRequest`; `Idempotency-Key` required | `201 MacroDraft` |
| GET | `/drafts/{draftId}` | — | `MacroDraft` |
| PUT | `/drafts/{draftId}` | `UpdateMacroDraftRequest` with `expectedVersion` | `200 MacroDraft` |
| POST | `/drafts/{draftId}/clone` | `{name}`; idempotency key | `201 MacroDraft` |
| POST | `/drafts/{draftId}/archive` | `{expectedVersion}` | `204` |
| POST | `/drafts/{draftId}/validate` | — | `MacroValidationReport` |
| POST | `/drafts/{draftId}/preview` | `{seed}` | `MacroPreviewResult` |
| POST | `/drafts/{draftId}/publish` | `{expectedVersion}` | `200 {scenarioVersionId}` |

Draft and published-version discovery is available through the lightweight generic metadata routes in §4a. Generic direct publication is available at `POST /api/v1/simulation-definitions/{definitionId}/scenarios` with `PublishScenarioRequest`; it bypasses typed authoring validation and should not be used for macro authoring screens.

`MacroScenarioContent` fields are `briefing`, `learningObjectives[]`, `discussionPrompts[]`, `debriefPrompts[]`, `startingConditions` (`outputIndex`, `potentialOutputIndex`, `inflation`, `unemployment`, `policyRate`, `debtToOutput`), `maximumQuarters` (1–20), `roles[]` (`code`, `enabled`, `canSeeInstitutionalIndicators`, `objectives[]`), `enabledActions`, `allowedIntensities` (`Mild|Moderate|Strong`), `scheduledShocks[]` (`round`, `type`, `intensity`), `teamObjectives`, and optional `assessmentDimensions[]` (`DirectionalPrediction|CausalMechanism|TradeoffAwareness|LagRecognition|ObjectiveAchievement|PolicyReasoning|EconomicOutcome`). The model owns coefficients, bounds, equations, mechanism internals, and shock math; these are never author-editable or student-facing.

Known blockers: missing briefing/learning objective; invalid quarter range; duplicate/unsupported roles; missing required role action; empty/unsupported intensity; invalid shock type/intensity/timing; out-of-bounds starting state; impossible objective range. Warnings: missing role objective, no shocks, missing discussion/debrief prompts, duplicate accumulating shocks. Typical failures are 404 not-found, 409 concurrency/idempotency conflict, and 422 `scenario.validation_failed` or field-specific domain codes.

Example:

```json
{"simulationDefinitionId":"00000000-0000-0000-0000-000000000001","name":"Oil disruption","content":{"briefing":"Prepare for a supply disruption.","learningObjectives":["Explain AD-AS transmission."],"discussionPrompts":["Which policies conflicted?"],"debriefPrompts":["What would you change?"],"startingConditions":{"outputIndex":100,"potentialOutputIndex":100,"inflation":2,"unemployment":5,"policyRate":3,"debtToOutput":55},"maximumQuarters":4,"roles":[{"code":"GOVERNMENT","enabled":true,"canSeeInstitutionalIndicators":true,"objectives":["Support output"]},{"code":"CENTRAL_BANK","enabled":true,"canSeeInstitutionalIndicators":true,"objectives":["Maintain prices"]},{"code":"BUSINESS","enabled":true,"canSeeInstitutionalIndicators":true,"objectives":["Maintain capacity"]},{"code":"HOUSEHOLD_LABOR","enabled":true,"canSeeInstitutionalIndicators":true,"objectives":["Support employment"]}],"enabledActions":["SET_FISCAL_POLICY","SET_MONETARY_POLICY","SET_BUSINESS_STRATEGY","SET_HOUSEHOLD_LABOR_STANCE"],"allowedIntensities":["Mild","Moderate","Strong"],"scheduledShocks":[{"round":2,"type":"supply_disruption","intensity":"Moderate"}],"teamObjectives":{"inflationMinimum":1,"inflationMaximum":3,"unemploymentMaximum":6,"outputGapAbsoluteMaximum":2,"debtToOutputMaximum":80},"assessmentDimensions":["DirectionalPrediction","CausalMechanism","TradeoffAwareness"]}}
```

## 6. Macro preview

`MacroPreviewResult` is `{seed,quarters[],diagnostics[]}`. Each quarter is `{quarter,state}` where state contains the typed macro state and last report. Diagnostics are warnings such as `REPEATED_SATURATION`, `UNSTABLE_TRAJECTORY`, `INEFFECTIVE_PLAYER_AGENCY`, plus readiness blockers/warnings when preview is not publishable. Preview is instructor-only, private, deterministic, and does not create a session. Raw coefficients and internal mechanism values are not in authoring content; instructors may see full state/report through the console, students do not.

# CompetitiveMarket

## 7. Authoring endpoints

Base: `/api/v1/economics/competitive-market/scenario-authoring`; all require Instructor ownership. Routes mirror macro: `GET /templates`, `POST /drafts`, `GET /drafts/{id}`, `PUT /drafts/{id}`, `POST /drafts/{id}/clone`, `POST /drafts/{id}/archive`, `POST /drafts/{id}/validate`, `POST /drafts/{id}/preview`, and `POST /drafts/{id}/publish`. Create/clone require `Idempotency-Key`; update/archive/publish require `expectedVersion`. Lightweight draft/version discovery uses §4a.

`CompetitiveMarketScenarioContent` contains briefing/objectives/prompts, `configuration` (`demandIntercept`, `supplyIntercept`, `demandSlope`, `supplySlope`, `priceCeiling`, `priceFloor`, `unitTax`, `unitSubsidy`, `policy`, `buyerCount`, `sellerCount`, `unitsPerBuyer`, `unitsPerSeller`, `maximumRounds`, `scheduledShocks[]`), `maximumRounds`, roles, `enabledActions`, and typed `assessmentDimensions[]` (`EquilibriumReasoning`, `DemandSupplyReasoning`, `Elasticity`, `ConsumerProducerSurplus`, `TaxIncidence`, `PriceControls`, `CausalMarketReasoning`). Supported shocks are `demand_increase`, `demand_decrease`, `supply_increase`, `supply_decrease`; intensities are `Mild|Moderate|Strong`. The current model generates deterministic private valuations/costs from configuration and seed. There is no separately authored valuation/cost array endpoint.

Instructor/audit-only: complete state, all orders, all transactions, welfare breakdown, future shocks, diagnostics. Buyer-visible: only buyer information when `MARKET_VIEW_BUYER_INFO`; seller-visible: only seller information when `MARKET_VIEW_SELLER_INFO`; never expose other agents' valuations/costs or raw internals. Example:

```json
{"simulationDefinitionId":"00000000-0000-0000-0000-000000000001","name":"Tax incidence","content":{"briefing":"Trade in a competitive market.","learningObjectives":["Explain equilibrium and incidence."],"discussionPrompts":["Who bears the tax?"],"debriefPrompts":["Where did deadweight loss arise?"],"configuration":{"demandIntercept":120,"supplyIntercept":20,"demandSlope":1,"supplySlope":1,"policy":"PerUnitTax","unitTax":10,"buyerCount":10,"sellerCount":10,"unitsPerBuyer":1,"unitsPerSeller":2,"maximumRounds":4,"scheduledShocks":[{"round":2,"type":"demand_decrease","intensity":"Moderate"}]},"maximumRounds":4,"roles":[{"code":"BUYER","enabled":true,"seePrivateInformation":true,"objectives":["Buy above value"]},{"code":"SELLER","enabled":true,"seePrivateInformation":true,"objectives":["Sell above cost"]},{"code":"GOVERNMENT","enabled":true,"seePrivateInformation":false,"objectives":["Monitor welfare"]}],"enabledActions":["SUBMIT_BUYER_BID","SUBMIT_SELLER_ASK","SUBMIT_MARKET_PREDICTION"],"assessmentDimensions":["EquilibriumReasoning","TaxIncidence","ConsumerProducerSurplus"]}}
```

## 8. Session creation and launch

| Method/route | Auth | Body/result |
|---|---|---|
| POST `/api/v1/simulation-definitions` | Instructor | `{name}` → `201 {id}` |
| POST `/api/v1/simulation-definitions/{definitionId}/scenarios` | Instructor owner | `{name,manifest}` → `201 {id}` |
| POST `/api/v1/classrooms/{classroomId}/sessions` | Instructor classroom owner | `{scenarioVersionId,seed}` → `201 {id}` |
| POST `/api/v1/sessions/{sessionId}/teams` | Instructor owner | `{name}` → `201 {id}` |
| POST `/api/v1/sessions/{sessionId}/teams/{teamId}/members` | Instructor owner | `{userId}` → `204` |
| POST `/api/v1/sessions/{sessionId}/role-assignments` | Instructor owner | `{teamId,userId,roleCode}` → `201 {id}` |
| POST `/api/v1/sessions/{sessionId}/commands/start` | Instructor owner | none → `204` |
| POST `/commands/pause`, `/commands/resume` | Instructor owner | none → `204` |
| POST `/commands/advance-phase` | Instructor owner | `{targetPhase}` → `204` |
| GET `/api/v1/sessions/{sessionId}/state` | enrolled participant or owner | `SessionRecoveryView` |
| GET `/api/v1/sessions/{sessionId}/history` | same | `HistoryItem[]` |

There are no list sessions, join-code, share-link, remove-member, reset, or end-session endpoints. Scenario and published-version discovery for launch selection is documented in §4a. Session creation freezes the selected published version. Legal transitions are manifest-defined; normal start is `Draft→first phase`, pause/resume are status transitions, and phase advancement must follow `AllowedTransitions`. Invalid transitions are 422 domain errors; stale session updates are 409. Team/role creation is instructor-only and intended before start.

## 9. Join/reconnect

Students need an account, classroom enrollment, and instructor-added session participant membership. No join code or self-join endpoint exists. The instructor assigns teams/roles before launch. Students cannot add themselves or join an arbitrary running session. On initial load, refresh, tab resume, or SignalR reconnect call `GET /api/v1/sessions/{id}/state`; use the returned `teamId`, `roleCodes`, `participants`, `phase`, `roundNumber`, `version`, and `visibleState`. SignalR group membership is re-established only by calling hub `JoinSession` again; it is not persisted automatically.

## 10–11. Lifecycle and readiness

Phase strings are manifest values; standard values are `Briefing`, `Prediction`, `Decision`, `Locked`, `Simulation`, `Results`, `Discussion`, `Reflection`, `Completed`. Readiness endpoints are `PUT /api/v1/sessions/{id}/participants/me/readiness` and `PUT /api/v1/sessions/{id}/rounds/current/readiness`, body `{ready}`; both require Student and return 204. Participant readiness is used for start; round readiness is checked by configured phase. Readiness rows are phase/round-specific and reset logically when round/phase changes. Instructors see readiness in `SessionInspection`/console.

Submission count is evaluated server-side by declarative rules. Frontend may disable duplicate-submit UI but must assume the backend is authoritative. Decisions are accepted in their manifest phase; before lock the same idempotency key replays and distinct keys may be rejected by `submission.count` rules. There is no generic “get my prediction” endpoint; recover state/history or instructor inspection.

## 12–13. Predictions and actions

Generic submission: `POST /api/v1/sessions/{sessionId}/actions`, Student policy, body `{teamId,roleAssignmentId,actionCode,payload}`, `Idempotency-Key` required. Response is `202 {id,...}`. It authenticates the caller, verifies enrollment/team/assignment/capability/phase/rules/model validation, then persists an immutable submission. Duplicate key/request replays; same key/different payload is 409.

Macro actions: `SUBMIT_DIRECTIONAL_PREDICTION` (capability `MACRO_SUBMIT_PREDICTION`, Prediction phase) with bounded direction values `increase|decrease|stable` for output/inflation/unemployment and explanation; `SET_FISCAL_POLICY`, `SET_MONETARY_POLICY`, `SET_BUSINESS_STRATEGY`, `SET_HOUSEHOLD_LABOR_STANCE` require corresponding role capabilities and Decision phase. Qualitative intensities are `Mild|Moderate|Strong`; role directions are model-validated. `TRIGGER_EXTERNAL_SHOCK` is model-defined but not exposed by published authoring manifests.

Market actions: `SUBMIT_BUYER_BID` (`MARKET_SUBMIT_BID`) and `SUBMIT_SELLER_ASK` (`MARKET_SUBMIT_ASK`) in Decision phase, payload `{price,quantity}` with price 0–10000 and quantity 1–1000; `SUBMIT_MARKET_PREDICTION` (`MARKET_SUBMIT_PREDICTION`) in Prediction phase with `MarketPrediction` fields `price`, `quantity`, `shortageSurplus`, `welfare`, `explanation`. No government order action is currently implemented. Orders are immutable submissions; clearing uses all submitted orders, or deterministic fallback private values/costs when absent.

## 14. Student projection

`GET /api/v1/sessions/{id}/state` is the recovery endpoint. `SessionRecoveryView` always includes session metadata, current phase/round/version, caller team, role codes, participants, and a nullable capability-filtered `visibleState`. Instructor recovery includes full state; students receive model projections generated with their capability set. Macro excludes coefficients, raw mechanism contributions, future shocks, complete reports, and internal configuration unless `MACRO_VIEW_ALL` is granted. Market excludes other buyers' valuations, seller costs, future shocks, all-agent diagnostics, and instructor welfare internals. Frontend should render only fields present in `visibleState`; do not infer hidden values.

Representative capability behavior:

```json
{"sessionId":"...","phase":"Results","roundNumber":2,"teamId":"...","roleCodes":["GOVERNMENT"],"visibleState":{"quarter":2,"outputIndex":101.2,"inflation":2.8,"unemployment":4.9,"lastReport":{"causalExplanation":["Fiscal demand supported output."]}},"version":12}
```

The exact state shape is model-specific. There are currently no dedicated student macro/market projection endpoints beyond generic recovery and action submission.

## 15. Instructor consoles

`GET /api/v1/economics/macro/sessions/{id}/console` (Instructor) returns session status/phase/quarter/version, team full states, decisions, predictions, pending participants/readiness, scheduled shocks, causal contributions, detected trade-offs, conceptual scores, objectives, and event history. `GET /api/v1/economics/competitive-market/sessions/{id}/console` returns market team state, submissions, and filtered team events. Console data is instructor-only and should not be reused in student projector mode.

## 16–17. Execution and results

`POST /api/v1/sessions/{sessionId}/rounds/current/execute` requires Instructor ownership and body `{teamId,executionId}`. `executionId` is the caller command identifier; exactly one transaction wins per session/team/round. The response is `ExecuteRoundResult` with execution ID, round number, and metrics. On race/duplicate expect 409 (`round.already_executed`, `concurrency.conflict`, or `session.status_conflict`). Wait for `ResultsAvailable`/state-change notification, then refetch state/console; do not calculate results in the browser.

Macro result state includes economic indicators, `MacroRoundReport` contributions/conflicts/assessments/objectives/causalExplanation, plus model-visible projections. Market `MarketRoundResult` includes transactions, price, quantity, unmatched demand/supply, consumer/producer/total surplus, unrealized gains, government revenue, deadweight loss, tax wedge, and causal explanation. Stored snapshots/reports are authoritative and replayable.

## 18–19. Discussion and debrief

Discussion prompts are in the frozen manifest presentation and macro `GET /debrief`; there is no separate discussion-response endpoint. Discussion is currently read-only and no discussion readiness/response submission exists. Macro debrief: `GET /api/v1/economics/macro/sessions/{id}/debrief`, Instructor only, returns team quarter replays, average conceptual score, achieved objectives, and event history. CompetitiveMarket currently has no `/debrief` endpoint; use its console/replay plus generic history. This is a confirmed gap.

## 20. Analytics

Macro analytics routes (Instructor only): `GET /analytics`, `/comparison`, and `/cohort-summary` all return `MacroSessionAnalytics` containing student/role/team/session slices and comments. Values include raw counts (`PredictionCount`, `CorrectPredictions`, `ObjectivesAchieved`, `ShockResponses`, `LagRecognitions`, `PolicyConflicts`) and normalized decimal scores (`ConceptualScore`, `PolicyReasoningScore`, `ObjectiveScore`, `EconomicOutcomeScore`). They can be returned live; no completion gate is enforced. There are no separate student/team/role/concept routes and no CompetitiveMarket analytics routes. Analytics are persisted-data projections, not historical re-simulations.

## 21. Replay

Macro `GET /api/v1/economics/macro/sessions/{id}/replay` returns report replay frames ordered by snapshot round. Each frame includes state, predictions, decisions, contributions, trade-offs, assessments, objectives, student-visible explanations, instructor explanations, and round events. CompetitiveMarket `GET /api/v1/economics/competitive-market/sessions/{id}/replay` returns instructor team snapshots/submissions/events but not the richer typed frame. Replays are ordered by persisted round/snapshot order and never execute a model.

## 22. Assessment comments

Macro Instructor routes: `GET /assessment-comments`; `POST /assessment-comments` with `{targetType,targetId,text}` and required `Idempotency-Key`; `PUT /assessment-comments/{commentId}` with `{text,expectedVersion}`. Target types are `Student|Role|Team|Session`; text is 1–4000 characters. Only the authoring instructor may update; session ownership is checked. Creation/update writes immutable audit rows. Stale version is 409 `concurrency.conflict`. CompetitiveMarket comment routes are not implemented.

## 23. Reports/exports

Macro `GET /api/v1/economics/macro/sessions/{id}/report?format=json|csv` is Instructor-only and synchronous. JSON returns `MacroSessionReport`; any format other than case-insensitive `csv` returns JSON. CSV is `text/csv` body with student analytics columns; no `Content-Disposition` filename header is set, so frontend should create a filename locally. There is no market report/export endpoint.

## 24. SignalR

Hub: `/hubs/sessions`; JWT bearer authentication is used by the normal API configuration. Invoke `JoinSession(sessionId)` after every connection/reconnect. The server validates participant membership or course ownership, joins `session:{id}` and participant team group `session:{id}:team:{teamId}`. There are no instructor-specific groups.

Outbox dispatch publishes event names including `SessionStateChanged`, `TeamChanged`, `ParticipantChanged`, `ActionSubmitted`, `ResultsAvailable`, and lifecycle events emitted by workflow/runtime. Payload shape is `{messageId,payload}` when routed through the API publisher. Treat every event as an invalidation and refetch `/state`; do not mutate authoritative state from the payload. Reconnect must re-authenticate, invoke `JoinSession`, then refetch state/history. There is no durable websocket replay.

## 25. Problem Details

Responses use `{type,title,status,detail,traceId}`; validation may add field errors. Stable domain codes include `authentication.invalid_credentials`, `authentication.invalid_token`, `authentication.required`, `idempotency.required`, `idempotency.conflict`, `concurrency.conflict`, `session.not_found`, `session.frozen`, `session.status_conflict`, `round.already_executed`, `scenario.validation_failed`, `scenario_draft.not_found`, `scenario_draft.immutable`, `definition.not_found`, `participant.not_enrolled`, `team.not_found`, `role.capacity_reached`, `action.unsupported`, `action.phase_invalid`, `action.capability_denied`, model-specific validation codes, and `macro.session_required`/`market.session_required`. HTTP 401 means authenticate/refresh; 403 means policy denied; 404 means missing or intentionally hidden resource; 409 means retry after refetch (except idempotency conflict, which requires a new key); 422 means fix the request/configuration; 500 is unexpected and should show traceId to support staff.

## 26. Optimistic concurrency

`ScenarioDraftDocument.Version` is required as `expectedVersion` on draft update/archive/publish. `SessionRecoveryView.Version`/inspection version identifies the session revision; command transitions use transactional checks even where no version is in the body. `AssessmentComment.Version` is required on comment update. On 409 `concurrency.conflict`, refetch, show the user’s unsaved edit, and let them merge/retry; never overwrite silently.

## 27. Idempotency

Use a fresh stable UUID (or equivalent opaque key) in `Idempotency-Key` for action submission, scenario draft create/clone, assessment comment create, and any retryable command where the endpoint accepts it. Action and comment keys are scoped to user/operation. Keep the key across network retries; changing payload under the same key is a conflict. Session execution uses body `executionId` for exactly-once rather than an HTTP header. Most classroom lifecycle commands do not currently expose idempotency headers.

## 28. Pagination/filtering/sorting

Scenario discovery implements one-based page pagination with default size 25, maximum size 100, and `totalCount`; see §4a. It supports lifecycle, model, and title-search filters and orders by most recently updated with an ID tie-breaker. Other collection endpoints do not implement pagination, cursors, filters, total counts, or client-selected sorting. Their arrays remain in backend-defined order.

## 29. Dates and enums

All timestamps are UTC `DateTimeOffset` ISO-8601 strings: token expiry, draft/session creation/update, publication, participant/readiness/submission/event/snapshot/comment timestamps. Nullable fields include session `teamId`, `visibleState`, scenario presentation, and optional reports.

Serialized enums/constant strings: platform roles `PlatformAdministrator|Instructor|Student`; macro intensities `Mild|Moderate|Strong`; macro assessment dimensions as named above; market policy `None|PriceCeiling|PriceFloor|PerUnitTax|PerUnitSubsidy`; market intensities `Mild|Moderate|Strong`; standard phase strings are manifest literals. Directions are lowercase `increase|decrease|stable`; action/capability identifiers are uppercase constants documented in §13.

## 30. Capability matrix

| Feature/data | Instructor | Macro Government | Central Bank | Business | Household/Labor | Market Buyer | Market Seller |
|---|---|---|---|---|---|---|---|
| Submit macro fiscal | owner/platform | `MACRO_SET_FISCAL_POLICY` | — | — | — | — | — |
| Submit macro monetary | owner/platform | — | `MACRO_SET_MONETARY_POLICY` | — | — | — | — |
| Submit macro business/household | owner/platform | — | — | `MACRO_SET_BUSINESS_STRATEGY` | `MACRO_SET_HOUSEHOLD_LABOR_STANCE` | — | — |
| Submit prediction | owner/platform | `MACRO_SUBMIT_PREDICTION` | same | same | same | `MARKET_SUBMIT_PREDICTION` | same |
| Bid/ask | — | — | — | — | — | `MARKET_SUBMIT_BID` | `MARKET_SUBMIT_ASK` |
| View macro institutional indicators | full console | role-specific `MACRO_VIEW_*` | role-specific | role-specific | role-specific | — | — |
| View market private info | full console | — | — | — | — | `MARKET_VIEW_BUYER_INFO` (buyer data) | `MARKET_VIEW_SELLER_INFO` (seller data) |
| View all macro internals | instructor or `MACRO_VIEW_ALL` | otherwise denied | otherwise denied | otherwise denied | otherwise denied | — | — |

Platform Instructor is required for every instructor console/authoring/analytics route; simulation capabilities never substitute for it.

## 31. End-to-end workflows

Instructor Macro: login → `POST /simulation-definitions` → authoring `GET /templates`/`POST /drafts`/`PUT` → `/validate` → `/preview` → `/publish` → `POST /classrooms/{id}/sessions` → create teams → add members → assign roles → `/commands/start` → advance phase → monitor `/state`/console → students submit prediction/actions and round readiness → advance to Locked/Simulation → `POST /rounds/current/execute` → refetch state/console/results → advance Discussion/next round → final `/debrief`, `/analytics`, `/replay`, `/report`.

Student Macro: login → instructor-provisioned session → `GET /state` → SignalR connect/JoinSession → read visible phase/role state → submit prediction/action with idempotency key → `PUT rounds/current/readiness` → refetch on events → view Results/Discussion through `/state`; no student debrief endpoint exists.

Instructor CompetitiveMarket: login → create definition → competitive authoring template/draft/edit/validate/preview/publish → create session/team/members/roles → start → monitor generic state and competitive console → execute rounds → competitive replay/console. Buyer/Seller follow the student sequence but submit bounded bid/ask payloads with their assigned capability; no market analytics/debrief/report endpoints exist.

Refresh/reconnect: restore access token or rotate refresh once; call `/state`, `/history` as permitted; connect hub, invoke `JoinSession`; refetch after each event. Never trust cached phase/readiness/results after reconnect.

## 32. Endpoint inventory

| Domain | Method | Route | Auth | DTO/result | Idempotent/concurrency | Screen |
|---|---|---|---|---|---|---|
| Auth | POST | `/api/v1/auth/register` | anon | RegisterRequest/I `{id,email}` | no | sign-up |
| Auth | POST | `/api/v1/auth/login` | anon | LoginRequest/IssuedTokens | no | sign-in |
| Auth | POST | `/api/v1/auth/refresh` | anon | RefreshRequest/IssuedTokens | rotation | token refresh |
| Auth | POST | `/api/v1/auth/logout` | anon | RefreshRequest/204 | safe retry | sign-out |
| Platform | GET | `/api/v1/models` | anon | `SimulationModelDescriptor[]` | no | model picker |
| Discovery | GET | `/api/v1/simulation-definitions/models` | anon | `AuthoringModelSummary[]` | no | authoring model picker |
| Discovery | GET | `/api/v1/simulation-definitions[/{id}]` | Instructor owner | `SimulationDefinitionSummary[]`/item | no | definition picker |
| Discovery | GET | `/api/v1/scenarios` | Instructor | `ScenarioLibraryPage` | paged/filtered | scenario library |
| Discovery | GET | `/api/v1/scenarios/{id}/versions` | Instructor owner | `PublishedScenarioVersionSummary[]` | immutable | version history |
| Discovery | GET | `/api/v1/scenario-versions/{id}` | Instructor owner | `PublishedScenarioVersionSummary` | immutable | version detail/launch picker |
| Discovery | GET | `/api/v1/simulation-definitions/{id}/scenario-versions` | Instructor owner | `PublishedScenarioVersionSummary[]` | immutable | definition history |
| Discovery | GET | `/api/v1/scenario-templates[/{model}/{template}]` | Instructor | `ScenarioTemplateSummary[]`/item | immutable | template picker |
| Classroom | POST | `/api/v1/courses` | Instructor | NamedCourseRequest/{id} | no | course setup |
| Classroom | POST | `/api/v1/courses/{courseId}/classrooms` | Instructor owner | NameRequest/{id} | no | classroom setup |
| Classroom | POST | `/api/v1/classrooms/{id}/enrollments` | Instructor owner | UserRequest/204 | no | roster |
| Runtime | POST | `/api/v1/simulation-definitions` | Instructor | NameRequest/{id} | no | definitions |
| Runtime | POST | `/api/v1/simulation-definitions/{id}/scenarios` | Instructor owner | PublishScenarioRequest/{id} | no | direct publish |
| Runtime | POST | `/api/v1/classrooms/{id}/sessions` | Instructor owner | CreateSessionRequest/{id} | no | launch |
| Runtime | POST | `/api/v1/sessions/{id}/teams` | Instructor owner | NameRequest/{id} | no | teams |
| Runtime | POST | `/api/v1/sessions/{id}/teams/{teamId}/members` | Instructor owner | UserRequest/204 | no | teams |
| Runtime | POST | `/api/v1/sessions/{id}/role-assignments` | Instructor owner | AssignRoleRequest/{id} | no | roles |
| Runtime | PUT | `/api/v1/sessions/{id}/participants/me/readiness` | Student | ReadinessRequest/204 | no | readiness |
| Runtime | PUT | `/api/v1/sessions/{id}/rounds/current/readiness` | Student | ReadinessRequest/204 | no | readiness |
| Runtime | POST | `/api/v1/sessions/{id}/commands/{start,pause,resume}` | Instructor owner | none/204 | transactional | controls |
| Runtime | POST | `/api/v1/sessions/{id}/commands/advance-phase` | Instructor owner | AdvancePhaseRequest/204 | transactional | controls |
| Runtime | GET | `/api/v1/sessions/{id}/state` | participant/owner | SessionRecoveryView | — | all session screens |
| Runtime | GET | `/api/v1/sessions/{id}/history` | participant/owner | HistoryItem[] | — | history |
| Runtime | POST | `/api/v1/sessions/{id}/actions` | Student | SubmitActionRequest/202 | Idempotency-Key | action forms |
| Runtime | POST | `/api/v1/sessions/{id}/rounds/current/execute` | Instructor owner | ExecuteRoundRequest/ExecuteRoundResult | exactly once | execution |
| Macro | GET | `/economics/macro/sessions/{id}/console` | Instructor owner | MacroInstructorConsole | — | macro console |
| Macro | GET | `/economics/macro/sessions/{id}/debrief` | Instructor owner | MacroSessionDebrief | — | macro debrief |
| Macro | GET | `/economics/macro/sessions/{id}/analytics|comparison|cohort-summary` | Instructor owner | MacroSessionAnalytics | — | analytics |
| Macro | GET | `/economics/macro/sessions/{id}/replay` | Instructor owner | replay frames | — | replay |
| Macro | GET | `/economics/macro/sessions/{id}/report?format=json|csv` | Instructor owner | report/text | — | export |
| Macro | GET/POST/PUT | `/economics/macro/sessions/{id}/assessment-comments[/{commentId}]` | Instructor owner | comments | create idempotent/versioned | assessment |
| Macro | all | `/economics/macro/scenario-authoring/...` | Instructor owner | typed authoring DTOs | create/clone/versioned | authoring |
| Market | GET/POST/PUT | `/economics/competitive-market/scenario-authoring/...` | Instructor owner | typed market DTOs | create/clone/versioned | authoring |
| Market | GET | `/economics/competitive-market/sessions/{id}/console` | Instructor owner | MarketInstructorConsole | — | market console |
| Market | GET | `/economics/competitive-market/sessions/{id}/replay` | Instructor owner | MarketTeamConsole[] | — | market replay |
| Realtime | hub | `/hubs/sessions` | JWT | SignalR methods/events | refetch authoritative state | live sync |

## 33. OpenAPI and source references

Swagger UI is `/swagger` in Development; the machine-readable document is `/swagger/v1/swagger.json`. It is suitable for generating transport types, but generated types do not encode ownership, phase, capability, or Problem Details semantics; retain this handoff as behavioral documentation.

Source map: authentication `Program.cs`, `Identity/TokenServices.cs`; generic classroom/runtime routes `Api/ClassroomEndpoints.cs`, `Application/Classrooms/WorkflowContracts.cs`, `Infrastructure/Persistence/EfClassroomWorkflow.cs`, `EfRuntimeStore.cs`; macro authoring/gameplay/analytics `Api/MacroGameplayEndpoints.cs`, `Simulations.Economics/Macroeconomics/MacroScenarioAuthoring.cs`, `MacroClassroomGameplay.cs`, `MacroLearningAnalytics.cs`; market authoring/gameplay `Api/CompetitiveMarketEndpoints.cs`, `Simulations.Economics/CompetitiveMarket/*`; SignalR and Problem Details `Api/Program.cs`; persistence mappings/migrations `Infrastructure/Persistence/PlatformDbContext.cs` and `Migrations/*`.

## Confirmed frontend integration gaps

- No `/me` or course/classroom/session list APIs. Pagination is currently specific to scenario discovery.
- No join codes/share links/self-join flow; instructor must provision participants.
- No student-specific debrief endpoint; generic student recovery is the available projection.
- CompetitiveMarket has no analytics, debrief, report/export, assessment-comment, or rich typed replay endpoint (only console/replay).
- No discussion-response submission or discussion readiness API.
- No session end/reset/remove-participant endpoints.
- No instructor-specific SignalR groups and no durable websocket event replay.
- Direct generic scenario publication accepts arbitrary manifests; typed authoring routes are the safe frontend path.
- Market configuration currently exposes aggregate demand/supply parameters; private valuations/costs are generated by the model and are not author-configurable arrays.
- Report downloads do not set a filename/content-disposition header.
### Session setup correction commands

Instructor-owned draft session setup supports server-authoritative corrections. Each command requires the current `session.version` from `GET /api/v1/sessions/{sessionId}/setup`. Routes are `POST /sessions/{sessionId}/teams/{teamId}/rename` (`{name,expectedVersion}`), `/teams/{teamId}/delete`, `/teams/{teamId}/members/{studentId}/remove`, `/participants/{studentId}/move` (`{targetTeamId,expectedVersion}`), and `/role-assignments/{assignmentId}/unassign`. Commands are authorized to the owning instructor, rejected after the session leaves Draft, audited, and return `concurrency.conflict` when the version is stale. Idempotency persistence for these new correction routes remains a backend follow-up; the frontend guards duplicate submissions while a command is pending.
