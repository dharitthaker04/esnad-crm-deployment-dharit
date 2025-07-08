function moveToSolutionVerificationStage(executionContext) {
    var formContext = executionContext.getFormContext();

    // Only proceed after successful manual or auto-save
    var saveMode = executionContext.getEventArgs().getSaveMode();
    if (saveMode !== 1 && saveMode !== 70) {
        console.log("ℹ️ Not a user or auto save. Skipping.");
        return;
    }

    // Defer BPF logic until save completes
    setTimeout(function () {
        if (!formContext.data || !formContext.data.process) {
            console.error("❌ BPF process context not available.");
            return;
        }

        var process = formContext.data.process;
        var currentStage = process.getActiveStage();

        if (!currentStage) {
            console.warn("⚠️ Active stage not detected. Skipping BPF movement.");
            return;
        }

        var currentStageId = currentStage.getId();
        var ticketCreationStageId = "15322a8f-67b8-47fb-8763-13a28686c29d";

        console.log("🔍 Current Stage ID:", currentStageId);

        if (currentStageId.toLowerCase() === ticketCreationStageId.toLowerCase()) {
            console.log("🔁 Moving to next stage from Ticket Creation...");

            process.moveNext(function (result) {
                if (result === "success") {
                    console.log("✅ Moved to 'Approval And Forwarding'. Saving form again...");
                    
                    // Save again to persist BPF stage change
                    formContext.data.save().then(
                        function success() {
                            console.log("💾 Form saved after stage change.");
                        },
                        function error(e) {
                            console.error("❌ Error while saving after stage change:", e.message);
                        }
                    );
                } else {
                    console.error("❌ moveNext failed. Required fields may be missing.");
                }
            });
        } else {
            console.log("ℹ️ Already past 'Ticket Creation'. No movement needed.");
        }
    }, 2000);
}
