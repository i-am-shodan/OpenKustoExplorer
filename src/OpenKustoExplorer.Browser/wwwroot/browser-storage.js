const databaseName = "OpenKustoExplorer";
const databaseVersion = 2;
const catalogStoreName = "catalogs";
const partitionIndexName = "partition";
const backupFormat = "OpenKustoExplorer.BrowserBackup";
const backupVersion = 1;
const managedCatalogNames = new Set([
    "automations",
    "connections",
    "dashboards",
    "documents",
    "graphs",
    "recorded-sessions"
]);
const pendingWrites = new Map();
let databasePromise;
let lastWriteError = "";
let writesSuspended = false;

const waitForRequest = request => new Promise((resolve, reject) => {
    request.addEventListener("success", () => resolve(request.result), { once: true });
    request.addEventListener("error", () => reject(request.error), { once: true });
});

const waitForTransaction = transaction => new Promise((resolve, reject) => {
    transaction.addEventListener("complete", resolve, { once: true });
    transaction.addEventListener("abort", () => reject(transaction.error), { once: true });
    transaction.addEventListener("error", () => reject(transaction.error), { once: true });
});

const openDatabase = () => {
    if (!databasePromise) {
        databasePromise = new Promise((resolve, reject) => {
            const request = indexedDB.open(databaseName, databaseVersion);
            request.addEventListener("upgradeneeded", () => {
                let store;
                if (!request.result.objectStoreNames.contains(catalogStoreName)) {
                    store = request.result.createObjectStore(catalogStoreName, { keyPath: "id" });
                } else {
                    store = request.transaction.objectStore(catalogStoreName);
                }

                if (!store.indexNames.contains(partitionIndexName)) {
                    store.createIndex(partitionIndexName, partitionIndexName, { unique: false });
                }
            }, { once: true });
            request.addEventListener("success", () => {
                request.result.addEventListener("versionchange", () => request.result.close());
                resolve(request.result);
            }, { once: true });
            request.addEventListener("error", () => reject(request.error), { once: true });
            request.addEventListener("blocked", () => reject(new Error("Browser storage migration is blocked by another tab.")), { once: true });
        });
    }

    return databasePromise;
};

const createId = (partition, name) => `${partition}\u001f${name}`;
const createSettingsKey = partition => `OpenKustoExplorer:settings:${partition}`;
const isManagedCatalogName = name => managedCatalogNames.has(name) || name.startsWith("recovery:");

const getSettings = partition => {
    try {
        return localStorage.getItem(createSettingsKey(partition)) ?? "";
    } catch (error) {
        lastWriteError = `Failed to read settings: ${error?.message ?? String(error)}`;
        return "";
    }
};

const flushPendingWrites = async () => {
    while (pendingWrites.size > 0) {
        const entries = [...pendingWrites.entries()];
        const results = await Promise.allSettled(entries.map(([, write]) => write));
        entries.forEach(([id, write]) => clearPendingWrite(id, write));
        const failures = results
            .filter(result => result.status === "rejected")
            .map(result => result.reason);
        if (failures.length > 0) {
            throw new AggregateError(failures, `${failures.length} browser storage writes failed.`);
        }
    }
};

const readPartitionRecords = async partition => {
    const database = await openDatabase();
    const transaction = database.transaction(catalogStoreName, "readonly");
    const completion = waitForTransaction(transaction);
    const records = await waitForRequest(
        transaction.objectStore(catalogStoreName).index(partitionIndexName).getAll(partition));
    await completion;
    return records.filter(record => record.partition === partition);
};

const validateBackup = backupText => {
    const backup = JSON.parse(backupText);
    if (backup?.format !== backupFormat || backup.version !== backupVersion) {
        throw new Error("This is not a supported Open Kusto Explorer browser backup.");
    }

    if (!Array.isArray(backup.records) || backup.records.length > 10000) {
        throw new Error("The browser backup record collection is invalid.");
    }

    const names = new Set();
    for (const record of backup.records) {
        if (!record
            || typeof record.name !== "string"
            || typeof record.json !== "string"
            || !isManagedCatalogName(record.name)
            || names.has(record.name)) {
            throw new Error("The browser backup contains an invalid or duplicate record.");
        }

        if (managedCatalogNames.has(record.name) && record.json.trim()) {
            JSON.parse(record.json);
        }

        names.add(record.name);
    }

    if (typeof backup.settings !== "string") {
        throw new Error("The browser backup settings payload is invalid.");
    }

    return backup;
};

globalThis.openKustoExplorerStorageOpen = async () => {
    await openDatabase();
    if (navigator.storage?.persist) {
        await navigator.storage.persist();
    }
};

globalThis.openKustoExplorerStorageRead = async (partition, name) => {
    const database = await openDatabase();
    const transaction = database.transaction(catalogStoreName, "readonly");
    const record = await waitForRequest(
        transaction.objectStore(catalogStoreName).get(createId(partition, name)));
    await waitForTransaction(transaction);
    return record?.json ?? "";
};

globalThis.openKustoExplorerStoragePreserveInvalid = async (partition, name, json) => {
    await flushPendingWrites();
    const database = await openDatabase();
    const recoveryName = `recovery:${name}:${new Date().toISOString()}:${crypto.randomUUID()}`;
    const transaction = database.transaction(catalogStoreName, "readwrite");
    const store = transaction.objectStore(catalogStoreName);
    store.put({
        id: createId(partition, recoveryName),
        partition,
        name: recoveryName,
        json,
        updatedAtUtc: new Date().toISOString()
    });
    store.delete(createId(partition, name));
    await waitForTransaction(transaction);
};

globalThis.openKustoExplorerStoragePreserveInvalidSetting = (partition, settingsJson) => {
    const recoveryName = `recovery:settings:${new Date().toISOString()}:${crypto.randomUUID()}`;
    const id = createId(partition, recoveryName);
    const write = enqueueWrite(partition, recoveryName, settingsJson);
    void write.then(() => {
        try {
            const settingsKey = createSettingsKey(partition);
            if (localStorage.getItem(settingsKey) === settingsJson) {
                localStorage.removeItem(settingsKey);
            }
        } catch (error) {
            lastWriteError = `Failed to reset invalid settings: ${error?.message ?? String(error)}`;
            globalThis.openKustoExplorerShowToast?.("Storage recovery failed", lastWriteError);
        }

        clearPendingWrite(id, write);
    }, error => {
        lastWriteError = `Failed to archive invalid settings: ${error?.message ?? String(error)}`;
        console.error(lastWriteError, error);
        globalThis.openKustoExplorerShowToast?.("Storage recovery failed", lastWriteError);
        clearPendingWrite(id, write);
    });
};

globalThis.openKustoExplorerStorageGetStatus = async partition => {
    await flushPendingWrites();
    const records = await readPartitionRecords(partition);
    const settings = getSettings(partition);
    const encoder = new TextEncoder();
    const appBytes = records.reduce(
        (total, record) => total + encoder.encode(record.json ?? "").byteLength,
        encoder.encode(settings).byteLength);
    const estimate = navigator.storage?.estimate
        ? await navigator.storage.estimate()
        : {};
    const persisted = navigator.storage?.persisted
        ? await navigator.storage.persisted()
        : null;
    return JSON.stringify({
        appBytes,
        lastWriteError,
        originQuotaBytes: estimate.quota ?? null,
        originUsageBytes: estimate.usage ?? null,
        pendingWriteCount: pendingWrites.size,
        persisted,
        recordCount: records.length,
        recoveryCount: records.filter(record => record.name.startsWith("recovery:")).length
    });
};

globalThis.openKustoExplorerStorageCreateBackup = async partition => {
    await flushPendingWrites();
    const records = await readPartitionRecords(partition);
    return JSON.stringify({
        format: backupFormat,
        version: backupVersion,
        exportedAtUtc: new Date().toISOString(),
        records: records
            .filter(record => isManagedCatalogName(record.name))
            .map(record => ({ name: record.name, json: record.json ?? "" })),
        settings: getSettings(partition)
    }, null, 2);
};

globalThis.openKustoExplorerStorageRestoreBackup = async (partition, backupText) => {
    const backup = validateBackup(backupText);
    writesSuspended = true;
    try {
        await flushPendingWrites();
        const currentRecords = await readPartitionRecords(partition);
        const settingsKey = createSettingsKey(partition);
        const previousSettings = localStorage.getItem(settingsKey) ?? "";
        if (backup.settings) {
            localStorage.setItem(settingsKey, backup.settings);
        } else {
            localStorage.removeItem(settingsKey);
        }

        try {
            const database = await openDatabase();
            const transaction = database.transaction(catalogStoreName, "readwrite");
            const store = transaction.objectStore(catalogStoreName);
            for (const record of currentRecords) {
                store.delete(record.id);
            }

            const updatedAtUtc = new Date().toISOString();
            for (const record of backup.records) {
                store.put({
                    id: createId(partition, record.name),
                    partition,
                    name: record.name,
                    json: record.json,
                    updatedAtUtc
                });
            }

            await waitForTransaction(transaction);
        } catch (error) {
            if (previousSettings) {
                localStorage.setItem(settingsKey, previousSettings);
            } else {
                localStorage.removeItem(settingsKey);
            }

            throw error;
        }
    } catch (error) {
        writesSuspended = false;
        throw error;
    }
};

globalThis.openKustoExplorerStorageClear = async partition => {
    writesSuspended = true;
    try {
        await flushPendingWrites();
        const records = await readPartitionRecords(partition);
        const settingsKey = createSettingsKey(partition);
        const previousSettings = localStorage.getItem(settingsKey) ?? "";
        localStorage.removeItem(settingsKey);
        try {
            const database = await openDatabase();
            const transaction = database.transaction(catalogStoreName, "readwrite");
            const store = transaction.objectStore(catalogStoreName);
            for (const record of records) {
                store.delete(record.id);
            }

            await waitForTransaction(transaction);
        } catch (error) {
            if (previousSettings) {
                localStorage.setItem(settingsKey, previousSettings);
            }

            throw error;
        }
    } catch (error) {
        writesSuspended = false;
        throw error;
    }
};

globalThis.openKustoExplorerStorageRequestPersistence = async () => {
    return navigator.storage?.persist ? navigator.storage.persist() : false;
};

const enqueueWrite = (partition, name, json) => {
    if (writesSuspended) {
        return Promise.reject(new Error("Storage writes are paused while browser data is being replaced."));
    }

    const id = createId(partition, name);
    const previousWrite = pendingWrites.get(id)?.catch(() => {}) ?? Promise.resolve();
    const write = previousWrite.then(async () => {
        const database = await openDatabase();
        const transaction = database.transaction(catalogStoreName, "readwrite");
        transaction.objectStore(catalogStoreName).put({
            id,
            partition,
            name,
            json,
            updatedAtUtc: new Date().toISOString()
        });
        await waitForTransaction(transaction);
    });
    pendingWrites.set(id, write);
    return write;
};

const clearPendingWrite = (id, write) => {
    if (pendingWrites.get(id) === write) {
        pendingWrites.delete(id);
    }
};

globalThis.openKustoExplorerStorageWrite = async (partition, name, json) => {
    const id = createId(partition, name);
    const write = enqueueWrite(partition, name, json);
    try {
        await write;
    } catch (error) {
        lastWriteError = `Failed to persist ${name}: ${error?.message ?? String(error)}`;
        globalThis.openKustoExplorerShowToast?.("Storage write failed", lastWriteError);
        throw error;
    } finally {
        clearPendingWrite(id, write);
    }
};

globalThis.openKustoExplorerStorageReadSetting = partition => {
    return getSettings(partition);
};

globalThis.openKustoExplorerStorageWriteSetting = (partition, settingsJson) => {
    try {
        localStorage.setItem(createSettingsKey(partition), settingsJson);
    } catch (error) {
        lastWriteError = `Failed to persist settings: ${error?.message ?? String(error)}`;
        console.error(lastWriteError, error);
        globalThis.openKustoExplorerShowToast?.("Storage write failed", lastWriteError);
    }
};