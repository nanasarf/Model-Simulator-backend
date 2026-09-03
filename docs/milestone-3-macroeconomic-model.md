# Milestone 3: bounded short-run macroeconomic country model

## Boundary

`Economics.ShortRunMacro:1.0.0` is an economics-module implementation of the existing `ISimulationModel` contract. The generic platform knows only its identifier, version, action payloads, state JSON, capabilities and metrics. Registration occurs at the API composition root. No economics dependency is introduced into Domain, Application, Identity, Infrastructure or Simulations.Core.

## Educational purpose

The model is a deterministic quarterly teaching model of aggregate-demand/short-run aggregate-supply adjustment. It is designed to make causal mechanisms, institutional roles, policy lags, trade-offs and conflicting policy choices visible. It is not a forecasting model and must not be presented as an empirical estimate of a real country.

## Roles and intended capabilities

- Government: `MACRO_SET_FISCAL_POLICY`, `MACRO_VIEW_FISCAL`
- Central Bank: `MACRO_SET_MONETARY_POLICY`, `MACRO_VIEW_MONETARY`
- Business: `MACRO_SET_BUSINESS_STRATEGY`, `MACRO_VIEW_BUSINESS`
- Household/Labor: `MACRO_SET_HOUSEHOLD_LABOR_STANCE`, `MACRO_VIEW_HOUSEHOLD`
- Instructor/scenario engine: `MACRO_TRIGGER_SHOCK`
- Optional instructor overview: `MACRO_VIEW_ALL`

Roles and capabilities remain scenario data; these names are merely the vocabulary accepted by this module.

## Qualitative actions

Players choose a direction and `Mild`, `Moderate`, or `Strong` intensity. They never submit GDP, inflation, unemployment or score values. Each policy action may include a prediction of the direction of output, inflation and unemployment plus a causal explanation. The module validates the closed vocabulary and derives numeric impulses internally.

## Mechanisms

The calculation is composed from independently testable mechanisms:

1. Fiscal transmission converts spending/tax stance into aggregate-demand pressure, the public balance and debt.
2. Monetary transmission converts stance into a policy-rate change and lagged interest-sensitive demand.
3. Business behavior converts production/investment stance and confidence into demand and short-run capacity pressure.
4. Household/labor behavior converts consumption and wage-bargaining stance into demand and wage-cost pressure.
5. Shock transmission applies configured or instructor-triggered demand, supply, confidence and productivity disturbances.
6. Aggregate adjustment combines demand and supply pressure into a bounded output gap, inflation and unemployment path.
7. Expectations update provides persistence without treating expectations as perfectly rational.

Mechanisms emit named causal contributions. The orchestrator aggregates them and publishes an auditable causal report; no single class owns the entire macro calculation.

## Core relationships

- Expansionary fiscal policy raises aggregate demand and worsens the fiscal balance, all else equal.
- Monetary tightening raises the policy rate and lowers interest-sensitive aggregate demand with a lag.
- Stronger demand raises short-run output and inflation pressure and tends to lower cyclical unemployment.
- Adverse supply pressure raises inflation while reducing output.
- Productivity improvements expand potential output and reduce cost pressure.
- Wage pressure supports household income/demand but also raises short-run unit-cost pressure.

Coefficients are scenario parameters constrained to safe pedagogical ranges. State changes are bounded to prevent numerical instability. Identical initial configuration, ordered actions, seed and round always produce identical output.

## Interaction and conflict

Actions are processed together, not sequentially as independent mini-games. The report detects fiscal/monetary opposition, demand expansion during an adverse supply shock, business contraction against household demand expansion, and simultaneous strong wage/price pressure. Conflicts are diagnostics, not automatic mistakes: a defensible role objective may intentionally oppose another role.

## Assessment and objectives

Prediction assessment is distinct from economic success. Directional predictions are compared with the resulting changes using a tolerance band; causal-keyword checks provide limited, transparent evidence of mechanism recognition. The model reports a conceptual score separately from objective attainment. Objectives include configurable ranges for inflation, unemployment, output gap and debt; role-specific objective summaries may conflict.

## Information asymmetry

Every role sees headline output, inflation and unemployment. Fiscal, monetary, business and household capabilities unlock only their institutional indicators and relevant diagnostics. `MACRO_VIEW_ALL` receives the full state and causal report. Hidden state is removed by the model projection, not merely hidden in the UI.

## Assumptions

- Sticky prices and wages in the short run.
- A stable, downward-sloping short-run relationship between cyclical output and unemployment.
- An expectations-augmented inflation process with bounded adaptive expectations.
- Fiscal and monetary policy affect aggregate demand; monetary effects are partially lagged.
- One aggregate household sector, business sector and government; no distributional heterogeneity.
- Institutions can implement the selected qualitative stance within the quarter.
- Potential output evolves slowly except for explicit productivity shocks.

## Deliberate limitations

Excluded from this milestone: international trade, exchange rates, detailed banking/financial markets, asset prices, sovereign default, heterogeneous agents, sectoral production, long-run Solow growth, endogenous innovation, detailed tax instruments and empirical country calibration. Outcomes illustrate model logic and should be debriefed alongside these limitations.

