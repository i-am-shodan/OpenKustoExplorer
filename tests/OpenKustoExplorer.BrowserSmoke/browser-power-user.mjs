import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { chromium } from "playwright";

const parsePositiveInteger = (name, fallback, allowZero = false) => {
  const value = Number.parseInt(process.env[name] ?? `${fallback}`, 10);
  assert.ok(
    Number.isInteger(value) && (allowZero ? value >= 0 : value > 0),
    `${name} must be ${allowZero ? "non-negative" : "positive"}.`);
  return value;
};

const parsePositiveNumber = (name, fallback) => {
  const value = Number.parseFloat(process.env[name]?.trim() || `${fallback}`);
  assert.ok(Number.isFinite(value) && value > 0, `${name} must be positive.`);
  return value;
};

const baseUrl = process.env.OPEN_KUSTO_EXPLORER_WEB_URL ?? "http://127.0.0.1:5216";
const outputPath = process.env.OPEN_KUSTO_EXPLORER_POWER_USER_OUTPUT
  ?? path.resolve("artifacts", "performance", "browser-power-user.json");
const tracePath = process.env.OPEN_KUSTO_EXPLORER_POWER_USER_TRACE;
const cycleCount = parsePositiveInteger("OPEN_KUSTO_EXPLORER_POWER_USER_CYCLES", 5);
const warmupCycleCount = parsePositiveInteger(
  "OPEN_KUSTO_EXPLORER_POWER_USER_WARMUP_CYCLES",
  1,
  true);
const recordingInterval = parsePositiveInteger(
  "OPEN_KUSTO_EXPLORER_POWER_USER_RECORD_EVERY",
  5,
  true);
const cpuThrottle = parsePositiveNumber("OPEN_KUSTO_EXPLORER_POWER_USER_CPU_THROTTLE", 1);
const budgetMultiplier = parsePositiveNumber(
  "OPEN_KUSTO_EXPLORER_PERFORMANCE_BUDGET_MULTIPLIER",
  1);
const enforceBudgets = process.env.OPEN_KUSTO_EXPLORER_ENFORCE_PERFORMANCE_BUDGETS === "1";
const headless = process.env.OPEN_KUSTO_EXPLORER_HEADLESS !== "0";
const profileCapacity = Math.min(10000, Math.max(1000, cycleCount * 300));
const ignoredOutcomes = new Set(["canceled", "disposed", "superseded", "synchronized"]);
const actionBudgets = Object.fromEntries(Object.entries({
  "editor.insert": 400,
  "editor.undo": 400,
  "query.microEdit": 300,
  "query.replace": 600,
  "query.run": 1800,
  "recording.largeResult": 3500,
  "recording.returnQuery": 1200,
  "recording.sessionOpen": 4000,
  "result.clearFilters": 1200,
  "result.page.next": 900,
  "result.page.previous": 900,
  "result.search": 1800,
  "result.sort.ascending": 1800,
  "result.sort.clear": 1800,
  "result.sort.descending": 1800,
  "tab.large": 1200,
  "tab.query": 1200,
  "workspace.burst": 1000,
  "workspace.graph": 1000,
  "workspace.query": 1000,
  "workspace.sessions": 1200
}).map(([name, budget]) => [name, budget * budgetMultiplier]));

const percentile = (sortedValues, fraction) => {
  if (sortedValues.length === 0) {
    return null;
  }

  const index = Math.min(
    sortedValues.length - 1,
    Math.max(0, Math.ceil(sortedValues.length * fraction) - 1));
  return sortedValues[index];
};

const summarizeDurations = values => {
  const sortedValues = values.toSorted((left, right) => left - right);
  return {
    count: sortedValues.length,
    maximum: percentile(sortedValues, 1),
    mean: sortedValues.reduce((total, value) => total + value, 0) / sortedValues.length,
    p50: percentile(sortedValues, 0.5),
    p95: percentile(sortedValues, 0.95)
  };
};

const groupBy = (values, keySelector) => {
  const groups = new Map();
  for (const value of values) {
    const key = keySelector(value);
    const group = groups.get(key) ?? [];
    group.push(value);
    groups.set(key, group);
  }

  return groups;
};

const browser = await chromium.launch({
  headless,
  args: [
    "--use-angle=swiftshader",
    "--enable-webgl",
    "--ignore-gpu-blocklist",
    "--disable-gpu-sandbox",
    "--enable-unsafe-swiftshader",
    "--enable-precise-memory-info"
  ]
});
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
let traceStarted = false;

const waitForHost = async () => {
  for (let attempt = 0; attempt < 120; attempt++) {
    try {
      if ((await fetch(`${baseUrl}/healthz`)).ok) {
        return;
      }
    } catch {
      // The host can still be starting.
    }

    await new Promise(resolve => setTimeout(resolve, 500));
  }

  throw new Error(`The Web host did not become ready at ${baseUrl}.`);
};

const readProfile = page => page.evaluate(
  () => JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile()));

const getOperationCount = (page, operationName) => page.evaluate(
  ({ name, excludedOutcomes }) => {
    const profile = JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile());
    return profile.operations.filter(operation =>
      operation.name === name && !excludedOutcomes.includes(operation.outcome)).length;
  },
  { excludedOutcomes: Array.from(ignoredOutcomes), name: operationName });

const waitForOperationCount = async (page, operationName, requiredCount, timeout = 120000) => {
  await page.waitForFunction(
    ({ name, count, excludedOutcomes }) => {
      const profile = JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile());
      return profile.operations.filter(operation =>
        operation.name === name && !excludedOutcomes.includes(operation.outcome)).length >= count;
    },
    { count: requiredCount, excludedOutcomes: Array.from(ignoredOutcomes), name: operationName },
    { timeout });
};

const waitForOperationAfter = async (
  page,
  operationName,
  minimumStartTime,
  expectedOutcome,
  timeout = 120000) => {
  await page.waitForFunction(
    ({ name, startTime, outcome, excludedOutcomes }) => {
      const profile = JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile());
      return profile.operations.some(operation =>
        operation.name === name
        && operation.startTime >= startTime
        && !excludedOutcomes.includes(operation.outcome)
        && (outcome === null || operation.outcome === outcome));
    },
    {
      excludedOutcomes: Array.from(ignoredOutcomes),
      name: operationName,
      outcome: expectedOutcome,
      startTime: minimumStartTime
    },
    { timeout });
};

const getEditorSettledCount = page => page.evaluate(() => {
  const profile = JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile());
  return profile.operations.filter(operation =>
    ["editor.analysis.apply", "editor.analysis.deferred"].includes(operation.name)
    && !["canceled", "disposed", "failed", "superseded"].includes(operation.outcome)).length;
});

const waitForEditorSettled = async (page, requiredCount) => {
  await page.waitForFunction(
    count => {
      const profile = JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile());
      return profile.operations.filter(operation =>
        ["editor.analysis.apply", "editor.analysis.deferred"].includes(operation.name)
        && !["canceled", "disposed", "failed", "superseded"].includes(operation.outcome)).length >= count;
    },
    requiredCount,
    { timeout: 120000 });
};

const invokeFixture = async (page, action) => {
  const accepted = await page.evaluate(
    fixtureAction => globalThis.openKustoExplorerInvokePerformanceFixture(fixtureAction),
    action);
  assert.equal(accepted, true, `The fixture action '${action}' was not accepted.`);
};

const waitForPaint = page => page.evaluate(() => new Promise(resolve => {
  globalThis.requestAnimationFrame(() => globalThis.requestAnimationFrame(resolve));
}));

const overlaps = (entry, action) => {
  const entryEnd = entry.startTime + entry.duration;
  return entry.startTime < action.endTime && entryEnd > action.startTime;
};

const createActionSummaries = actions => Object.fromEntries(
  Array.from(groupBy(actions, action => action.name).entries())
    .toSorted(([left], [right]) => left.localeCompare(right))
    .map(([name, entries]) => {
      const budget = actionBudgets[name] ?? null;
      return [name, {
        ...summarizeDurations(entries.map(entry => entry.duration)),
        budget,
        budgetViolationCount: budget === null
          ? 0
          : entries.filter(entry => entry.duration > budget).length,
        maximumLongTask: Math.max(0, ...entries.map(entry => entry.maximumLongTask)),
        stalledActionCount: entries.filter(entry => entry.longTaskCount > 0).length
      }];
    }));

const createOperationSummaries = operations => Object.fromEntries(
  Array.from(groupBy(operations, operation => operation.name).entries())
    .toSorted(([left], [right]) => left.localeCompare(right))
    .map(([name, entries]) => [name, {
      ...summarizeDurations(entries.map(entry => entry.duration)),
      outcomes: Object.fromEntries(
        Array.from(groupBy(entries, entry => entry.outcome).entries())
          .map(([outcome, outcomeEntries]) => [outcome, outcomeEntries.length]))
    }]));

try {
  if (tracePath) {
    fs.mkdirSync(path.dirname(tracePath), { recursive: true });
    await context.tracing.start({ screenshots: true, snapshots: true, sources: true });
    traceStarted = true;
  }

  const page = await context.newPage();
  page.setDefaultTimeout(120000);
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => {
    if (message.type() === "error") {
      errors.push(message.text());
    }
  });

  if (cpuThrottle > 1) {
    const cdpSession = await context.newCDPSession(page);
    await cdpSession.send("Emulation.setCPUThrottlingRate", { rate: cpuThrottle });
  }

  await waitForHost();
  await page.goto(
    `${baseUrl}/app/index.html?profile=1&fixture=performance&profileCapacity=${profileCapacity}`,
    { waitUntil: "domcontentloaded" });
  await page.locator(".startup").waitFor({ state: "detached", timeout: 120000 });
  await page.waitForFunction(
    () => typeof globalThis.openKustoExplorerInvokePerformanceFixture === "function",
    null,
    { timeout: 30000 });
  assert.equal(
    await page.evaluate(() => globalThis.openKustoExplorerIsReleaseBuild()),
    true,
    "Power-user profiling requires a Release build.");

  await invokeFixture(page, "prepare");
  await invokeFixture(page, "large-editor");
  await waitForEditorSettled(page, 1);

  let actions = [];
  let memorySamples = [];

  const writeFailureReport = async (name, cycle, error) => {
    const profile = await readProfile(page).catch(() => null);
    const failureReport = {
      actions,
      configuration: {
        cpuThrottle,
        cycles: cycleCount,
        headless,
        recordingInterval,
        viewport: { height: 1000, width: 1440 },
        warmupCycles: warmupCycleCount
      },
      errors,
      failure: {
        action: name,
        cause: error?.cause?.message ?? null,
        cycle,
        message: error?.message ?? String(error),
        stack: error?.stack ?? null
      },
      generatedAtUtc: new Date().toISOString(),
      memorySamples,
      profile
    };
    fs.mkdirSync(path.dirname(outputPath), { recursive: true });
    fs.writeFileSync(outputPath, `${JSON.stringify(failureReport, null, 2)}\n`, "utf8");
  };

  const measureAction = async (name, cycle, record, action) => {
    const startTime = await page.evaluate(() => performance.now());
    try {
      await action();
      await waitForPaint(page);
    } catch (error) {
      const actionError = new Error(
        `Power-user action '${name}' failed in cycle ${cycle}.`,
        { cause: error });
      await writeFailureReport(name, cycle, actionError);
      throw actionError;
    }

    const endTime = await page.evaluate(() => performance.now());
    if (record) {
      actions.push({ cycle, duration: endTime - startTime, endTime, name, startTime });
    }
  };

  const performAndWaitForEditor = async action => {
    const count = await getEditorSettledCount(page);
    await action();
    await waitForEditorSettled(page, count + 1);
  };

  const activateAndWait = async (target, operationName) => {
    const count = await getOperationCount(page, operationName);
    await invokeFixture(page, `activate-${target}`);
    await waitForOperationCount(page, operationName, count + 1);
  };

  const activatePageAndWait = async (target, expectedOutcome) => {
    const count = await getOperationCount(page, "results.page.change");
    await invokeFixture(page, `result-page-${target}`);
    await waitForOperationCount(page, "results.page.change", count + 1);
    const profile = await readProfile(page);
    const operation = profile.operations
      .filter(candidate => candidate.name === "results.page.change")
      .at(-1);
    assert.equal(operation?.outcome, expectedOutcome);
  };

  const runCycle = async (cycle, record, includeRecording) => {
    const timed = (name, action) => measureAction(name, cycle, record, action);

    await timed("tab.large", () => performAndWaitForEditor(
      () => invokeFixture(page, "next-large-editor")));
    await timed("editor.insert", () => performAndWaitForEditor(async () => {
      await page.keyboard.press("Control+End");
      await page.keyboard.type(" ");
    }));
    await timed("editor.undo", () => performAndWaitForEditor(
      () => page.keyboard.press("Control+Z")));
    await timed("tab.query", () => invokeFixture(page, "query-document"));
    await timed("query.replace", async () => {
      const inputCount = await getOperationCount(page, "editor.input.process");
      await invokeFixture(page, "next-large-result-query");
      await waitForOperationCount(page, "editor.input.process", inputCount + 1);
    });
    await timed("query.microEdit", async () => {
      const inputCount = await getOperationCount(page, "editor.input.process");
      await page.keyboard.press("Control+End");
      await page.keyboard.type(" ");
      await waitForOperationCount(page, "editor.input.process", inputCount + 1);
    });

    await timed("query.run", async () => {
      const queryStartedAt = await page.evaluate(() => performance.now());
      await page.keyboard.press("F5");
      await waitForOperationAfter(
        page,
        "results.initial-page.paint",
        queryStartedAt,
        "rows:50");
      await waitForOperationAfter(
        page,
        "results.first-row.paint",
        queryStartedAt,
        "row:0");
    });

    await timed("result.page.next", () => activatePageAndWait(
      "next",
      "page:2"));
    await timed("result.page.previous", () => activatePageAndWait(
      "previous",
      "page:1"));

    await timed("result.search", async () => {
      const searchCount = await getOperationCount(page, "results.search.paint");
      const firstRowCount = await getOperationCount(page, "results.first-row.paint");
      await invokeFixture(page, "result-search");
      await page.keyboard.press("Control+A");
      await page.keyboard.type("needle-7");
      await page.keyboard.press("Enter");
      await waitForOperationCount(page, "results.search.paint", searchCount + 1);
      await waitForOperationCount(page, "results.first-row.paint", firstRowCount + 1);
    });
    await timed("result.clearFilters", () => activateAndWait(
      "clear-result-filters",
      "results.first-row.paint"));

    for (const [name, direction] of [
      ["result.sort.ascending", "ascending"],
      ["result.sort.descending", "descending"],
      ["result.sort.clear", "none"]
    ]) {
      await timed(name, async () => {
        const sortCount = await getOperationCount(page, "results.sort.paint");
        await invokeFixture(page, "sort-first-result-column");
        await page.keyboard.press("Enter");
        await waitForOperationCount(page, "results.sort.paint", sortCount + 1);
        const profile = await readProfile(page);
        const sortOperation = profile.operations
          .filter(operation => operation.name === "results.sort.paint")
          .at(-1);
        assert.equal(sortOperation?.outcome, direction);
      });
    }

    await timed("workspace.sessions", () => activateAndWait(
      "sessions",
      "workspace.sessions.paint"));
    await timed("workspace.query", () => activateAndWait(
      "connections",
      "workspace.query.paint"));
    await timed("workspace.graph", () => activateAndWait(
      "graph",
      "workspace.graph.paint"));
    await timed("workspace.query", () => activateAndWait(
      "connections",
      "workspace.query.paint"));

    await timed("workspace.burst", async () => {
      const queryPaintCount = await getOperationCount(page, "workspace.query.paint");
      await invokeFixture(page, "sessions");
      await page.keyboard.press("Enter");
      await invokeFixture(page, "graph");
      await page.keyboard.press("Enter");
      await invokeFixture(page, "connections");
      await page.keyboard.press("Enter");
      await waitForOperationCount(page, "workspace.query.paint", queryPaintCount + 1);
    });

    if (includeRecording) {
      await timed("recording.largeResult", async () => {
        const recordingCount = await getOperationCount(page, "fixture.recording.query");
        await invokeFixture(page, "record-large-result");
        await waitForOperationCount(page, "fixture.recording.query", recordingCount + 1);
      });
      await timed("recording.sessionOpen", () => activateAndWait(
        "sessions",
        "workspace.sessions.paint"));
      await timed("recording.returnQuery", () => activateAndWait(
        "connections",
        "workspace.query.paint"));
    }

    if (record) {
      memorySamples.push(await page.evaluate(currentCycle => {
        const memory = globalThis.performance.memory;
        return {
          cycle: currentCycle,
          jsHeapLimit: memory?.jsHeapSizeLimit ?? null,
          totalJsHeap: memory?.totalJSHeapSize ?? null,
          usedJsHeap: memory?.usedJSHeapSize ?? null
        };
      }, cycle));
    }
  };

  for (let cycle = 0; cycle < warmupCycleCount; cycle++) {
    await runCycle(cycle, false, false);
  }

  await page.evaluate(() => globalThis.openKustoExplorerResetPerformanceProfile());
  actions = [];
  memorySamples = [];
  const runStartTime = await page.evaluate(() => performance.now());
  for (let cycle = 0; cycle < cycleCount; cycle++) {
    const includeRecording = recordingInterval > 0 && (cycle + 1) % recordingInterval === 0;
    await runCycle(cycle + 1, true, includeRecording);
  }
  const runEndTime = await page.evaluate(() => performance.now());
  const profile = await readProfile(page);

  actions = actions.map(action => {
    const longTasks = profile.longTasks.filter(entry => overlaps(entry, action));
    const longAnimationFrames = profile.animationFrames.filter(entry => overlaps(entry, action));
    return {
      ...action,
      longAnimationFrameCount: longAnimationFrames.length,
      longTaskCount: longTasks.length,
      maximumLongAnimationFrame: Math.max(0, ...longAnimationFrames.map(entry => entry.duration)),
      maximumLongTask: Math.max(0, ...longTasks.map(entry => entry.duration)),
      totalLongTaskDuration: longTasks.reduce((total, entry) => total + entry.duration, 0)
    };
  });

  const actionSummaries = createActionSummaries(actions);
  const budgetViolations = Object.entries(actionSummaries)
    .filter(([, summary]) => summary.budgetViolationCount > 0)
    .map(([name, summary]) => ({
      budget: summary.budget,
      maximum: summary.maximum,
      name,
      p95: summary.p95,
      violationCount: summary.budgetViolationCount
    }));
  const failedOperations = profile.operations.filter(operation => operation.outcome === "failed");
  const report = {
    actions,
    actionSummaries,
    budgetMultiplier,
    budgetViolations,
    budgetsEnforced: enforceBudgets,
    configuration: {
      cpuThrottle,
      cycles: cycleCount,
      headless,
      profileCapacity,
      profileCapacity,
      recordingInterval,
      viewport: { height: 1000, width: 1440 },
      warmupCycles: warmupCycleCount
    },
    errors,
    generatedAtUtc: new Date().toISOString(),
    memorySamples,
    operationSummaries: createOperationSummaries(profile.operations),
    profile,
    runDuration: runEndTime - runStartTime,
    slowestActions: actions
      .toSorted((left, right) => right.duration - left.duration)
      .slice(0, 20)
  };

  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  console.log(JSON.stringify({
    actionSummaries,
    budgetViolations,
    cycles: cycleCount,
    outputPath,
    runDuration: report.runDuration,
    slowestActions: report.slowestActions.slice(0, 10).map(action => ({
      cycle: action.cycle,
      duration: action.duration,
      longTasks: action.longTaskCount,
      name: action.name
    })),
    tracePath: tracePath ?? null
  }, null, 2));

  assert.deepEqual(errors, []);
  assert.deepEqual(failedOperations, []);
  assert.ok(
    profile.operations.some(operation =>
      operation.name === "results.rows.project" && operation.itemCount === 50),
    "No bounded 50-row result projection was observed.");
  assert.ok(
    profile.operations.some(operation =>
      operation.name === "results.rows.on-demand-project" && operation.itemCount === 50),
    "No bounded on-demand result page projection was observed.");
  assert.ok(
    profile.operations.some(operation => operation.name === "editor.analysis.deferred"),
    "No oversized-document analysis deferral was observed.");
  if (recordingInterval > 0 && recordingInterval <= cycleCount) {
    assert.ok(
      profile.operations.some(operation =>
        operation.name === "recording.execution.persist" && operation.itemCount === 5000),
      "No 5,000-row recording persistence operation was observed.");
  }

  if (enforceBudgets) {
    assert.deepEqual(budgetViolations, []);
  }
} finally {
  if (traceStarted) {
    await context.tracing.stop({ path: tracePath });
  }

  await context.close();
  await browser.close();
}