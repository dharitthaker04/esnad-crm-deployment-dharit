function appendLicensesToCase(executionContext) {
    const formContext = executionContext.getFormContext();
    const customer = formContext.getAttribute("customerid").getValue();

    if (!customer) {
        formContext.getAttribute("new_globalsearch").setValue(null);
        return;
    }

    const customerId = customer[0].id.replace("{", "").replace("}", "");
    const customerType = customer[0].entityType;

    if (customerType === "account") {
        fetchAndAppendLicenses(customerId, formContext);
    } else if (customerType === "contact") {
        // First, get the associated account from contact
        Xrm.WebApi.retrieveRecord("contact", customerId, "?$select=new_companyname").then(
            function (contact) {
                if (contact.new_companyname) {
                    const accountId = contact.new_companyname.accountid;
                    fetchAndAppendLicenses(accountId, formContext);
                } else {
                    formContext.getAttribute("new_globalsearch").setValue("Contact not linked to any company");
                }
            },
            function (error) {
                console.error("Error retrieving contact:", error.message);
            }
        );
    } else {
        formContext.getAttribute("new_globalsearch").setValue("Unsupported customer type");
    }
}

function fetchAndAppendLicenses(accountId, formContext) {
    const fetchXml = `
<fetch>
<entity name="new_licensetype">
<attribute name="new_licensenumber" />
<filter>
<condition attribute="new_companyname" operator="eq" value="${accountId}" />
</filter>
</entity>
</fetch>`;

    Xrm.WebApi.retrieveMultipleRecords("new_licensetype", "?fetchXml=" + encodeURIComponent(fetchXml)).then(
        function (result) {
            const licenseNumbers = [];

            result.entities.forEach(function (record) {
                if (record.new_licensenumber) {
                    licenseNumbers.push(record.new_licensenumber);
                }
            });

            const licenseCsv = licenseNumbers.join(", ");
            formContext.getAttribute("new_globalsearch").setValue(licenseCsv);
        },
        function (error) {
            console.error("Error fetching licenses:", error.message);
        }
    );
}