import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { chromium } from "playwright";

const baseUrl = process.env.OPEN_KUSTO_EXPLORER_WEB_URL ?? "http://127.0.0.1:5216";
const outputPath = process.env.OPEN_KUSTO_EXPLORER_RESPONSIVENESS_OUTPUT
  ?? path.resolve("artifacts", "performance", "browser-responsiveness.json");
const screenshotDirectory = process.env.OPEN_KUSTO_EXPLORER_RESPONSIVENESS_SCREENSHOTS;
const largeDocumentCount = 12;
const largeDocumentLineCount = 6000;
const resultRowCount = 5000;
const retainedRecordingRowCount = 500;
const ignoredOutcomes = new Set(["canceled", "disposed", "failed", "superseded", "synchronized"]);
const budgetMultiplier = Number.parseFloat(
  process.env.OPEN_KUSTO_EXPLORER_PERFORMANCE_BUDGET_MULTIPLIER?.trim() || "1");
const enforceLatencyBudgets = process.env.OPEN_KUSTO_EXPLORER_ENFORCE_PERFORMANCE_BUDGETS === "1";
const latencyBudgets = {
  editorMedian: 400 * budgetMultiplier,
  recordingTotal: 3000 * budgetMultiplier,
  resultFirstPaint: 1650 * budgetMultiplier,
  resultPagePaint: 900 * budgetMultiplier,
  tabSwitchMedian: 800 * budgetMultiplier
};

assert.ok(Number.isFinite(budgetMultiplier) && budgetMultiplier > 0,
  "The performance budget multiplier must be a positive number.");

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

const completedOperations = (profile, name) => profile.operations.filter(
  operation => operation.name === name && !ignoredOutcomes.has(operation.outcome));

const waitForOperationCount = async (page, name, minimumCount, timeout = 120000) => {
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
    { timeout });
};

const waitForEditorSettled = async (page, minimumCount) => {
  await page.waitForFunction(
    requiredCount => {
      const profile = JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile());
      return profile.operations.filter(operation =>
        ["editor.analysis.apply", "editor.analysis.deferred"].includes(operation.name)
        && !["canceled", "disposed", "failed", "superseded"].includes(operation.outcome)).length >= requiredCount;
    },
    minimumCount,
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

const captureViewport = async (page, viewport, name) => {
  if (!screenshotDirectory) {
    return;
  }

  await page.setViewportSize(viewport);
  await waitForPaint(page);
  const canvasBounds = await page.locator("#out canvas").first().boundingBox();
  assert.ok(
    canvasBounds
      && canvasBounds.width >= viewport.width * 0.9
      && canvasBounds.height >= viewport.height * 0.9,
    `The result canvas does not fit the ${viewport.width}x${viewport.height} viewport.`);
  fs.mkdirSync(screenshotDirectory, { recursive: true });
  const screenshotPath = path.join(screenshotDirectory, name);
  await page.screenshot({ path: screenshotPath });
  assert.ok(fs.statSync(screenshotPath).size > 8000, `The screenshot '${name}' appears blank.`);
};

const measureAction = async (page, action) => {
  const startedAt = await page.evaluate(() => performance.now());
  await action();
  await waitForPaint(page);
  const completedAt = await page.evaluate(() => performance.now());
  return completedAt - startedAt;
};

const summarize = values => {
  const sorted = values.toSorted((left, right) => left - right);
  return {
    count: sorted.length,
    maximum: sorted.at(-1),
    median: sorted[Math.floor(sorted.length / 2)],
    values
  };
};

try {
  await waitForHost();
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => {
    if (message.type() === "error") {
      errors.push(message.text());
    }
  });

  await page.goto(
    `${baseUrl}/app/index.html?profile=1&fixture=performance`,
    { waitUntil: "domcontentloaded" });
  await page.locator(".startup").waitFor({ state: "detached", timeout: 120000 });
  await page.waitForFunction(
    () => typeof globalThis.openKustoExplorerInvokePerformanceFixture === "function",
    null,
    { timeout: 30000 });
  assert.equal(
    await page.evaluate(() => globalThis.openKustoExplorerIsReleaseBuild()),
    true,
    "Responsiveness acceptance requires a Release build.");

  await invokeFixture(page, "prepare");
  await invokeFixture(page, "large-editor");
  await waitForEditorSettled(page, 1);
  await page.evaluate(() => globalThis.openKustoExplorerResetPerformanceProfile());

  const editDurations = [];
  for (let iteration = 0; iteration < 3; iteration++) {
    const requiredAnalysisCount = iteration + 1;
    editDurations.push(await measureAction(page, async () => {
      await page.keyboard.press("Control+End");
      await page.keyboard.type(" ");
      await waitForEditorSettled(page, requiredAnalysisCount);
    }));
  }
  const editorProfile = await readProfile(page);

  await page.evaluate(() => globalThis.openKustoExplorerResetPerformanceProfile());
  const tabSwitchDurations = [];
  for (let iteration = 0; iteration < largeDocumentCount - 1; iteration++) {
    const requiredAnalysisCount = iteration + 1;
    tabSwitchDurations.push(await measureAction(page, async () => {
      await invokeFixture(page, "next-large-editor");
      await waitForEditorSettled(page, requiredAnalysisCount);
    }));
  }
  const tabsProfile = await readProfile(page);

  await page.evaluate(() => globalThis.openKustoExplorerResetPerformanceProfile());
  await invokeFixture(page, "large-result-query");
  await waitForOperationCount(page, "editor.analysis.apply", 1);
  await page.evaluate(() => globalThis.openKustoExplorerResetPerformanceProfile());
  const resultDuration = await measureAction(page, async () => {
    await page.keyboard.press("F5");
    await waitForOperationCount(page, "results.initial-page.paint", 1);
    await waitForOperationCount(page, "results.first-row.paint", 1);
  });
  await waitForOperationCount(page, "results.rows.remaining-project", 1);
  const resultProfile = await readProfile(page);
  assert.ok(
    completedOperations(resultProfile, "results.initial-page.paint")
      .some(operation => operation.itemCount === resultRowCount && operation.outcome === "rows:50"),
    "The bounded 50-row initial page metric is missing.");
  assert.ok(
    completedOperations(resultProfile, "results.rows.project")
      .some(operation => operation.itemCount === 50),
    "The initial projection was not bounded to 50 rows.");
  assert.ok(
    completedOperations(resultProfile, "results.rows.remaining-project")
      .some(operation => operation.itemCount === resultRowCount - 50 && operation.outcome === "deferred"),
    "The remaining row projection was not deferred.");

  await page.evaluate(() => globalThis.openKustoExplorerResetPerformanceProfile());
  const pageNavigationDuration = await measureAction(page, async () => {
    await invokeFixture(page, "next-result-page");
    await page.keyboard.press("Enter");
    await waitForOperationCount(page, "results.first-row.paint", 1);
  });
  const pagingProfile = await readProfile(page);
  assert.ok(
    completedOperations(pagingProfile, "results.rows.on-demand-project")
      .some(operation => operation.itemCount === 50),
    "Next-page navigation did not project exactly one 50-row page.");
  assert.ok(
    completedOperations(pagingProfile, "results.first-row.paint")
      .some(operation => operation.outcome === "row:50"),
    "Next-page navigation did not paint the expected first row.");

  await captureViewport(page, { width: 1440, height: 1000 }, "results-1440x1000.png");
  await captureViewport(page, { width: 390, height: 844 }, "results-390x844.png");
  await page.setViewportSize({ width: 1440, height: 1000 });
  await waitForPaint(page);

  await page.evaluate(() => globalThis.openKustoExplorerResetPerformanceProfile());
  const recordingDuration = await measureAction(page, async () => {
    await invokeFixture(page, "record-large-result");
    await waitForOperationCount(page, "fixture.recording.query", 1);
  });
  const recordingProfile = await readProfile(page);

  const recordingPersistence = completedOperations(
    recordingProfile,
    "recording.execution.persist");
  assert.ok(
    recordingPersistence.some(operation => operation.itemCount === resultRowCount),
    "The 5,000-row recording persistence metric is missing.");

  await page.evaluate(() => globalThis.openKustoExplorerResetPerformanceProfile());
  const recordedSessionOpenDuration = await measureAction(page, async () => {
    await invokeFixture(page, "sessions");
    await page.keyboard.press("Enter");
    await waitForOperationCount(page, "recording.session.materialize", 1);
  });
  const recordedSessionProfile = await readProfile(page);
  const recordingMaterialization = completedOperations(
    recordedSessionProfile,
    "recording.session.materialize");
  assert.ok(
    recordingMaterialization.some(operation => operation.itemCount === retainedRecordingRowCount),
    "The retained 500-row session materialization metric is missing.");

  const editorLatency = summarize(editDurations);
  const tabSwitchLatency = summarize(tabSwitchDurations);
  const report = {
    budgets: latencyBudgets,
    budgetsEnforced: enforceLatencyBudgets,
    configuration: "Release",
    editor: {
      documents: largeDocumentCount,
      linesPerDocument: largeDocumentLineCount,
      editLatency: editorLatency,
      operations: editorProfile.operations,
      longAnimationFrames: editorProfile.animationFrames,
      longTasks: editorProfile.longTasks
    },
    generatedAtUtc: new Date().toISOString(),
    recording: {
      queryRows: resultRowCount,
      retainedRows: retainedRecordingRowCount,
      sessionOpenDuration: recordedSessionOpenDuration,
      sessionOperations: recordedSessionProfile.operations,
      totalDuration: recordingDuration,
      operations: recordingProfile.operations,
      longAnimationFrames: recordingProfile.animationFrames,
      longTasks: recordingProfile.longTasks
    },
    results: {
      nextPageDuration: pageNavigationDuration,
      pagingLongAnimationFrames: pagingProfile.animationFrames,
      pagingLongTasks: pagingProfile.longTasks,
      pagingOperations: pagingProfile.operations,
      rows: resultRowCount,
      totalDuration: resultDuration,
      operations: resultProfile.operations,
      longAnimationFrames: resultProfile.animationFrames,
      longTasks: resultProfile.longTasks
    },
    tabs: {
      documents: largeDocumentCount,
      switchLatency: tabSwitchLatency,
      operations: tabsProfile.operations,
      longAnimationFrames: tabsProfile.animationFrames,
      longTasks: tabsProfile.longTasks
    }
  };

  assert.deepEqual(errors, []);
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  console.log(JSON.stringify({
    editor: report.editor.editLatency,
    outputPath,
    recordingDuration: report.recording.totalDuration,
    resultDuration: report.results.totalDuration,
    resultPageDuration: report.results.nextPageDuration,
    tabs: report.tabs.switchLatency
  }, null, 2));
  if (enforceLatencyBudgets) {
    assert.ok(
      editorLatency.median <= latencyBudgets.editorMedian,
      `Large-document edit median ${editorLatency.median.toFixed(1)} ms exceeded ${latencyBudgets.editorMedian.toFixed(1)} ms.`);
    assert.ok(
      tabSwitchLatency.median <= latencyBudgets.tabSwitchMedian,
      `Large-tab switch median ${tabSwitchLatency.median.toFixed(1)} ms exceeded ${latencyBudgets.tabSwitchMedian.toFixed(1)} ms.`);
    assert.ok(
      resultDuration <= latencyBudgets.resultFirstPaint,
      `Large-result first paint ${resultDuration.toFixed(1)} ms exceeded ${latencyBudgets.resultFirstPaint.toFixed(1)} ms.`);
    assert.ok(
      pageNavigationDuration <= latencyBudgets.resultPagePaint,
      `Result-page paint ${pageNavigationDuration.toFixed(1)} ms exceeded ${latencyBudgets.resultPagePaint.toFixed(1)} ms.`);
    assert.ok(
      recordingDuration <= latencyBudgets.recordingTotal,
      `Large-result recording ${recordingDuration.toFixed(1)} ms exceeded ${latencyBudgets.recordingTotal.toFixed(1)} ms.`);
  }
} finally {
  await browser.close();
}