const maximumFileCount = 2000;
const maximumFileBytes = 16 * 1024 * 1024;
const maximumTotalBytes = 64 * 1024 * 1024;
const profilePath = String.raw`%LOCALAPPDATA%\Kusto.Explorer`;

const isSupportedFile = file => {
    const name = file.name.toLowerCase();
    return name === "userconnections.xml"
        || name === "userconnectiongroups.xml"
        || name.endsWith(".kebak");
};

    const isProfileFile = file => {
        const name = file.name.toLowerCase();
        return name === "userconnections.xml"
        || name === "userconnectiongroups.xml";
    };

    const isRecoveryFile = file => file.name.toLowerCase().endsWith(".kebak");

const readTextFile = async file => {
    const bytes = new Uint8Array(await file.arrayBuffer());
    if (bytes.length >= 2 && bytes[0] === 0xff && bytes[1] === 0xfe) {
        return new TextDecoder("utf-16le").decode(bytes.subarray(2));
    }

    if (bytes.length >= 2 && bytes[0] === 0xfe && bytes[1] === 0xff) {
        return new TextDecoder("utf-16be").decode(bytes.subarray(2));
    }

    const start = bytes.length >= 3
        && bytes[0] === 0xef
        && bytes[1] === 0xbb
        && bytes[2] === 0xbf
            ? 3
            : 0;
    return new TextDecoder("utf-8").decode(bytes.subarray(start));
};

const serializeSelectedFiles = async entries => {
    const supportedEntries = entries
        .filter(entry => isSupportedFile(entry.file))
        .sort((left, right) => left.path.localeCompare(right.path));
    if (supportedEntries.length === 0) {
        throw new Error("Select at least one supported Kusto Explorer profile file.");
    }

    if (supportedEntries.length > maximumFileCount) {
        throw new Error(`The selected profile contains more than ${maximumFileCount} supported files.`);
    }

    const selected = [];
    let totalBytes = 0;
    for (const entry of supportedEntries) {
        if (entry.file.size > maximumFileBytes) {
            throw new Error(`${entry.file.name} exceeds the 16 MB profile-file limit.`);
        }

        totalBytes += entry.file.size;
        if (totalBytes > maximumTotalBytes) {
            throw new Error("The selected profile exceeds the 64 MB import limit.");
        }

        selected.push({
            path: entry.path,
            content: await readTextFile(entry.file)
        });
    }

    return JSON.stringify(selected);
};

const selectFiles = (accept, isAllowed, createPath, description) => new Promise((resolve, reject) => {
    const input = document.createElement("input");
    input.type = "file";
    input.multiple = true;
    input.accept = accept;
    input.setAttribute("aria-hidden", "true");
    input.style.display = "none";
    document.body.append(input);
    let completed = false;

    const finish = value => {
        if (completed) {
            return;
        }

        completed = true;
        globalThis.removeEventListener("focus", onWindowFocus);
        input.remove();
        resolve(value);
    };

    const fail = value => {
        if (completed) {
            return;
        }

        completed = true;
        globalThis.removeEventListener("focus", onWindowFocus);
        input.remove();
        reject(value);
    };

    const onWindowFocus = () => {
        globalThis.setTimeout(() => {
            if (!completed && input.files?.length === 0) {
                finish([]);
            }
        }, 250);
    };

    input.addEventListener("cancel", () => finish([]), { once: true });
    input.addEventListener("change", () => {
        try {
            const selectedFiles = [...(input.files ?? [])];
            const allowedFiles = selectedFiles.filter(isAllowed);
            if (selectedFiles.length > 0 && allowedFiles.length === 0) {
                throw new Error(`Select ${description}.`);
            }

            finish(allowedFiles.map(file => ({
                file,
                path: createPath(file)
            })));
        } catch (error) {
            fail(error);
        }
    }, { once: true });
    globalThis.addEventListener("focus", onWindowFocus, { once: true });
    input.click();
});

const dialog = document.querySelector("#kusto-profile-import");
const copyPathButton = document.querySelector("#kusto-profile-copy-path");
const chooseProfileFilesButton = document.querySelector("#kusto-profile-choose-files");
const chooseRecoveryFilesButton = document.querySelector("#kusto-profile-choose-recovery");
const importButton = document.querySelector("#kusto-profile-import-selected");
const cancelButton = document.querySelector("#kusto-profile-cancel");
const status = document.querySelector("#kusto-profile-status");
const errorMessage = document.querySelector("#kusto-profile-error");
let activeSelection = null;

copyPathButton?.addEventListener("click", async () => {
    try {
        await navigator.clipboard.writeText(profilePath);
        status.textContent = "Profile path copied";
    } catch {
        status.textContent = "Select the path above and copy it manually";
    }
});

globalThis.openKustoExplorerSelectProfile = () => {
    if (activeSelection) {
        return activeSelection;
    }

    activeSelection = new Promise(resolve => {
        let completed = false;
        const selectedEntries = new Map();
        const setBusy = busy => {
            chooseProfileFilesButton.disabled = busy;
            chooseRecoveryFilesButton.disabled = busy;
            importButton.disabled = busy || selectedEntries.size === 0;
            cancelButton.disabled = busy;
        };
        const updateStatus = () => {
            const entries = [...selectedEntries.values()];
            const profileCount = entries.filter(entry => isProfileFile(entry.file)).length;
            const recoveryCount = entries.filter(entry => isRecoveryFile(entry.file)).length;
            status.textContent = selectedEntries.size === 0
                ? "Choose profile files to continue"
                : `${profileCount} profile file${profileCount === 1 ? "" : "s"} and ${recoveryCount} recovery file${recoveryCount === 1 ? "" : "s"} selected`;
            importButton.disabled = selectedEntries.size === 0;
        };
        const showError = error => {
            errorMessage.textContent = error instanceof Error
                ? error.message
                : "The selected Kusto Explorer files could not be read.";
            errorMessage.hidden = false;
        };
        const finish = value => {
            if (completed) {
                return;
            }

            completed = true;
            dialog.removeEventListener("cancel", onCancel);
            cancelButton.removeEventListener("click", onCancelButton);
            chooseProfileFilesButton.removeEventListener("click", onChooseProfileFiles);
            chooseRecoveryFilesButton.removeEventListener("click", onChooseRecoveryFiles);
            importButton.removeEventListener("click", onImport);
            if (dialog.open) {
                dialog.close();
            }

            setBusy(false);
            activeSelection = null;
            resolve(value);
        };
        const onCancel = event => {
            event.preventDefault();
            finish("");
        };
        const onCancelButton = () => finish("");
        const addFiles = async select => {
            errorMessage.hidden = true;
            status.textContent = "Opening file picker";
            setBusy(true);
            try {
                const entries = await select();
                for (const entry of entries) {
                    selectedEntries.set(entry.path.toLowerCase(), entry);
                }

                updateStatus();
            } catch (error) {
                showError(error);
                updateStatus();
            } finally {
                setBusy(false);
            }
        };
        const onChooseProfileFiles = () => addFiles(() => selectFiles(
            ".xml,application/xml,text/xml",
            isProfileFile,
            file => file.name,
            "UserConnections.xml or UserConnectionGroups.xml"));
        const onChooseRecoveryFiles = () => addFiles(() => selectFiles(
            ".kebak,application/json",
            isRecoveryFile,
            file => `Recovery/${file.name}`,
            ".kebak recovery files"));
        const onImport = async () => {
            setBusy(true);
            try {
                finish(await serializeSelectedFiles([...selectedEntries.values()]));
            } catch (error) {
                showError(error);
                setBusy(false);
            }
        };

        errorMessage.hidden = true;
        selectedEntries.clear();
        updateStatus();
        dialog.addEventListener("cancel", onCancel);
        cancelButton.addEventListener("click", onCancelButton);
        chooseProfileFilesButton.addEventListener("click", onChooseProfileFiles);
        chooseRecoveryFilesButton.addEventListener("click", onChooseRecoveryFiles);
        importButton.addEventListener("click", onImport);
        dialog.showModal();
        copyPathButton.focus();
    });

    return activeSelection;
};