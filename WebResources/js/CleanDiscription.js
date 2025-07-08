function cleanDescription(executionContext) {
    console.log("🔄 cleanDescription triggered on Case form load.");

    var formContext = executionContext.getFormContext();
    var descAttr = formContext.getAttribute("description");

    if (descAttr) {
        var htmlDesc = descAttr.getValue();

        if (htmlDesc && typeof htmlDesc === "string") {
            // 1. Remove all HTML tags
            var plainText = htmlDesc.replace(/<[^>]+>/g, '');

            // 2. Decode HTML entities (e.g., &nbsp; to space)
            var textArea = document.createElement("textarea");
            textArea.innerHTML = plainText;
            plainText = textArea.value;

            // 3. Replace multiple spaces and trim
            plainText = plainText.replace(/\s+/g, ' ').trim();

            // 4. Set cleaned text back to the field
            descAttr.setValue(plainText);
        }
    }
}
