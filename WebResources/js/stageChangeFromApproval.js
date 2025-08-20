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
                await formContext.data.save();
                closeBpfFlyout();
                return;
            }

            formContext.data.process.setActiveStage(targetStage.getId(), async function (result) {
                if (result === "success") {
                    console.log(`✅ UI stage changed to: ${targetStageName}`);
                    await updateStatusCodeByStageId(formContext, targetStage.getId());
                } else {
                    console.warn("⚠ Failed to change stage via UI. Using Web API fallback...");
                    await forceChangeViaWebAPI(formContext, targetStageName);
                }

                formContext.getAttribute("new_stagesofbpf").setValue(null);
                await formContext.data.save();
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

        console.log("✅ Stage changed via Web API.");
        await updateStatusCodeByStageId(formContext, stageId);

       
    } catch (err) {
        console.error("❌ Web API fallback failed:", err);
    }
}

// ✅ Utility: Update statuscode based on stage GUID
async function updateStatusCodeByStageId(formContext, stageId) {
    const stageIdToStatuscodeMap = {
        "65894155-4ed9-449b-ab1d-d4d4fb196e48": 100000001, // Return To Customer
        "1ee2e3b4-3e83-4fe3-9b5b-490b6e91e8af": 100000002, // Solution Verification
        "91153307-982f-479d-af7f-73048b80e52c": 100000008, // Processing
        "ef0a2c39-d6d9-4b29-a39b-53dc539f0982": 100000003  // Ticket Closure
    };

    const cleanedId = stageId.replace(/[{}]/g, "").toLowerCase();
    const newStatus = stageIdToStatuscodeMap[cleanedId];

    if (newStatus === undefined) {
        console.warn("⚠️ No statuscode mapping found for stage ID:", cleanedId);
        return;
    }

    const statusAttr = formContext.getAttribute("statuscode");
    if (statusAttr?.getValue() !== newStatus) {
        statusAttr.setValue(newStatus);
        console.log(`✅ statuscode set to ${newStatus} for stage ${cleanedId}`);
		formContext.getAttribute("new_stagesofbpf").setValue(null);
        await formContext.data.save();
        formContext.data.refresh(false);
        closeBpfFlyout();
    } else {
        console.log("ℹ️ statuscode already set correctly.");
		formContext.getAttribute("new_stagesofbpf").setValue(null);
        await formContext.data.save();
        formContext.data.refresh(false);
        closeBpfFlyout();
    }
}

// 🔽 Close BPF flyout
function closeBpfFlyout() {
    setTimeout(() => {
        try {
            const closeButton = parent.document.querySelector('button[title="Close"]');
            if (closeButton) {
                closeButton.click();
                console.log("✅ BPF flyout closed.");
            } else {
                console.warn("⚠️ Close button not found.");
            }
        } catch (e) {
            console.error("❌ Failed to close flyout:", e);
        }
    }, 1000);
}
