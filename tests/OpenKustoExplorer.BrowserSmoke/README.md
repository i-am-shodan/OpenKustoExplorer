# Browser performance environment

The Browser smoke project contains two complementary performance environments:

- `npm run responsiveness` measures isolated large-editor, tab, result, paging, and recording paths.
- `npm run power-user` runs sustained mixed workflows in one long-lived Browser session.

## One-command power-user run

From PowerShell:

```powershell
.\run-power-user.ps1 -Cycles 8 -Trace
```

The launcher builds the Release Web host, starts that exact executable on port 5216, waits for health, runs the workload, and stops only the process it launched. It refuses to take over an occupied port.

Use `-InstallDependencies` on a new machine to run `npm ci` and install Playwright Chromium. Use `-NoBuild` only after a current Release build exists.

Useful variants:

```powershell
# Fail when an action exceeds its latency budget.
.\run-power-user.ps1 -Cycles 8 -EnforceBudgets

# Simulate a slower workstation, scale its budgets, and retain a Playwright trace.
.\run-power-user.ps1 -Cycles 8 -CpuThrottle 4 -BudgetMultiplier 4 -Trace

# Watch the Browser workload run interactively.
.\run-power-user.ps1 -Cycles 3 -Headful

# Stress result work without creating recorded sessions.
.\run-power-user.ps1 -Cycles 12 -RecordEvery 0

# Probe repeated recorded-session growth. Failures still write the JSON diagnostic report.
.\run-power-user.ps1 -Cycles 3 -RecordEvery 1
```

## Workload

Each measured cycle performs real keyboard and button interactions:

1. Switch among twelve 6,000-line query tabs, edit, and undo.
2. Replace query text and run a deterministic 5,000-row query.
3. Page forward/back, search all rows, clear filters, and sort in three states.
4. Navigate Sessions, Graph, and Query workspaces, including a rapid navigation burst.
5. At the configured interval, record a 5,000-row result, open its retained 500-row history, and return to Query.

The JSON report includes per-action samples and p50/p95/max summaries, correlated long tasks and animation frames, operation summaries, memory samples, budget violations, browser errors, and the twenty slowest actions. The runner scales the normally bounded Browser performance timeline up to 10,000 entries for sustained runs. Wall-clock budgets are enforced only with `-EnforceBudgets`; deterministic work-count assertions always run.

Open a retained trace with:

```powershell
npx playwright show-trace ..\..\artifacts\performance\browser-power-user-trace.zip
```

For reliable comparisons, use the same Release build, machine power mode, browser version, viewport, cycle count, and CPU-throttle setting. Close unrelated high-CPU applications before enforcing budgets.