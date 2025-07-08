function showCommentModalOnStatusChange(executionContext) {
    const formContext = executionContext.getFormContext();
    const statusAttr = formContext.getAttribute("statuscode");

    if (!statusAttr) {
        console.error("❌ 'statuscode' field not found.");
        return;
    }

    const selectedOption = statusAttr.getSelectedOption();
    if (!selectedOption) {
        console.warn("⚠️ No option selected in 'statuscode'.");
        return;
    }

    const selectedLabel = selectedOption.text;
    console.log("🎯 Ticket Status changed to:", selectedLabel);

    if (selectedLabel === "New") {
        console.log("ℹ️ Status is 'New'. No modal shown.");
        return;
    }

    openCommentModal(formContext);
}

function injectBootstrapCss() {
    const head = window.top.document.head;
    if (!head.querySelector("#bootstrap-css")) {
        const link = window.top.document.createElement("link");
        link.id = "bootstrap-css";
        link.rel = "stylesheet";
        link.href = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css";
        head.appendChild(link);
    }
}

function openCommentModal(formContext) {
    injectBootstrapCss();

    console.log("📝 Opening comment modal...");

    const parentDoc = window.top.document;

    const existing = parentDoc.getElementById("statusCommentModal");
    if (existing) existing.remove();

    const modalHtml = `
<div id="statusCommentModal" class="modal fade show" tabindex="-1" style="
    background-color: rgba(0,0,0,0.5);
    position: fixed;
    top: 0; left: 0;
    width: 100%; height: 100%;
    z-index: 1055;
    display: flex;
    justify-content: center;
    align-items: center;">
  <div class="modal-dialog modal-dialog-centered" style="max-width: 500px; width: 100%;">
    <div class="modal-content shadow-lg border-0 rounded-3">
      <div class="modal-header">
        <h5 class="modal-title">Status Change Comment</h5>
        <button type="button" class="btn-close" onclick="window.top.closeStatusCommentModal()"></button>
      </div>
      <div class="modal-body">
        <textarea id="statusCommentText" class="form-control w-100" style="min-height: 120px;" placeholder="Enter your comment..."></textarea>
      </div>
      <div class="modal-footer justify-content-end">
        <button class="btn btn-primary px-4" onclick="window.top.submitStatusComment()">Submit</button>
      </div>
    </div>
  </div>
</div>
`;

    const container = parentDoc.createElement("div");
    container.innerHTML = modalHtml;
    parentDoc.body.appendChild(container);

    window.top._statusCommentContext = formContext;
}

window.top.closeStatusCommentModal = function () {
    const existing = window.top.document.getElementById("statusCommentModal");
    if (existing) existing.remove();
    window.top._statusCommentContext = null;
};

window.top.submitStatusComment = function () {
    const comment = window.top.document.getElementById("statusCommentText")?.value.trim();
    if (!comment) {
        alert("⚠️ Please enter a comment.");
        return;
    }

    const formContext = window.top._statusCommentContext;
    if (!formContext) {
        alert("❌ Form context is missing.");
        return;
    }

    const caseId = formContext.data.entity.getId();
    if (!caseId) {
        alert("❌ Cannot find Case ID.");
        return;
    }

    const cleanId = caseId.replace(/[{}]/g, "");
    const note = {
        "subject": "Status Change Comment",
        "notetext": comment,
        "objectid_incident@odata.bind": `/incidents(${cleanId})`
    };

    Xrm.WebApi.createRecord("annotation", note).then(function (result) {
        console.log("📝 Note created:", result.id);
        alert("✅ Comment saved successfully.");
        window.top.closeStatusCommentModal();
    }, function (error) {
        console.error("❌ Failed to save note:", error.message);
        alert("Error saving comment: " + error.message);
    });
};
