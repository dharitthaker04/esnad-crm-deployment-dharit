// Use global variable for in-session tracking
window.previousStageMemory = "";

function onSave(executionContext) {
    try {
        const formContext = executionContext.getFormContext();
        const process = formContext.data.process;

        if (!process || !process.getActiveStage()) {
            console.warn("Process or active stage not available.");
            return;
        }

        const currentStageName = process.getActiveStage().getName();
        const previousStage = window.previousStageMemory || "";

        console.log("✅ Current Stage:", currentStageName);
        console.log("✅ Previous Stage:", previousStage);

        const procFromDeptAttr = formContext.getAttribute("new_procfromdept");
        const prevStageAttr = formContext.getAttribute("new_previousstage");

        if (!procFromDeptAttr || !prevStageAttr) {
            console.warn("Missing required fields.");
            return;
        }

        // Use safe lowercase comparison
        const shouldBeYes =
            previousStage?.toLowerCase().includes("processing- department") &&
            currentStageName?.toLowerCase() === "processing";

        const newValue = shouldBeYes ? "Yes" : "No";
        const oldValue = procFromDeptAttr.getValue();

        procFromDeptAttr.setValue(newValue);
        procFromDeptAttr.setSubmitMode("always");

        const control = formContext.getControl("new_procfromdept");
        if (control) {
            control.setFocus();
        }

        prevStageAttr.setValue(previousStage);
        prevStageAttr.setSubmitMode("always");

        window.previousStageMemory = currentStageName;

        console.log(`📝 Updated 'Back to Proc from Dept' to ${newValue}`);

        // ✅ Assign to team only when value CHANGES to "Yes"
        if (newValue === "Yes" && oldValue !== "Yes") {
            assignToTeam(formContext, "Customer Service Department");
        }

    } catch (e) {
        console.error("🔥 Save Error:", e);
    }
}

function assignToTeam(formContext, teamName) {
    Xrm.WebApi.retrieveMultipleRecords("team", `?$filter=name eq '${teamName}'`).then(
        function success(result) {
            if (result.entities.length > 0) {
                const team = result.entities[0];
                console.log("👥 Team found:", team.name);

                formContext.getAttribute("ownerid").setValue([{
                    id: team.teamid,
                    name: team.name,
                    entityType: "team"
                }]);
                formContext.getAttribute("ownerid").setSubmitMode("always");

                console.log(`✅ Owner assigned to team: ${team.name}`);
            } else {
                console.warn(`❌ Team '${teamName}' not found.`);
            }
        },
        function (error) {
            console.error("❌ Failed to retrieve team:", error.message);
        }
    );
}
