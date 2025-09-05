function validateIntegerField(executionContext) {
    var formContext = executionContext.getFormContext();
    var fieldName = "new_crnumber"; // replace with your field logical name
    var fieldValue = formContext.getAttribute(fieldName).getValue();

    console.log("validateIntegerField called for field:", fieldName);
    console.log("Current field value:", fieldValue);

    if (fieldValue !== null && fieldValue !== undefined && fieldValue !== "") {
        // Ensure it's treated as string for validation
        var stringValue = String(fieldValue).trim();

        // Regex: exactly 10 digits (0–9)
        var regex = /^\d{10}$/;

        if (!regex.test(stringValue)) {
            console.log("❌ Invalid value. Must be exactly 10 digits.");

            // Show notification
            formContext.getControl(fieldName).setNotification("CR Number must be exactly 10 digits.");

            // Prevent save if OnSave event
            if (executionContext.getEventArgs) {
                var eventArgs = executionContext.getEventArgs();
                if (eventArgs && eventArgs.preventDefault) {
                    eventArgs.preventDefault(); // blocks save
                    console.log("Save prevented due to invalid CR Number.");
                }
            }
            return false;
        } else {
            console.log("✅ Valid CR Number:", stringValue);
            // Clear notification if valid
            formContext.getControl(fieldName).clearNotification();
        }
    } else {
        console.log("Field is empty or null.");
        formContext.getControl(fieldName).clearNotification();
    }
}
