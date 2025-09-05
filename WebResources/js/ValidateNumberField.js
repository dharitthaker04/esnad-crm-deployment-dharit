function validateNumberField(executionContext) {
    var formContext = executionContext.getFormContext();
    var fieldName = "mobilephone"; // replace with your field logical name
    var fieldValue = formContext.getAttribute(fieldName).getValue();

    console.log("validateIntegerField called for field:", fieldName);
    console.log("Current field value:", fieldValue);

    if (fieldValue !== null && fieldValue !== undefined && fieldValue !== "") {
        // Ensure it's a string for validation
        var stringValue = String(fieldValue).trim();

        // Regex: must start with 0, followed by 9 digits (total 10 digits)
        var regex = /^0\d{9}$/;

        if (!regex.test(stringValue)) {
            console.log("❌ Invalid value. Must be exactly 10 digits and start with 0.");

            // Show notification
            formContext.getControl(fieldName).setNotification("Mobile number must be exactly 10 digits and start with 0.");

            // Prevent save if OnSave event
            if (executionContext.getEventArgs) {
                var eventArgs = executionContext.getEventArgs();
                if (eventArgs && eventArgs.preventDefault) {
                    eventArgs.preventDefault();
                    console.log("Save prevented due to invalid mobile number.");
                }
            }
            return false;
        } else {
            console.log("✅ Valid mobile number:", stringValue);
            // Clear notification if valid
            formContext.getControl(fieldName).clearNotification();
        }
    } else {
        console.log("Field is empty or null.");
        formContext.getControl(fieldName).clearNotification();
    }
}
