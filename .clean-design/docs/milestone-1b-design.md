# Milestone 1B infrastructure design

This document is the implementation contract for production persistence, identity, authorization,
rules, transactional simulation execution, and reliable publication. The simulation kernel remains
discipline-neutral. No project except a simulation implementation may reference
`SimulationPlatform.Simulations.Economics`.

## Database schema

PostgreSQL uses explicit schemas owned by one modular monolith:

| Schema | Tables | Guaranteed boundary |
|---|---|---|
| `identity` | `users`, `roles`, `user_roles`, `user_claims`, `user_logins`, `user_tokens`, `refresh_tokens` | Credentials and platform roles only; never simulation roles |
| `education` | `organizations`, `organization_memberships`, `courses`, `course_instructors`, `classrooms`, `enrollments` | Every classroom resource has an organization/course ownership path |
| `definitions` | `simulation_definitions`, immutable definition versions, scenario versions, roles, capabilities, role capabilities, actions, rules | Published versions are immutable and sessions reference exact versions |
| `runtime` | `sessions`, `participants`, `teams`, memberships, role assignments, rounds, action submissions, snapshots, events, idempotency records, executions | Server-authoritative state and append-only history |
| `integration` | `outbox_messages` | Post-commit reliable notification delivery |
| `audit` | `audit_records` | Security- and instructor-sensitive activity |

Flexible model state, action payloads, declarative rules, and event data use JSONB with an explicit
schema/configuration version. Ownership, lifecycle, identity, authorization, uniqueness, and query
keys remain relational.

Critical unique constraints are `(user_id, operation, idempotency_key)`, one execution per round,
one round number per session/team scope, published version numbers per definition, and active role
assignment constraints. Foreign keys prevent orphaned ownership. Optimistic concurrency uses an
application-managed `version` column so behavior is portable and visible.

## Authentication and authorization flow

1. ASP.NET Core Identity verifies credentials using its password hasher.
2. Login issues a short-lived JWT and a random refresh token. Only the refresh-token SHA-256 hash is stored.
3. Refresh rotates the token in one transaction. Reuse of a rotated token revokes the entire family.
4. JWT validation verifies signature, issuer, audience, lifetime, security stamp, and platform-role claims.
5. Endpoint policies establish platform authority (`Instructor`, `Student`, administrator).
6. Resource authorization loads the protected resource through its ownership path and evaluates a
   requirement handler against the authenticated subject. Caller-supplied owner/user IDs are never trusted.
7. Session participation is checked independently from platform authority.
8. Simulation authority then resolves the active role assignment, required capability, current phase,
   declarative rules, payload schema, and model validation—in that order.

A simulation role named `President` is runtime data and can never satisfy an ASP.NET platform policy.
Cross-tenant reads return 404 when revealing existence would disclose private resources.

## Declarative rule AST

Rules are stored as a typed JSON AST, never executable text:

```json
{
  "kind": "all",
  "children": [
    { "kind": "comparison", "fact": "runtime.phase", "operator": "eq", "value": "Decision" },
    { "kind": "contains", "fact": "actor.capabilities", "value": "SET_POLICY" },
    { "kind": "comparison", "fact": "state.capacity", "operator": "gt", "value": 0 }
  ]
}
```

Supported nodes are `all`, `any`, `not`, `comparison`, `contains`, and `exists`. Comparison operators
are `eq`, `neq`, `gt`, `gte`, `lt`, and `lte`. Facts must be registered, typed, and supplied through an
`IRuleFactSource`; arbitrary object traversal and reflection are prohibited. AST depth, child count,
string length, and evaluation work are bounded. Effects are controlled data such as allow/deny action,
constraint, transition permission, visibility, objective status, or score component. Deny wins at equal
priority; otherwise rules evaluate by descending priority and stable identifier. Unknown facts/operators,
type mismatches, or conflicting terminal rules fail closed.

## Transaction boundaries

### Action submission

One database transaction performs idempotency reservation, session/team/assignment lookup, capability
and phase checks, rule evaluation, model validation against the current snapshot, action insertion,
simulation-event append, audit append where required, and outbox insertion. Commit makes all visible
together. Same key plus same canonical request returns the original reference; same key plus a different
hash returns 409.

### Round execution

One transaction atomically changes `Locked` to `Simulation` using the expected concurrency version and
creates the unique execution record. Inputs are frozen. The deterministic model executes from that input
and stored seed. A final transaction conditionally writes the snapshot, metrics/event payload, changes
`Simulation` to `Results`, marks the execution complete, and inserts outbox messages. Only the holder of
the execution ID and expected version can commit. A retry resolves the existing execution/result.

Short models may execute inside one serializable transaction initially. The two-transaction claim/finalize
protocol is the durable boundary for longer models and avoids holding locks during computation.

### Refresh rotation and definition publication

Refresh-token rotation, family-reuse revocation, and immutable definition publication each occur within a
single transaction. No network or SignalR call occurs inside a business transaction.

## Outbox design

Domain/application operations insert an `OutboxMessage` in the same transaction as authoritative state.
Rows contain ID, message type, aggregate ID, serialized payload, occurred time, attempts, next-attempt time,
processed time, and last safe error. A hosted dispatcher claims batches using PostgreSQL
`FOR UPDATE SKIP LOCKED`, publishes through an `IIntegrationEventPublisher`, and marks success. Failures use
bounded exponential backoff and eventually become operational alerts/dead letters. Consumers and client
events use message IDs for deduplication. SignalR remains a notification transport; reconnecting clients
reload authoritative REST state.

## Layer invariants

### Domain

- Contains no EF, ASP.NET, Identity, SignalR, PostgreSQL, or discipline-specific references.
- Enforces valid lifecycle transitions and immutable historical facts.
- Uses `IClock` inputs rather than ambient time and never uses global randomness.

### Application

- Orchestrates commands through interfaces and returns stable failures.
- Never trusts client-supplied identity or ownership.
- Performs authorization/capability/rule/model checks before state changes.
- Contains no economics dependency and sends no SignalR messages.

### Identity

- Owns credentials, platform roles, access tokens, refresh rotation, and revocation.
- Does not know simulation roles, actions, capabilities, models, or outcomes.
- Never persists or logs plaintext credentials or refresh tokens.

### Infrastructure

- Implements persistence, transactions, locking, clock, token signing, outbox delivery, and audit storage.
- Does not contain economics calculations or select behavior based on economics action codes.
- Database constraints remain the final defense against duplicate or orphaned state.

### API

- Resolves caller identity exclusively from the authenticated principal.
- Maps transport requests to commands, applies policies, and emits RFC Problem Details.
- Contains no business calculations and never exposes persistence entities.

### Simulation implementations

- Are trusted, versioned deployments implementing the generic model contract.
- Are deterministic for identical frozen inputs and seeds.
- Cannot access EF, HTTP, identity, SignalR, or platform authorization services.

### Publication

- Authoritative state and its publication request commit atomically.
- Publication is at least once; messages carry stable IDs and consumers must deduplicate.
- No websocket delivery is considered authoritative state.
