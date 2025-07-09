async function autoMoveToProcessingIfCustomerResponded(executionContext) {
    const formContext = executionContext.getFormContext();
    const processingStageName = "Processing";
    const bpfEntityName = "phonetocaseprocess";
    const caseId = formContext.data.entity.getId().replace(/[{}]/g, "");

    setTimeout(async () => {
        try {
            // Step 1: Check if either field is true
            const replied = formContext.getAttribute("new_customerreplyonsolutionvarification")?.getValue();
            const responded = formContext.getAttribute("new_customerresponded")?.getValue();

            console.log(`📌 Reply: ${replied}, Responded: ${responded}`);

            if (!replied && !responded) {
                console.log("ℹ️ No customer reply or response. No action needed.");
                return;
            }

            // Step 2: Find 'Processing' stage ID
            const path = formContext.data.process.getActivePath();
            let processingStageId = null;

            path.forEach(stage => {
                if (stage.getName().toLowerCase() === processingStageName.toLowerCase()) {
                    processingStageId = stage.getId();
                    console.log(`✅ Found 'Processing' stage ID: ${processingStageId}`);
                }
            });

            if (!processingStageId) {
                alert("❌ 'Processing' stage not found in path.");
                return;
            }

            // Step 3: Retrieve BPF record
            const result = await Xrm.WebApi.retrieveMultipleRecords(bpfEntityName, `?$filter=_incidentid_value eq ${caseId}`);
            if (!result.entities || result.entities.length === 0) {
                console.error("❌ No BPF record found.");
                return;
            }

            const bpfRecord = result.entities[0];
            const bpfId = bpfRecord["phonetocaseprocessid"] || bpfRecord["businessprocessflowinstanceid"] || bpfRecord["bpfid"];

            if (!bpfId) {
                alert("❌ Could not extract BPF ID.");
                return;
            }

            // Step 4: Forcefully set active stage to Processing
            await Xrm.WebApi.updateRecord(bpfEntityName, bpfId, {
                "activestageid@odata.bind": `/processstages(${processingStageId})`
            });

            console.log("✅ Processing stage activated.");

            // Step 5: Set both fields to No and save
            formContext.getAttribute("new_customerreplyonsolutionvarification").setValue(false);
            formContext.getAttribute("new_customerresponded").setValue(false);

            await formContext.data.save();
            console.log("💾 Form saved after setting fields to No.");

        } catch (err) {
            console.error("❌ Error during stage activation:", err);
            alert("❌ Failed to auto-move to Processing.");
        }
    }, 1000); // Ensure BPF is fully loaded
}

