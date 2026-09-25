import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { chromium } from "playwright";

const baseUrl = process.env.OPEN_KUSTO_EXPLORER_WEB_URL ?? "http://127.0.0.1:5216";
const iterationCount = Number.parseInt(
  process.env.OPEN_KUSTO_EXPLORER_PERFORMANCE_ITERATIONS ?? "5",
  10);
const outputPath = process.env.OPEN_KUSTO_EXPLORER_PERFORMANCE_OUTPUT
  ?? path.resolve("artifacts", "performance", "browser-release.json");
const ignoredOutcomes = new Set(["canceled", "disposed", "failed", "superseded", "synchronized"]);

assert.ok(Number.isInteger(iterationCount) && iterationCount > 0, "Iteration count must be a positive integer.");

const browser = await chromium.launch({
  headless: true,
  args: [
    "--use-angle=swiftshader",
    "--enable-webgl",
    "--ignore-gpu-blocklist",
    "--disable-gpu-sandbox",
    "--enable-unsafe-swiftshader"
  ]
});

const waitForHost = async () => {
  let lastFailure = "no response";
  for (let attempt = 0; attempt < 120; attempt++) {
    try {
      const response = await fetch(`${baseUrl}/healthz`);
      if (response.ok) {
        return;
      }

      lastFailure = `HTTP ${response.status}: ${await response.text()}`;
    } catch (error) {
      lastFailure = error?.message ?? String(error);
    }

    await new Promise(resolve => setTimeout(resolve, 500));
  }

  throw new Error(`The Web host did not become ready at ${baseUrl}: ${lastFailure}`);
};

const readProfile = page => page.evaluate(
  () => JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile()));

const completedOperations = (profile, name) => profile.operations.filter(
  operation => operation.name === name && !ignoredOutcomes.has(operation.outcome));

const waitForCompletedOperation = async (page, name, minimumCount) => {
  await page.waitForFunction(
    ({ operationName, requiredCount, excludedOutcomes }) => {
      const profile = JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile());
      return profile.operations.filter(operation =>
        operation.name === operationName
        && !excludedOutcomes.includes(operation.outcome)).length >= requiredCount;
    },
    {
      excludedOutcomes: Array.from(ignoredOutcomes),
      operationName: name,
      requiredCount: minimumCount
    },
    { timeout: 30000 });
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

const percentile = (sortedValues, fraction) => {
  if (sortedValues.length === 0) {
    return null;
  }

  const index = Math.min(sortedValues.length - 1, Math.ceil(sortedValues.length * fraction) - 1);
  return sortedValues[index];
};

const summarize = values => {
  const sortedValues = values.toSorted((left, right) => left - right);
  return {
    count: sortedValues.length,
    maximum: percentile(sortedValues, 1),
    median: percentile(sortedValues, 0.5),
    p95: percentile(sortedValues, 0.95)
  };
};

const summarizeProfiles = iterations => {
  const operationDurations = new Map();
  const startupDurations = [];
  const eventDurations = [];
  const longAnimationFrameDurations = [];
  const longTaskDurations = [];

  for (const iteration of iterations) {
    const startupMeasure = iteration.startup.measures.find(measure => measure.name === "startup.total");
    if (startupMeasure) {
      startupDurations.push(startupMeasure.duration);
    }

    for (const operation of iteration.interactions.operations) {
      if (ignoredOutcomes.has(operation.outcome)) {
        continue;
      }

      const durations = operationDurations.get(operation.name) ?? [];
      durations.push(operation.duration);
      operationDurations.set(operation.name, durations);
    }

    eventDurations.push(...iteration.interactions.events.map(event => event.duration));
    longAnimationFrameDurations.push(
      ...iteration.interactions.animationFrames.map(frame => frame.duration));
    longTaskDurations.push(...iteration.interactions.longTasks.map(task => task.duration));
  }

  return {
    events: summarize(eventDurations),
    longAnimationFrames: summarize(longAnimationFrameDurations),
    longTasks: summarize(longTaskDurations),
    operations: Object.fromEntries(
      Array.from(operationDurations.entries())
        .toSorted(([left], [right]) => left.localeCompare(right))
        .map(([name, durations]) => [name, summarize(durations)])),
    startup: summarize(startupDurations)
  };
};

const runIteration = async iteration => {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => {
    if (message.type() === "error") {
      errors.push(message.text());
    }
  });

  try {
    const fixtureUrl = `${baseUrl}/app/index.html?profile=1&fixture=performance&iteration=${iteration}`;
    await page.goto(fixtureUrl, { waitUntil: "domcontentloaded" });
    await page.locator(".startup").waitFor({ state: "detached", timeout: 120000 });
    await page.waitForFunction(
      () => typeof globalThis.openKustoExplorerInvokePerformanceFixture === "function",
      null,
      { timeout: 30000 });
    assert.equal(
      await page.evaluate(() => globalThis.openKustoExplorerIsReleaseBuild()),
      true,
      "Browser performance acceptance requires a Release build.");

    const canvas = page.locator("#out canvas").first();
    await canvas.waitFor({ state: "visible", timeout: 30000 });
    const bounds = await canvas.boundingBox();
    assert.ok(bounds && bounds.width >= 800 && bounds.height >= 500, "The Avalonia canvas is not usable.");

    const startup = await readProfile(page);
    assert.ok(
      startup.measures.some(measure => measure.name === "startup.total"),
      "The startup profile is missing startup.total.");

    await invokeFixture(page, "prepare");
    await waitForPaint(page);
    await page.evaluate(() => globalThis.openKustoExplorerResetPerformanceProfile());

    await invokeFixture(page, "editor");
    await page.keyboard.press("Control+End");
    await page.keyboard.type("\n| where Mes", { delay: 8 });
    await waitForCompletedOperation(page, "editor.completion.paint", 1);
    await page.keyboard.press("Escape");

    await page.keyboard.press("F5");
    await waitForCompletedOperation(page, "results.first-row.paint", 1);
    await waitForCompletedOperation(page, "results.containers.realized", 1);

    await invokeFixture(page, "result-search");
    await page.keyboard.type("needle-7", { delay: 8 });
    await waitForCompletedOperation(page, "results.search.paint", 1);
    await waitForCompletedOperation(page, "results.first-row.paint", 2);

    await invokeFixture(page, "sessions");
    await page.keyboard.press("Enter");
    await waitForCompletedOperation(page, "workspace.sessions.paint", 1);
    await invokeFixture(page, "connections");
    await page.keyboard.press("Enter");
    await waitForCompletedOperation(page, "workspace.query.paint", 1);
    await invokeFixture(page, "sessions");
    await page.keyboard.press("Enter");
    await waitForCompletedOperation(page, "workspace.sessions.paint", 2);
    await invokeFixture(page, "connections");
    await page.keyboard.press("Enter");
    await waitForCompletedOperation(page, "workspace.query.paint", 2);
    await invokeFixture(page, "graph");
    await page.keyboard.press("Enter");
    await waitForCompletedOperation(page, "workspace.graph.paint", 1);
    await invokeFixture(page, "connections");
    await page.keyboard.press("Enter");
    await waitForCompletedOperation(page, "workspace.query.paint", 3);
    await invokeFixture(page, "graph");
    await page.keyboard.press("Enter");
    await waitForCompletedOperation(page, "workspace.graph.paint", 2);

    await waitForPaint(page);
    const interactions = await readProfile(page);
    assert.deepEqual(errors, []);
    assert.deepEqual(
      completedOperations(interactions, "workspace.sessions.data").map(operation => operation.outcome),
      ["refreshed", "cached"]);
    assert.deepEqual(
      completedOperations(interactions, "workspace.graph.data").map(operation => operation.outcome),
      ["refreshed", "cached"]);

    const initialContainers = completedOperations(interactions, "results.containers.realized")
      .find(operation => operation.itemCount === 5000);
    assert.ok(initialContainers, "The initial 5,000-row container metric is missing.");
    const realizedCount = Number.parseInt(initialContainers.outcome.replace("realized:", ""), 10);
    assert.ok(realizedCount > 0 && realizedCount < 200, `Expected bounded row realization; observed ${realizedCount}.`);

    const initialFirstRow = completedOperations(interactions, "results.first-row.paint")
      .find(operation => operation.itemCount === 5000);
    assert.equal(initialFirstRow?.outcome, "row:0");
    return { interactions, startup };
  } finally {
    await context.close();
  }
};

const iterations = [];
try {
  await waitForHost();
  for (let iteration = 1; iteration <= iterationCount; iteration++) {
    iterations.push(await runIteration(iteration));
  }

  const report = {
    configuration: "Release",
    fixture: { columns: 6, rows: 5000 },
    generatedAtUtc: new Date().toISOString(),
    iterationCount,
    iterations,
    summary: summarizeProfiles(iterations)
  };
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  console.log(JSON.stringify({ outputPath, summary: report.summary }, null, 2));
} finally {
  await browser.close();
}