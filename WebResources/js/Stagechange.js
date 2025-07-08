async function OnFieldChange(executionContext) {
    const formContext = executionContext.getFormContext();

    try {
        // Get the active path of the BPF
        const process = formContext.data.process;
        const activePath = process.getActivePath();

        // Check if the active path is valid and contains stages
        if (activePath) {
            const stages = activePath.getStages();
            if (stages.length > 0) {
                // Log all stages
                console.log("All Stages in the Active Path:");

                stages.forEach((stage, index) => {
                    const stageName = stage.getName();
                    const stageId = stage.getId();
                    const stageStatus = stage.getStatus();

                    // Log each stage's name, ID, and status
                    console.log(`Stage ${index + 1}:`);
                    console.log("Stage Name:", stageName);
                    console.log("Stage GUID:", stageId);
                    console.log("Stage Status:", stageStatus);
                });
            } else {
                console.log("No stages found in the active path.");
            }
        } else {
            console.log("No active path found.");
        }
    } catch (error) {
        console.error("❌ Error fetching stages:", error);
        alert("An error occurred while fetching the stages.");
    }
}
