import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { chromium } from "playwright";

const baseUrl = process.env.OPEN_KUSTO_EXPLORER_WEB_URL ?? "http://127.0.0.1:5216";
const browserChannel = process.env.OPEN_KUSTO_EXPLORER_BROWSER_CHANNEL;
const outputPath = process.env.OPEN_KUSTO_EXPLORER_LARGE_EDITOR_OUTPUT
  ?? path.resolve("artifacts", "performance", "browser-large-editor.json");
const iterationCount = 8;

const browser = await chromium.launch({
  ...(browserChannel ? { channel: browserChannel } : {}),
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

const waitForPaint = page => page.evaluate(() => new Promise(resolve => {
  globalThis.requestAnimationFrame(() => globalThis.requestAnimationFrame(resolve));
}));

const invokeFixture = async (page, action) => {
  const accepted = await page.evaluate(
    fixtureAction => globalThis.openKustoExplorerInvokePerformanceFixture(fixtureAction),
    action);
  assert.equal(accepted, true, `The fixture action '${action}' was not accepted.`);
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
  const configuration = await page.evaluate(
    () => globalThis.openKustoExplorerIsReleaseBuild() ? "Release" : "Debug");
  await page.evaluate(() => globalThis.openKustoExplorerResetPerformanceProfile());
  await invokeFixture(page, "large-editor");
  await page.waitForFunction(
    () => {
      const profile = JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile());
      return profile.operations.some(operation =>
        operation.name === "editor.analysis.apply"
        && operation.itemCount >= 5000
        && operation.outcome === "completed");
    },
    null,
    { timeout: 120000 });
  await waitForPaint(page);

  const analysisProfile = await page.evaluate(
    () => JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile()));
  const analysis = analysisProfile.operations.find(operation =>
    operation.name === "editor.analysis.apply" && operation.itemCount >= 5000);
  assert.ok(analysis, "The 6,000-line fixture did not produce the expected semantic classifications.");
  await page.evaluate(() => globalThis.openKustoExplorerResetPerformanceProfile());

  const scrollDurations = [];
  for (let iteration = 0; iteration < iterationCount; iteration++) {
    const startedAt = await page.evaluate(() => performance.now());
    await page.keyboard.press(iteration % 2 === 0 ? "Control+End" : "Control+Home");
    await waitForPaint(page);
    const completedAt = await page.evaluate(() => performance.now());
    scrollDurations.push(completedAt - startedAt);
  }

  await page.keyboard.press("Control+Home");
  await waitForPaint(page);
  const pageDownDurations = [];
  for (let iteration = 0; iteration < 12; iteration++) {
    const startedAt = await page.evaluate(() => performance.now());
    await page.keyboard.press("PageDown");
    await waitForPaint(page);
    const completedAt = await page.evaluate(() => performance.now());
    pageDownDurations.push(completedAt - startedAt);
  }

  const profile = await page.evaluate(
    () => JSON.parse(globalThis.openKustoExplorerGetPerformanceProfile()));
  const sortedDurations = scrollDurations.toSorted((left, right) => left - right);
  const sortedPageDownDurations = pageDownDurations.toSorted((left, right) => left - right);
  const report = {
    analysis: {
      classificationCount: analysis.itemCount,
      duration: analysis.duration
    },
    configuration,
    fixture: { lines: 6000 },
    generatedAtUtc: new Date().toISOString(),
    scroll: {
      iterations: iterationCount,
      maximum: sortedDurations.at(-1),
      median: sortedDurations[Math.floor(sortedDurations.length / 2)],
      values: scrollDurations
    },
    pageDown: {
      iterations: pageDownDurations.length,
      maximum: sortedPageDownDurations.at(-1),
      median: sortedPageDownDurations[Math.floor(sortedPageDownDurations.length / 2)],
      values: pageDownDurations
    },
    longAnimationFrames: profile.animationFrames.map(frame => frame.duration),
    longTasks: profile.longTasks.map(task => task.duration)
  };

  assert.deepEqual(errors, []);
  assert.ok(report.scroll.maximum < 1000, `Large-editor scrolling stalled for ${report.scroll.maximum.toFixed(1)} ms.`);
  assert.ok(report.pageDown.maximum < 500, `Large-editor page scrolling stalled for ${report.pageDown.maximum.toFixed(1)} ms.`);
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  console.log(JSON.stringify({ outputPath, ...report }, null, 2));
} finally {
  await browser.close();
}
