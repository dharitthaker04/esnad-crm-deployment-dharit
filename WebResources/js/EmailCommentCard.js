function toggleEmailCommentCardVisibility(executionContext) {
    var formContext = executionContext.getFormContext();
    var stage = formContext.data.process.getActiveStage();

    if (stage) {
        var currentStageName = stage.getName();
        console.log("Current Stage:", currentStageName);

        // Show/hide Email Comment section
        var section = formContext.ui.tabs.get("general").sections.get("general_section_5");
        if (section) {
            section.setVisible(currentStageName === "Return To Customer");
        } else {
            console.error("❌ Section 'general_section_5' not found on tab 'general'.");
        }

        // Update custom hidden field to trigger workflow
        if (currentStageName === "Return To Customer") {
            var stageField = formContext.getAttribute("new_bpfstage");
            if (stageField && stageField.getValue() !== "Return To Customer") {
                stageField.setValue("Return To Customer");
                stageField.setSubmitMode("always"); // ensure it's saved
                formContext.data.entity.save(); // triggers workflow
                console.log("✅ Set new_bpfstage to 'Return To Customer' and saved form.");
            }
        }
    }
}
