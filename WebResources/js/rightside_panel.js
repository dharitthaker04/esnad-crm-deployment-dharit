function openCopilotPanel() {
    var pageInput = {
        pageType: "webresource",
        webresourceName: "new_genesys_panel", // no .html extension
        data: null
    };

    var navigationOptions = {
        target: 2, // Side panel
        position: 2, // Right side
        width: { value: 400, unit: "px" },
        title: "Copilot"
    };

    Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
        function success() {
            console.log("Panel opened");
        },
        function error(e) {
            console.error("Error:", e.message);
        }
    );
}
