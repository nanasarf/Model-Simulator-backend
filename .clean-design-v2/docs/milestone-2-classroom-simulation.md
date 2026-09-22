# Milestone 2: classroom simulation architecture

## Scope and dependency rule

Milestone 2 is an end-to-end proof of the generic classroom runtime. The platform projects deal only in definitions, manifests, roles, capabilities, rules, actions, phases, state and events. `SimulationPlatform.Simulations.Economics` remains an API composition-root dependency and implements the pre-existing `ISimulationModel`; Domain, Application, Identity and Infrastructure do not reference it.

## Persistence model

Authoring data is stored in `education` and `definitions`; frozen runtime data is stored in `runtime`.

- `education.organizations -> courses -> classrooms -> enrollments` establishes tenant ownership and student eligibility.
- `definitions.simulation_definitions -> scenario_versions -> rules` stores published authoring versions. Published versions are append-only through the application API.
- `runtime.sessions -> session_manifests` freezes the complete model/configuration version, phases, transition graph, roles, capabilities, actions and rule AST at session creation.
- `runtime.sessions -> teams -> participants` establishes session membership. `role_assignments` stores assignment history plus a frozen capability set.
- `action_submissions`, `executions` and `snapshots` hold commands, exactly-once round claims and authoritative state.
- `events` is append-only history; `integration.outbox_messages` and `audit.audit_records` provide reliable publication and security audit trails.

Important uniqueness constraints cover enrollment, team names per session, participant membership, active assignment identity, action idempotency, snapshots per round and round executions. Session `Version` is an optimistic concurrency token.

## Authorization flow

JWT authentication first establishes platform identity and a non-teacher-configurable platform role. Endpoint policies admit only Instructor/Administrator or Student operations. Every application operation then resolves ownership or membership from persisted server data:

1. Instructor commands join Session -> Classroom -> Course and require the authenticated subject to be the course owner.
2. Student commands require enrollment, session participation, team membership and the selected active role assignment.
3. An action requires the capability frozen on that assignment, an available phase, a matching frozen rule decision and model validation.
4. SignalR group admission repeats persisted owner/participant checks. Possession of an identifier grants no authority.

A simulation role named `President` therefore grants only its frozen simulation capabilities; its holder remains a platform Student.

## Rule AST

Rules are JSON data, never executable text. Supported nodes are `all`, `any`, `not`, `exists`, `contains` and scalar `comparison` (`eq`, `neq`, `gt`, `gte`, `lt`, `lte`). Facts are allow-listed to `runtime.phase`, `actor.capabilities`, `team.id` and `action.code`. Parsing enforces depth/node budgets and rejects unknown nodes, facts, operators and non-scalar comparison values. Priority is deterministic; a matching deny overrides allows. Runtime evaluation reads the immutable session manifest.

## Transaction boundaries and concurrency

Each mutating classroom command runs in one PostgreSQL serializable transaction. The transaction includes state change, event append, audit entry and outbox insert. Database uniqueness is the final arbiter for racing duplicates. Session transitions additionally update the concurrency token. Round execution atomically claims `(SessionId, TeamId, RoundNumber)`, validates phase, executes the deterministic model, writes the snapshot/results and advances the state machine; only one claim can commit.

## Outbox and realtime

Business transactions never publish directly. They append an outbox message in the same commit. A background dispatcher claims rows with a lease, publishes to authorized SignalR session groups and marks completion; failures retain the row with bounded exponential retry. Messages are notifications, not authoritative state. Reconnecting clients call `GET /api/v1/sessions/{id}/state` and history endpoints to rebuild from persisted truth.

## Layer invariants

- Identity: credentials and platform roles cannot be created or escalated by simulation configuration.
- Domain/Application: commands express intent; phases and model contracts remain discipline-neutral.
- Infrastructure: ownership, transactionality, immutable manifests, deterministic persistence, audit and reliable publication are enforced at the database boundary.
- Simulation module: owns discipline-specific validation, calculation and role-sensitive projection; it cannot authorize platform resources or publish realtime messages.
- API: maps authenticated requests and policies to commands; it never accepts authoritative state or scores from clients.

## Session lifecycle

`Draft` permits team/participant/assignment configuration. Start performs readiness and role-minimum validation, freezes use of configuration, initializes one deterministic team snapshot, and enters `Running`. The manifest transition graph governs Briefing/Deliberation/Decision/Locked/Results/Reflection/Completed. Entering Simulation is reserved for the transactional execute command. Pause/Resume changes availability without changing phase. Completed sessions are immutable.

