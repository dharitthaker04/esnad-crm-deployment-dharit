function autoMoveToApprovalStage(executionContext) {
console.log("autoMoveToApprovalStage");
    var formContext = executionContext.getFormContext();
    var saveMode = executionContext.getEventArgs().getSaveMode();
 
    // Only proceed for AutoSave (70) or UserSave (1)
    if (saveMode !== 1 && saveMode !== 70) {
        console.log("ℹ️ Save triggered by unsupported mode. Exiting.");
        return;
    }
 
    // Wait for form save and BPF readiness
    setTimeout(function () {
        if (!formContext.data || !formContext.data.process) {
            console.error("❌ BPF process not available.");
            return;
        }
 
        var process = formContext.data.process;
        var currentStage = process.getActiveStage();
 
        if (!currentStage) {
            console.warn("⚠️ No active stage. BPF might not be initialized yet.");
            return;
        }
 
        var currentStageId = currentStage.getId();

        var ticketCreationStageId = "15322a8f-67b8-47fb-8763-13a28686c29d";

 
        console.log("🔍 Current Stage:", currentStage.getName(), "| ID:", currentStageId);
 
        if (currentStageId.toLowerCase() === ticketCreationStageId.toLowerCase()) {
            console.log("⏩ Moving from 'Ticket Creation' to next stage...");
 
            process.moveNext(function (result) {
                if (result === "success") {
                    console.log("✅ Stage moved successfully. Saving again...");
                    formContext.data.save().then(
                        () => console.log("💾 Form saved after BPF stage transition."),
                        (e) => console.error("❌ Save after BPF move failed:", e.message)
                    );
                } else {
                    console.error("❌ BPF moveNext failed. Possibly due to validation errors or missing required fields.");
                }
            });
        } else {
            console.log("ℹ️ Not at 'Ticket Creation' stage. No move needed.");
        }
    }, 5000); // Increased timeout for better BPF readiness
}
 
 
function autoMoveToApprovalStageonLoad(executionContext) {
    var formContext = executionContext.getFormContext();
 
    // Check if the form is opened for an existing record (not a new record)
    var recordId = formContext.data.entity.getId();
 
    // If the record ID is null or empty, the form is for a new record; return early
    if (!recordId) {
        console.log("ℹ️ Form opened for creating a new record. Skipping stage update.");
        return;
    }
 
    // Wait for BPF readiness
    setTimeout(function () {
        if (!formContext.data || !formContext.data.process) {
            console.error("❌ BPF process not available.");
            return;
        }
 
        var process = formContext.data.process;
        var currentStage = process.getActiveStage();
 
        if (!currentStage) {
            console.warn("⚠️ No active stage. BPF might not be initialized yet.");
            return;
        }
 
        var currentStageId = currentStage.getId();

        var ticketCreationStageId = "15322a8f-67b8-47fb-8763-13a28686c29d";

 
        console.log("🔍 Current Stage:", currentStage.getName(), "| ID:", currentStageId);
 
        // Only move if currently in 'Ticket Creation' stage
        if (currentStageId.toLowerCase() === ticketCreationStageId.toLowerCase()) {
            console.log("⏩ Moving from 'Ticket Creation' to next stage...");
 
            process.moveNext(function (result) {
                if (result === "success") {
                    console.log("✅ Stage moved successfully.");
                } else {
                    console.error("❌ BPF moveNext failed. Possibly due to validation errors or missing required fields.");
                }
            });
        } else {
            console.log("ℹ️ Not at 'Ticket Creation' stage. No move needed.");
        }
    }, 5000); // Increased timeout for better BPF readiness
}
 