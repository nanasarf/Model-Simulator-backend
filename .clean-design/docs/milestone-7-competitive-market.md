# Milestone 7: Economics.CompetitiveMarket:1.0.0

Competitive Market is a second plugin implementation of the generic simulation contract. It introduces no market concepts into Domain, Application, Identity, Infrastructure, or the runtime engine.

The model accepts qualitative classroom role actions represented as bounded buyer bids and seller asks (the server validates ownership/capability and payload bounds). It deterministically sorts bids descending and asks ascending, matches while willingness-to-pay covers effective cost, and records immutable round state with price, quantity, unmatched demand/supply, transactions, consumer surplus, producer surplus, total surplus, unrealized gains, tax revenue, wedge, and deadweight loss. Price ceilings/floors and per-unit taxes/subsidies are configuration-owned policy instruments; students never submit outcomes or curves.

Scenario authoring is available under `/api/v1/economics/competitive-market/scenario-authoring` with typed starting market conditions, participant quantities, policy settings, shocks, rounds, roles, visibility, objectives, prompts, and plugin-owned assessment dimensions. Draft ownership, validation, deterministic preview, publication, version freezing, idempotency, and optimistic concurrency reuse the existing generic infrastructure. Published sessions retain their frozen manifests.

Buyer and seller private information is projected only when the corresponding capability is present. The plugin’s causal explanations describe matching, wedges, shortages/surpluses, and welfare without exposing internal implementation calculations. Existing ShortRunMacro behavior is unchanged.
