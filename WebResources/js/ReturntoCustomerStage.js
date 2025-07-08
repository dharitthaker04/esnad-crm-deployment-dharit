function moveToReturnToCustomerStage(executionContext) {
    try {
        const formContext = executionContext.getFormContext();
        if (!formContext || !formContext.data) {
            console.error("❌ Form context not available.");
            return;
        }

        const caseId = formContext.data.entity.getId().replace(/[{}]/g, "");
        console.log("✅ Case ID:", caseId);

        const flagAttr = formContext.getAttribute("new_isreturn");
        if (!flagAttr) {
            console.error("❌ Field 'new_isreturn' not found.");
            return;
        }

        const flagValue = flagAttr.getValue();
        const ALLOW_VALUE = 4;

        if (flagValue !== ALLOW_VALUE) {
            console.log("🚫 Flag is not 'Allow'. Exiting without changes.");
            return;
        }

        const bpfEntityName = "phonetocaseprocess";
        const targetStageName = "Return to Customer";

        console.log("🔎 Step 1: Retrieving BPF record for Case...");

        Xrm.WebApi.retrieveMultipleRecords(bpfEntityName, `?$filter=_incidentid_value eq ${caseId}`).then(function (bpfResult) {
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
                console.log(`🔎 Step 2: Fetching stage ID for '${targetStageName}'...`);

                Xrm.WebApi.retrieveMultipleRecords("processstage", `?$filter=stagename eq '${targetStageName}'`).then(function (stageResult) {
                    if (!stageResult.entities || stageResult.entities.length === 0) {
                        console.error(`❌ Stage '${targetStageName}' not found.`);
                        return;
                    }

                    const stageId = stageResult.entities[0]["processstageid"];
                    console.log("✅ Stage ID:", stageId);

                    const updatePayload = {
                        "activestageid@odata.bind": `/processstages(${stageId})`
                    };

                    console.log("🔄 Step 3: Updating BPF to target stage...");

                    Xrm.WebApi.updateRecord(bpfEntityName, bpfId, updatePayload).then(function () {
                        console.log("✅ BPF moved to 'Return to Customer' stage.");
                        formContext.data.entity.save();
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

