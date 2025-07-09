function shouldEnableButtonForCSRRole() {
    console.log("🔄 shouldEnableButtonForCSRRole triggered in Ribbon.");

    var flag = false;
    var targetRoleId = "9ffb23f5-546e-4fc6-8d5e-583c2ebd21ce".toLowerCase(); // CSR role GUID
    var roleIds = Xrm.Utility.getGlobalContext().userSettings.securityRoles;

    if (roleIds && roleIds.length > 0) {
        roleIds.forEach(function (roleId) {
            if (roleId.toLowerCase() === targetRoleId) {
                flag = true;
            }
        });
    }

    console.log("CSR Role matched:", flag);
    return !flag; // Button should be enabled only if NOT in CSR role
}
