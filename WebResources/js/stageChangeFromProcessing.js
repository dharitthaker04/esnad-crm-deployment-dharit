async function updateBPFStageBasedOnOptionSet(executionContext) {
    try {
        const formContext = executionContext.getFormContext();
        if (!formContext || !formContext.data) {
            console.error("❌ Form context not available.");
            return;
        }

        const caseId = formContext.data.entity.getId().replace(/[{}]/g, "");
        console.log("✅ Case ID:", caseId);

        const optionSetAttr = formContext.getAttribute("new_stagesofbpf");
        if (!optionSetAttr) {
            console.error("❌ Field 'new_stagesofbpf' not found.");
            return;
        }

        const selectedValue = optionSetAttr.getValue();
        console.log("📦 Selected option value:", selectedValue);

        const stageMap = {
            0: "Return to Customer",
            1: "Solution Verification",
            2: "Ticket Closure"
        };

        const targetStageName = stageMap[selectedValue];
        if (!targetStageName) {
            console.warn("⚠️ No mapped stage name for selected value:", selectedValue);
            return;
        }

        const bpfEntityName = "phonetocaseprocess"; // Replace if different

        console.log(🔎 Retrieving BPF record for case...);

        Xrm.WebApi.retrieveMultipleRecords(bpfEntityName, ?$filter = _incidentid_value eq ${ caseId }).then(function (bpfResult) {
            if (!bpfResult.entities || bpfResult.entities.length === 0) {
                console.error("❌ No BPF record found for this case.");
                return;
            }

            const bpfRecord = bpfResult.entities[0];
            const bpfStatus = bpfRecord["statecode"];
            let bpfId = null;

            for (let key in bpfRecord) {
                if (key.endsWith("id") && key !== "_incidentid_value") {
                    bpfId = bpfRecord[key];
                    console.log("✅ Detected BPF ID:", bpfId);
                    break;
                }
            }

            if (!bpfId) {
                console.error("❌ Could not extract BPF ID.");
                return;
            }

            const proceedToStageUpdate = () => {
                console.log(🔎 Fetching stage ID for '${targetStageName}'...);

        Xrm.WebApi.retrieveMultipleRecords("processstage", ?$filter = stagename eq '${targetStageName}').then(function (stageResult) {
            if (!stageResult.entities || stageResult.entities.length === 0) {
                console.error(❌ Stage '${targetStageName}' not found.);
                return;
            }

            const stageId = stageResult.entities[0]["processstageid"];
            console.log("✅ Stage ID:", stageId);

            const updatePayload = {
                "activestageid@odata.bind": /processstages(${stageId})
            };

            console.log(🔄 Updating BPF to '${targetStageName}' stage...);

            Xrm.WebApi.updateRecord(bpfEntityName, bpfId, updatePayload).then(async function () {
                console.log(✅ BPF moved to '${targetStageName}' stage.);

                // ✅ Reset the option set field
                formContext.getAttribute("new_stagesofbpf").setValue(null);

                // ✅ Save again to prevent "Unsaved Changes" popup
                await formContext.data.save();

                // Proceed with stage hiding logic
                hideStages(formContext, selectedValue);

            }, function (error) {
                console.error("❌ Failed to update BPF stage:", error.message);
            });
        }, function (error) {
            console.error("❌ Failed to fetch stage:", error.message);
        });
    };

    if (bpfStatus === 1) {
        console.log("♻️ BPF is inactive. Reactivating...");

        const reactivationPayload = {
            "statecode": 0,
            "statuscode": 1
        };

        Xrm.WebApi.updateRecord(bpfEntityName, bpfId, reactivationPayload).then(function () {
            console.log("✅ BPF reactivated.");
            proceedToStageUpdate();
        }, function (error) {
            console.error("❌ Failed to reactivate BPF:", error.message);
        });
    } else {
        proceedToStageUpdate();
    }

}, function (error) {
    console.error("❌ Failed to retrieve BPF record:", error.message);
});

    } catch (e) {
    console.error("❌ Exception occurred:", e.message);
}
}

// 🔒 Function to hide stages based on selection
function hideStages(formContext, selectedValue) {
    try {
        const stageToHideMap = {
            0: ["Processing"],
            1: ["Processing", "Return to Customer"],
            2: ["Processing", "Return to Customer", "Solution Verification"]
        };