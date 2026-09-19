import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { chromium } from "playwright";

const baseUrl = process.env.OPEN_KUSTO_EXPLORER_WEB_URL ?? "http://127.0.0.1:5216";
const artifactDirectory = process.env.OPEN_KUSTO_EXPLORER_SMOKE_ARTIFACTS;
const browserChannel = process.env.OPEN_KUSTO_EXPLORER_BROWSER_CHANNEL;
const importOnly = process.env.OPEN_KUSTO_EXPLORER_IMPORT_ONLY === "1";
const profileDirectory = fs.mkdtempSync(path.join(os.tmpdir(), "oke-browser-smoke-"));
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

const focusFixtureTarget = async (page, target) => {
  const accepted = await page.evaluate(
    fixtureTarget => globalThis.openKustoExplorerInvokePerformanceFixture(fixtureTarget),
    target);
  assert.equal(accepted, true, `The Browser fixture target '${target}' is unavailable.`);
};

const waitForFixtureTarget = async (page, target) => {
  await page.waitForFunction(
    fixtureTarget => globalThis.openKustoExplorerInvokePerformanceFixture(fixtureTarget),
    target,
    { timeout: 5000 });
};

const captureScreenshot = async (page, name) => {
  if (!artifactDirectory) {
    return;
  }

  fs.mkdirSync(artifactDirectory, { recursive: true });
  const screenshotPath = path.join(artifactDirectory, name);
  await page.screenshot({ path: screenshotPath });
  assert.ok(fs.statSync(screenshotPath).size > 8000, `The screenshot '${name}' appears blank.`);
};

const verifyDashboardTimeRange = async viewport => {
  const context = await browser.newContext({ viewport });
  const page = await context.newPage();
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => {
    if (message.type() === "error") {
      errors.push(message.text());
    }
  });

  try {
    await page.goto(
      `${baseUrl}/app/index.html?profile=1&fixture=performance`,
      { waitUntil: "domcontentloaded" });
    await page.locator(".startup").waitFor({ state: "detached", timeout: 120000 });
    await page.waitForFunction(
      () => typeof globalThis.openKustoExplorerInvokePerformanceFixture === "function",
      null,
      { timeout: 30000 });

    const canvas = page.locator("#out canvas").first();
    await canvas.waitFor({ state: "visible", timeout: 30000 });
    const bounds = await canvas.boundingBox();
    assert.ok(
      bounds
        && bounds.width >= viewport.width * 0.9
        && bounds.height >= viewport.height * 0.9,
      `The ${viewport.width}x${viewport.height} Avalonia canvas is not correctly framed.`);

    await focusFixtureTarget(page, "dashboard");
    await page.keyboard.press("Enter");
    await focusFixtureTarget(page, "create-dashboard");
    await page.keyboard.press("Enter");
    await page.keyboard.type(`Parity smoke ${viewport.width}`);
    await page.keyboard.press("Enter");
    await focusFixtureTarget(page, "dashboard-time-range");
    await captureScreenshot(page, `dashboard-${viewport.width}x${viewport.height}.png`);

    await page.keyboard.press("Alt+ArrowDown");
    await page.keyboard.press("End");
    await page.keyboard.press("Enter");
    await waitForFixtureTarget(page, "custom-time-range-start-date");
    await captureScreenshot(page, `dashboard-custom-${viewport.width}x${viewport.height}.png`);
    assert.deepEqual(errors, []);
  } finally {
    await context.close();
  }
};

const verifyProfileGuidance = async viewport => {
  const context = await browser.newContext({ viewport });
  const page = await context.newPage();
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => {
    if (message.type() === "error") {
      errors.push(message.text());
    }
  });

  try {
    await page.goto(`${baseUrl}/app/index.html`, { waitUntil: "domcontentloaded" });
    await page.locator(".startup").waitFor({ state: "detached", timeout: 120000 });
    await page.evaluate(() => {
      globalThis.browserSmokeCanceledProfile = globalThis.openKustoExplorerSelectProfile();
    });
    const dialog = page.locator("#kusto-profile-import");
    await dialog.waitFor({ state: "visible" });
    const bounds = await dialog.boundingBox();
    assert.ok(
      bounds
        && bounds.x >= 0
        && bounds.y >= 0
        && bounds.x + bounds.width <= viewport.width
        && bounds.y + bounds.height <= viewport.height,
      `The profile guidance does not fit the ${viewport.width}x${viewport.height} viewport.`);
    await captureScreenshot(page, `kusto-profile-import-${viewport.width}x${viewport.height}.png`);
    await page.locator("#kusto-profile-cancel").click();
    assert.equal(await page.evaluate(() => globalThis.browserSmokeCanceledProfile), "");
    assert.deepEqual(errors, []);
  } finally {
    await context.close();
  }
};

try {
  const connectionFilePath = path.join(profileDirectory, "UserConnections.xml");
  const groupFilePath = path.join(profileDirectory, "UserConnectionGroups.xml");
  const recoveryDirectory = path.join(profileDirectory, "Recovery");
  const recoveryFilePath = path.join(recoveryDirectory, "Recovered.kebak");
  fs.mkdirSync(recoveryDirectory);
  fs.writeFileSync(
    connectionFilePath,
    "<ArrayOfServerDescriptionBase />",
    "utf8");
  fs.writeFileSync(groupFilePath, "<ArrayOfConnectionGroup />", "utf8");
  fs.writeFileSync(recoveryFilePath, "{}", "utf8");
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => {
    if (message.type() === "error") {
      errors.push(message.text());
    }
  });

  await waitForHost();
  await page.goto(`${baseUrl}/app/index.html`, { waitUntil: "domcontentloaded" });
  await page.locator(".startup").waitFor({ state: "detached", timeout: 120000 });
  const canvas = page.locator("#out canvas").first();
  await canvas.waitFor({ state: "visible", timeout: 30000 });
  const bounds = await canvas.boundingBox();
  assert.ok(bounds && bounds.width >= 800 && bounds.height >= 500, "The Avalonia canvas is not usable.");

  await page.evaluate(() => {
    globalThis.browserSmokeDirectoryPicker = globalThis.showDirectoryPicker;
    globalThis.browserSmokeDirectoryPickerCalls = 0;
    globalThis.showDirectoryPicker = () => {
      globalThis.browserSmokeDirectoryPickerCalls++;
      throw new DOMException("Directory access blocked", "SecurityError");
    };
    globalThis.browserSmokeProfile = globalThis.openKustoExplorerSelectProfile();
  });
  const profileDialog = page.locator("#kusto-profile-import");
  await profileDialog.waitFor({ state: "visible" });
  assert.match(await page.locator("#kusto-profile-path").textContent(), /%LOCALAPPDATA%\\Kusto\.Explorer/);
  await captureScreenshot(page, "kusto-profile-import-1440x1000.png");
  const profileFileChooserPromise = page.waitForEvent("filechooser");
  await page.locator("#kusto-profile-choose-files").click();
  const profileFileChooser = await profileFileChooserPromise;
  await profileFileChooser.setFiles([connectionFilePath, groupFilePath]);
  await page.locator("#kusto-profile-status").getByText("2 profile files and 0 recovery files selected").waitFor();
  assert.equal(await page.locator("#kusto-profile-import-selected").isEnabled(), true);

  const recoveryFileChooserPromise = page.waitForEvent("filechooser");
  await page.locator("#kusto-profile-choose-recovery").click();
  const recoveryFileChooser = await recoveryFileChooserPromise;
  await recoveryFileChooser.setFiles(recoveryFilePath);
  await page.locator("#kusto-profile-status").getByText("2 profile files and 1 recovery file selected").waitFor();
  await page.locator("#kusto-profile-import-selected").click();
  const selectedProfile = JSON.parse(await page.evaluate(() => globalThis.browserSmokeProfile));
  assert.equal(selectedProfile.length, 3);
  assert.ok(selectedProfile.some(file => file.path === "UserConnections.xml"));
  assert.ok(selectedProfile.some(file => file.path === "UserConnectionGroups.xml"));
  assert.ok(selectedProfile.some(file => file.path === "Recovery/Recovered.kebak"));
  assert.equal(await page.evaluate(() => globalThis.browserSmokeDirectoryPickerCalls), 0);
  assert.equal(await profileDialog.isVisible(), false);
  await page.evaluate(() => {
    globalThis.showDirectoryPicker = globalThis.browserSmokeDirectoryPicker;
    delete globalThis.browserSmokeDirectoryPicker;
    delete globalThis.browserSmokeDirectoryPickerCalls;
  });

  const storageResult = await page.evaluate(async () => {
    const partition = `ci-smoke-${crypto.randomUUID()}`;
    const original = "{\"version\":1,\"clusters\":[]}";
    await globalThis.openKustoExplorerStorageWrite(partition, "connections", original);
    const backup = await globalThis.openKustoExplorerStorageCreateBackup(partition);
    await globalThis.openKustoExplorerStorageClear(partition);
    const cleared = await globalThis.openKustoExplorerStorageRead(partition, "connections");
    await globalThis.openKustoExplorerStorageRestoreBackup(partition, backup);
    const restored = await globalThis.openKustoExplorerStorageRead(partition, "connections");

    globalThis.openKustoExplorerShowToast("Browser smoke", "Toast delivery is active");
    const toast = document.querySelector(".browser-toast");
    if (!toast) {
      throw new Error("The Browser toast was not added to the live region.");
    }

    const toastText = toast?.textContent ?? "";
    toast?.querySelector("button")?.click();
    const toastDismissed = !toast?.isConnected;
    await globalThis.openKustoExplorerStorageClear(partition);
    return { cleared, restored, toastDismissed, toastText };
  });

  assert.equal(storageResult.cleared, "");
  assert.equal(storageResult.restored, "{\"version\":1,\"clusters\":[]}");
  assert.match(storageResult.toastText, /Browser smoke/);
  assert.equal(storageResult.toastDismissed, true);
  assert.deepEqual(errors, []);
  await verifyProfileGuidance({ width: 390, height: 844 });
  if (!importOnly) {
    await verifyDashboardTimeRange({ width: 1440, height: 1000 });
    await verifyDashboardTimeRange({ width: 390, height: 844 });
  }
} finally {
  await browser.close();
  fs.rmSync(profileDirectory, { recursive: true, force: true });
}