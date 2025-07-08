async function OnchangeProcessing(executionContext) {
    const formContext = executionContext.getFormContext();
    const selectedValue = formContext.getAttribute("new_stagesbpf")?.getValue();

    if (!selectedValue && selectedValue !== 0) {
        console.warn("⚠ No value selected in new_stagesbpf");
        return;
    }

    const stageNameMap = {
        1: "Solution Verification",
        2: "Return to Customer",
        3: "Processing- Department"
    };

    const targetStageName = stageNameMap[selectedValue];
    if (!targetStageName) {
        console.warn("❌ Invalid option set value:", selectedValue);
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
            await forceChangeViaWebAPI(formContext, targetStageName, "new_stagesbpf");
            return;
        }

        formContext.data.process.setActiveStage(targetStage.getId(), async function (result) {
            if (result === "success") {
                console.log(`✅ UI stage changed to: ${targetStageName}`);
                formContext.getAttribute("new_stagesbpf").setValue(null);
                await formContext.data.save(); // ✅ Auto-save after UI stage change
            } else {
                console.warn("⚠ Failed to change stage via UI. Using Web API fallback...");
                await forceChangeViaWebAPI(formContext, targetStageName, "new_stagesbpf");
            }
        });
    } catch (err) {
        console.error("❌ Unexpected error during stage change:", err);
    }
}


async function forceChangeViaWebAPI(formContext, targetStageName, optionSetLogicalName) {
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

        console.log("🧩 BPF Entity Name:", bpfEntityLogicalName);

        const bpfRecord = await Xrm.WebApi.retrieveRecord(bpfEntityLogicalName, instanceId, "?$expand=processid($select=workflowid)");
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

        formContext.getAttribute(optionSetLogicalName).setValue(null);
        await formContext.data.save(); // ✅ Auto-save after Web API update
        formContext.data.refresh(false); // Optional: can comment this out if not required

        console.log("✅ Stage changed via Web API. Saved and refreshed.");

    } catch (err) {
        console.error("❌ Web API fallback failed:", err);
    }
}
