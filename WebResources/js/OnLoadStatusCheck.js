// ---------- Stage GUID → StatusCode Mapping ----------
const stageToStatusMap = {
    "15322a8f-67b8-47fb-8763-13a28686c29d": 100000000,
    "92a6721b-d465-4d36-aef7-e8822d7a5a6a": 100000006,
    "3b5a344f-9f9d-466b-aa08-611e60964b46": 1,
    "65894155-4ed9-449b-ab1d-d4d4fb196e48": 100000001,
    "1ee2e3b4-3e83-4fe3-9b5b-490b6e91e8af": 100000002,
    "91153307-982f-479d-af7f-73048b80e52c": 100000008,
    "ef0a2c39-d6d9-4b29-a39b-53dc539f0982": 100000003
};

// ---------- OnLoad Function ----------
function syncBPFStatus_OnLoad(executionContext) {
    const formCtx = executionContext.getFormContext();

    // Validate process API
    if (!formCtx.data.process || !formCtx.data.process.getActiveStage) {
        console.warn("BPF process API not available on this form.");
        return;
    }

    // Get active stage
    const stage = formCtx.data.process.getActiveStage();
    const stageId = stage?.getId()?.replace(/[{}]/g, "").toLowerCase();

    if (!stageId) {
        console.warn("No active BPF stage found.");
        return;
    }

    // Check if the stage is the specific stage where we want to avoid changing the status
    if (stageId === "ef0a2c39-d6d9-4b29-a39b-53dc539f0982") {
        const statusAttr = formCtx.getAttribute("statuscode");
        const currentStatus = statusAttr?.getValue();

        // If statuscode is 5, do not change it
        if (currentStatus === 5) {
            console.log("Statuscode is 5, no update needed.");
            return;
        }
    }

    // Map stage to status
    const newStatus = stageToStatusMap[stageId];
    if (!newStatus) {
        console.warn("Stage ID not in mapping. No status update.");
        return;
    }

    const statusAttr = formCtx.getAttribute("statuscode");
    const currentStatus = statusAttr?.getValue();

    if (currentStatus === newStatus) {
        console.log("Statuscode already matches the stage.");
        return;
    }

    // Update statuscode on form
    statusAttr.setValue(newStatus);
    statusAttr.setSubmitMode("always"); // ensure value is saved

    // Save the record (classic client safe)
    try {
        formCtx.data.entity.save();
        console.log(`Statuscode updated to ${newStatus} and record saved.`);
    } catch (err) {
        console.error("Error saving record after status update:", err);
    }
}
