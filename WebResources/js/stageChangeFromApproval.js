async function OnchangeApproval(executionContext) {
    const formContext = executionContext.getFormContext();

    // Save form first before processing stage change
    await formContext.data.save().then(async function () {
        const selectedValue = formContext.getAttribute("new_stagesofbpf")?.getValue();

        if (selectedValue === null || selectedValue === undefined) {
            console.warn("⚠ No value selected in new_stagesofbpf");
            return;
        }

        const stageNameMap = {
            1: "Solution Verification",
            0: "Return To Customer",
            100000000: "Processing",
            2: "Ticket Closure"
        };

        const targetStageName = stageNameMap[selectedValue];
        if (!targetStageName) {
            console.warn("❌ Invalid selection. No stage mapped.");
            return;
        }

        try {
            const activePath = formContext.data.process.getActivePath();
            let targetStage = null;

            activePath.forEach(stage => {
                if (stage.getName()?.trim().toLowerCase() === targetStageName.toLowerCase()) {
                    targetStage = stage;
                }
            });

            if (!targetStage) {
                console.warn(`⚠ UI stage not found: ${targetStageName}. Falling back to Web API.`);
                await forceChangeViaWebAPI(formContext, targetStageName);
                formContext.getAttribute("new_stagesofbpf").setValue(null);
                await formContext.data.save(); // ✅ Save again to prevent unsaved changes popup
                closeBpfFlyout();
                return;
            }

            formContext.data.process.setActiveStage(targetStage.getId(), async function (result) {
                if (result === "success") {
                    console.log(`✅ UI stage changed to: ${targetStageName}`);
                } else {
                    console.warn("⚠ Failed to change stage via UI. Using Web API fallback...");
                    await forceChangeViaWebAPI(formContext, targetStageName);
                }

                formContext.getAttribute("new_stagesofbpf").setValue(null);
                await formContext.data.save(); // ✅ Save again to prevent unsaved changes popup
                closeBpfFlyout();
            });
        } catch (err) {
            console.error("❌ Unexpected error during stage change:", err);
        }
    });
}

async function forceChangeViaWebAPI(formContext, targetStageName) {
    try {
        const instanceId = formContext.data.process.getInstanceId();
        const processId = formContext.data.process.getActiveProcess()?.getId();

        if (!instanceId || !processId) {
            console.error("❌ Could not retrieve BPF instance ID or process ID.");
            return;
        }

        const workflow = await Xrm.WebApi.retrieveRecord("workflow", processId, "?$select=uniquename");
        const bpfEntityLogicalName = workflow.uniquename?.toLowerCase();
        if (!bpfEntityLogicalName) {
            console.error("❌ Could not determine BPF entity name.");
            return;
        }

        console.log(`🧩 BPF Entity Name: ${bpfEntityLogicalName}`);

        const bpfRecord = await Xrm.WebApi.retrieveRecord(
            bpfEntityLogicalName,
            instanceId,
            "?$expand=processid($select=workflowid)"
        );

        const actualProcessId = bpfRecord.processid?.workflowid;

        if (!actualProcessId) {
            console.error("❌ Process ID not found in BPF record.");
            return;
        }

        const stageResults = await Xrm.WebApi.retrieveMultipleRecords(
            "processstage",
            `?$filter=processid/workflowid eq ${actualProcessId}`
        );

        const matchedStage = stageResults.entities.find(s =>
            s.stagename?.trim().toLowerCase() === targetStageName.trim().toLowerCase()
        );

        if (!matchedStage) {
            console.error(`❌ No stage matched for name: ${targetStageName}`);
            return;
        }

        const stageId = matchedStage["processstageid"];
        console.log(`➡ Setting active stage to: ${targetStageName} (${stageId})`);

        await Xrm.WebApi.updateRecord(bpfEntityLogicalName, instanceId, {
            "activestageid@odata.bind": `/processstages(${stageId})`
        });

        console.log("✅ Stage changed via Web API. Saving and refreshing form...");
        formContext.getAttribute("new_stagesofbpf").setValue(null); // ✅ Reset after change
        await formContext.data.save(); // ✅ Save again to avoid "unsaved changes" popup
        formContext.data.refresh(false);
    } catch (err) {
        console.error("❌ Web API fallback failed:", err);
    }
}

// 🔽 Utility function to close BPF stage flyout
function closeBpfFlyout() {
    setTimeout(() => {
        try {
            const closeButton = parent.document.querySelector('button[title="Close"]');
            if (closeButton) {
                closeButton.click();
                console.log("✅ BPF flyout closed successfully.");
            } else {
                console.warn("⚠️ Close button for BPF flyout not found.");
            }
        } catch (e) {
            console.error("❌ Failed to close BPF flyout:", e);
        }
    }, 1000); // Delay to allow stage change to complete
}
