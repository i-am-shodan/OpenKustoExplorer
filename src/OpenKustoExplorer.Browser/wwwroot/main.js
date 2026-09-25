import "./browser-storage.js";
import "./kusto-explorer-import.js";
import "./storage-manager.js";

const performancePrefix = "oke.";
const queryParameters = new URLSearchParams(globalThis.location.search);
const profilingEnabled = queryParameters.has("profile");
const performanceFixtureEnabled = profilingEnabled
    && queryParameters.get("fixture") === "performance"
    && ["localhost", "127.0.0.1", "[::1]"].includes(globalThis.location.hostname);
const maximumObservedEntryCount = 200;
const observedAnimationFrames = [];
const observedEvents = [];
const observedLongTasks = [];
const observedOperations = [];
const activeOperations = new Map();
let authenticatedStoragePartition = "";

const observePerformanceEntries = (type, destination, options = {}) => {
    try {
        const observer = new PerformanceObserver(list => {
            for (const entry of list.getEntries()) {
                if (destination.length >= maximumObservedEntryCount) {
                    break;
                }

                destination.push({
                    blockingDuration: entry.blockingDuration ?? 0,
                    name: entry.name,
                    startTime: entry.startTime,
                    duration: entry.duration,
                    interactionId: entry.interactionId ?? 0
                });
            }
        });
        observer.observe({ type, buffered: true, ...options });
    } catch {
        // Entry types vary by browser and must never affect application startup.
    }
};

if (profilingEnabled) {
    observePerformanceEntries("long-animation-frame", observedAnimationFrames);
    observePerformanceEntries("longtask", observedLongTasks);
    observePerformanceEntries("event", observedEvents, { durationThreshold: 16 });
}

globalThis.openKustoExplorerMarkPerformance = name => {
    globalThis.performance.mark(`${performancePrefix}${name}`);
};

globalThis.openKustoExplorerIsProfilingEnabled = () => profilingEnabled;
globalThis.openKustoExplorerIsPerformanceFixtureEnabled = () => performanceFixtureEnabled;

globalThis.openKustoExplorerStartPerformanceOperation = (operationId, operationName, itemCount) => {
    if (!profilingEnabled || activeOperations.has(operationId)) {
        return;
    }

    const startMark = `${performancePrefix}operation.${operationId}.start`;
    globalThis.performance.mark(startMark);
    activeOperations.set(operationId, {
        id: operationId,
        itemCount,
        name: operationName,
        startMark
    });
};

const completePerformanceOperation = (operationId, outcome) => {
    const operation = activeOperations.get(operationId);
    if (!operation) {
        return;
    }

    activeOperations.delete(operationId);
    const endMark = `${performancePrefix}operation.${operationId}.end`;
    const measureName = `${performancePrefix}operation.${operationId}`;
    globalThis.performance.mark(endMark);
    globalThis.performance.measure(measureName, operation.startMark, endMark);
    const measure = globalThis.performance.getEntriesByName(measureName, "measure").at(-1);
    if (measure && observedOperations.length < maximumObservedEntryCount) {
        observedOperations.push({
            id: operation.id,
            name: operation.name,
            startTime: measure.startTime,
            duration: measure.duration,
            itemCount: operation.itemCount,
            outcome
        });
    }

    globalThis.performance.clearMarks(operation.startMark);
    globalThis.performance.clearMarks(endMark);
    globalThis.performance.clearMeasures(measureName);
};

globalThis.openKustoExplorerCompletePerformanceOperation = completePerformanceOperation;

globalThis.openKustoExplorerCompletePerformanceOperationAfterRender = async (operationId, outcome) => {
    await new Promise(resolve => globalThis.requestAnimationFrame(resolve));
    await new Promise(resolve => globalThis.requestAnimationFrame(resolve));
    completePerformanceOperation(operationId, outcome);
};

globalThis.openKustoExplorerSetStoragePartition = partition => {
    authenticatedStoragePartition = partition;
};

globalThis.openKustoExplorerShowToast = (title, message) => {
    const region = document.querySelector("#toast-region");
    if (!region) {
        return;
    }

    while (region.childElementCount >= 3) {
        region.firstElementChild?.remove();
    }

    const toast = document.createElement("section");
    toast.className = "browser-toast";

    const header = document.createElement("div");
    header.className = "browser-toast-header";

    const heading = document.createElement("strong");
    heading.textContent = title;
    header.append(heading);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "browser-toast-close";
    close.setAttribute("aria-label", "Dismiss notification");
    close.textContent = "\u00d7";
    header.append(close);

    const body = document.createElement("p");
    body.textContent = message;
    toast.append(header, body);

    let removalTimer = globalThis.setTimeout(() => toast.remove(), 8000);
    close.addEventListener("click", () => {
        globalThis.clearTimeout(removalTimer);
        toast.remove();
    });
    toast.addEventListener("pointerenter", () => globalThis.clearTimeout(removalTimer));
    toast.addEventListener("pointerleave", () => {
        removalTimer = globalThis.setTimeout(() => toast.remove(), 3000);
    });
    region.append(toast);
};

globalThis.openKustoExplorerGetPerformanceProfile = () => JSON.stringify({
    animationFrames: observedAnimationFrames,
    events: observedEvents,
    longTasks: observedLongTasks,
    operations: observedOperations,
    marks: globalThis.performance
        .getEntriesByType("mark")
        .filter(entry => entry.name.startsWith(performancePrefix))
        .map(entry => ({ name: entry.name.substring(performancePrefix.length), startTime: entry.startTime })),
    measures: globalThis.performance
        .getEntriesByType("measure")
        .filter(entry => entry.name.startsWith(performancePrefix))
        .map(entry => ({
            name: entry.name.substring(performancePrefix.length),
            startTime: entry.startTime,
            duration: entry.duration
        }))
});

globalThis.openKustoExplorerResetPerformanceProfile = () => {
    observedAnimationFrames.length = 0;
    observedEvents.length = 0;
    observedLongTasks.length = 0;
    observedOperations.length = 0;
    const activeStartMarks = new Set(
        Array.from(activeOperations.values(), operation => operation.startMark));
    for (const entry of globalThis.performance.getEntriesByType("mark")) {
        if (entry.name.startsWith(performancePrefix) && !activeStartMarks.has(entry.name)) {
            globalThis.performance.clearMarks(entry.name);
        }
    }

    for (const entry of globalThis.performance.getEntriesByType("measure")) {
        if (entry.name.startsWith(performancePrefix)) {
            globalThis.performance.clearMeasures(entry.name);
        }
    }
};

globalThis.openKustoExplorerMarkPerformance("script.start");

globalThis.openKustoExplorerSubmitSignOut = (action, antiforgeryFieldName, antiforgeryToken) => {
    const form = document.createElement("form");
    form.method = "post";
    form.action = action;

    const tokenInput = document.createElement("input");
    tokenInput.type = "hidden";
    tokenInput.name = antiforgeryFieldName;
    tokenInput.value = antiforgeryToken;
    form.append(tokenInput);

    document.body.append(form);
    form.submit();
};

globalThis.openKustoExplorerCompleteStartup = () => {
    globalThis.openKustoExplorerMarkPerformance("startup.complete");
    globalThis.performance.measure(
        `${performancePrefix}startup.total`,
        `${performancePrefix}script.start`,
        `${performancePrefix}startup.complete`);
    const startup = document.querySelector(".startup");
    if (!startup) {
        return;
    }

    globalThis.requestAnimationFrame(() => {
        globalThis.requestAnimationFrame(() => {
            startup.classList.add("startup-leaving");
            startup.addEventListener("transitionend", () => startup.remove(), { once: true });
            globalThis.setTimeout(() => startup.remove(), 300);
            if (profilingEnabled) {
                console.info("Open Kusto Explorer performance", globalThis.openKustoExplorerGetPerformanceProfile());
            }
        });
    });
};

const startupMessage = document.querySelector("#startup-message");
const startupRetry = document.querySelector("#startup-retry");
const startupStorage = document.querySelector("#startup-storage");

const updateStartupMessage = (message) => {
    if (startupMessage) {
        startupMessage.textContent = message;
    }
};

startupRetry?.addEventListener("click", () => globalThis.location.reload());
startupStorage?.addEventListener("click", () => {
    if (authenticatedStoragePartition) {
        globalThis.openKustoExplorerOpenStorageManager(authenticatedStoragePartition);
    }
});

try {
    updateStartupMessage("Loading application");
    globalThis.openKustoExplorerMarkPerformance("runtime.import.start");
    const { dotnet } = await import("./_framework/dotnet.js");
    globalThis.openKustoExplorerMarkPerformance("runtime.import.complete");
    globalThis.openKustoExplorerMarkPerformance("runtime.create.start");
    const dotnetRuntime = await dotnet
        .withDiagnosticTracing(false)
        .withApplicationArgumentsFromQuery()
        .create();
    globalThis.openKustoExplorerMarkPerformance("runtime.create.complete");

    updateStartupMessage("Starting workspace");
    const config = dotnetRuntime.getConfig();

    globalThis.openKustoExplorerMarkPerformance("managed.main.invoke");
    await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
    globalThis.openKustoExplorerMarkPerformance("managed.main.returned");
    if (performanceFixtureEnabled) {
        const managedExports = await dotnetRuntime.getAssemblyExports(config.mainAssemblyName);
        const fixtureExports = managedExports.OpenKustoExplorer?.Browser?.BrowserPerformanceExports;
        if (!fixtureExports) {
            throw new Error("The Browser performance fixture export is unavailable.");
        }

        globalThis.openKustoExplorerInvokePerformanceFixture = action => fixtureExports.Invoke(action);
        globalThis.openKustoExplorerIsReleaseBuild = () => fixtureExports.IsReleaseBuild();
    }
} catch (error) {
    console.error("Open Kusto Explorer failed to start.", error);
    document.body.classList.add("startup-failed");
    updateStartupMessage("The workspace could not start");
    if (startupRetry) {
        startupRetry.hidden = false;
    }

    if (startupStorage && authenticatedStoragePartition) {
        startupStorage.hidden = false;
    }
}