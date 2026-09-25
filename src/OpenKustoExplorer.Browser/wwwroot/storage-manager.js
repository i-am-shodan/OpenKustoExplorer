const dialog = document.querySelector("#storage-manager");
const status = document.querySelector("#storage-manager-status");
const error = document.querySelector("#storage-manager-error");
const appUsage = document.querySelector("#storage-app-usage");
const originUsage = document.querySelector("#storage-origin-usage");
const persistence = document.querySelector("#storage-persistence");
const recoveries = document.querySelector("#storage-recoveries");
const backupButton = document.querySelector("#storage-backup");
const restoreButton = document.querySelector("#storage-restore");
const restoreInput = document.querySelector("#storage-restore-input");
const persistButton = document.querySelector("#storage-persist");
const clearButton = document.querySelector("#storage-clear");
const actionButtons = [backupButton, restoreButton, persistButton, clearButton].filter(Boolean);
let currentPartition = "";

const formatBytes = value => {
    if (!Number.isFinite(value) || value < 0) {
        return "Unavailable";
    }

    if (value < 1024) {
        return `${value} B`;
    }

    const units = ["KB", "MB", "GB", "TB"];
    let amount = value;
    let unit = -1;
    do {
        amount /= 1024;
        unit++;
    } while (amount >= 1024 && unit < units.length - 1);
    return `${amount.toFixed(amount >= 10 ? 1 : 2)} ${units[unit]}`;
};

const setBusy = busy => {
    for (const button of actionButtons) {
        button.disabled = busy;
    }
};

const showError = value => {
    if (!error) {
        return;
    }

    error.textContent = value;
    error.hidden = !value;
};

const runAction = async action => {
    showError("");
    setBusy(true);
    try {
        await action();
    } catch (actionError) {
        showError(actionError?.message ?? String(actionError));
    } finally {
        setBusy(false);
    }
};

const refreshStatus = async () => {
    const result = JSON.parse(await globalThis.openKustoExplorerStorageGetStatus(currentPartition));
    if (appUsage) {
        appUsage.textContent = `${formatBytes(result.appBytes)} in ${result.recordCount} records`;
    }

    if (originUsage) {
        originUsage.textContent = `${formatBytes(result.originUsageBytes)} of ${formatBytes(result.originQuotaBytes)}`;
    }

    if (persistence) {
        persistence.textContent = result.persisted === true ? "Persistent" : "Best effort";
    }

    if (recoveries) {
        recoveries.textContent = String(result.recoveryCount);
    }

    if (status) {
        status.textContent = result.pendingWriteCount > 0
            ? `${result.pendingWriteCount} writes pending`
            : "All changes saved";
    }

    if (result.lastWriteError) {
        showError(result.lastWriteError);
    }
};

const downloadBackup = async () => {
    const backup = await globalThis.openKustoExplorerStorageCreateBackup(currentPartition);
    const blob = new Blob([backup], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    const date = new Date().toISOString().slice(0, 10);
    link.href = url;
    link.download = `open-kusto-explorer-backup-${date}.json`;
    link.click();
    URL.revokeObjectURL(url);
    globalThis.openKustoExplorerShowToast?.("Backup downloaded", "Browser application data was exported.");
};

globalThis.openKustoExplorerOpenStorageManager = partition => {
    currentPartition = partition;
    showError("");
    dialog?.showModal();
    void runAction(refreshStatus);
};

backupButton?.addEventListener("click", () => runAction(downloadBackup));

restoreButton?.addEventListener("click", () => restoreInput?.click());
restoreInput?.addEventListener("change", () => runAction(async () => {
    const file = restoreInput.files?.[0];
    restoreInput.value = "";
    if (!file || !globalThis.confirm("Replace this account's browser data with the selected backup?")) {
        return;
    }

    await globalThis.openKustoExplorerStorageRestoreBackup(currentPartition, await file.text());
    globalThis.location.reload();
}));

persistButton?.addEventListener("click", () => runAction(async () => {
    const persisted = await globalThis.openKustoExplorerStorageRequestPersistence();
    globalThis.openKustoExplorerShowToast?.(
        persisted ? "Persistent storage enabled" : "Persistent storage unavailable",
        persisted
            ? "The browser granted persistent storage for this site."
            : "The browser may reclaim this site's data when storage is constrained.");
    await refreshStatus();
}));

clearButton?.addEventListener("click", () => runAction(async () => {
    if (!globalThis.confirm("Clear all Open Kusto Explorer data for this account in this browser?")) {
        return;
    }

    await globalThis.openKustoExplorerStorageClear(currentPartition);
    globalThis.location.reload();
}));

dialog?.addEventListener("click", event => {
    if (event.target === dialog) {
        dialog.close();
    }
});