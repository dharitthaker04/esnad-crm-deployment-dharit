function setFromToAdminUser(executionContext) {
    var formContext = executionContext.getFormContext();
    console.log("📥 Email form loaded. Attempting to set 'From' (lookup type).");

    var fromAttr = formContext.getAttribute("from");
    if (!fromAttr) {
        console.error("❌ 'From' field not found.");
        return;
    }

    var domainName = "CRM-ESNAD\\crmadmin"; // Replace with correct domain\username
    var query = `?$select=fullname,systemuserid&$filter=domainname eq '${domainName}' and isdisabled eq false`;

    Xrm.WebApi.retrieveMultipleRecords("systemuser", query).then(
        function (result) {
            if (!result || result.entities.length === 0) {
                console.warn("⚠️ No active user found with domain name: " + domainName);
                return;
            }

            var user = result.entities[0];
            console.log(`✅ Found user: ${user.fullname} (${user.systemuserid})`);

            // If 'from' is a lookup (not a partylist), use this format
            var value = [{
                id: user.systemuserid,
                name: user.fullname,
                entityType: "systemuser"
            }];

            try {
                fromAttr.setValue(value);
                fromAttr.setSubmitMode("always");
                console.log("📤 'From' field set successfully as lookup.");
            } catch (err) {
                console.error("❌ Error setting 'From' field:", err);
            }
        },
        function (error) {
            console.error("❌ Web API error:", error.message);
        }
    );
}
