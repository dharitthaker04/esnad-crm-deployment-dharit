function setCopyCaseGuidOnSave(executionContext) {
    var formContext = executionContext.getFormContext();
    console.log("🔄 OnSave event triggered");

    var formType = formContext.ui.getFormType();
    console.log("📝 Form Type: " + formType);

    // Only apply when form is in Update mode
    if (formType !== 2) {
        console.log("🚫 Not in Update mode. Exiting.");
        return;
    }

    var copyCaseGuidAttr = formContext.getAttribute('new_copycaseguid');

    if (!copyCaseGuidAttr) {
        console.error("❌ 'new_copycaseguid' attribute not found on form.");
        return;
    }

    var incidentId = formContext.data.entity.getId();
    console.log("🆔 Current Incident ID: " + incidentId);

    if (incidentId) {
        var cleanedId = incidentId.replace(/[{}]/g, "");
        copyCaseGuidAttr.setValue(cleanedId);
        console.log("✅ new_copycaseguid set to: " + cleanedId);
    } else {
        console.warn("⚠️ Incident ID not found.");
    }
}
