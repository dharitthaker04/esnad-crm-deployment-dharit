function ensureBootstrapLoaded() {
    const parentHead = window.top.document.head;
    if (!parentHead.querySelector("#bootstrap-css")) {
        const link = window.top.document.createElement("link");
        link.id = "bootstrap-css";
        link.rel = "stylesheet";
        link.href = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css";
        parentHead.appendChild(link);
    }
}

function reactivateAndSetStage(executionContext) {
    console.log("✅ OnSave triggered");

    const formContext = executionContext.getFormContext?.() || executionContext; // ✅ Safe fallback

    const caseId = formContext.data.entity.getId().replace(/[{}]/g, "");
    const parentDoc = window.top.document;

    ensureBootstrapLoaded();

    const modalHtml = `
    <div id="ticketReopenModal" class="modal fade show" tabindex="-1" style="
      background-color: rgba(0,0,0,0.5);
      position: fixed;
      top: 0; left: 0;
      width: 100%; height: 100%;
      z-index: 1055;
      display: flex;
      justify-content: center;
      align-items: center;
    ">
      <div class="modal-dialog modal-dialog-centered modal-md" style="width: 100%;">
        <div class="modal-content p-3">
          <div class="modal-header">
            <h5 class="modal-title">Ticket Reopen</h5>
            <button type="button" class="btn-close" onclick="window.top.document.getElementById('ticketReopenModal').remove();"></button>
          </div>
          <div class="modal-body">
            <div class="mb-3">
              <label for="reopenComment" class="form-label">Comment</label>
              <textarea class="form-control" id="reopenComment" rows="4" placeholder="Enter comment"></textarea>
            </div>
          </div>
          <div class="modal-footer justify-content-end">
            <button class="btn btn-secondary me-2" onclick="window.top.document.getElementById('ticketReopenModal').remove();">Cancel</button>
            <button class="btn btn-primary" onclick="window.top.submitReopenModal()">Submit</button>
          </div>
        </div>
      </div>
    </div>
    `;

    const existing = parentDoc.getElementById("ticketReopenModal");
    if (existing) existing.remove();

    const container = parentDoc.createElement("div");
    container.innerHTML = modalHtml;
    parentDoc.body.appendChild(container);

    window.top.submitReopenModal = function () {
        const comment = window.top.document.getElementById("reopenComment").value;

        if (!comment || comment.trim() === "") {
            alert("Please enter a comment before submitting.");
            return;
        }

        const note = {
            "subject": "Ticket Reopen Comment",
            "notetext": comment,
            "objectid_incident@odata.bind": `/incidents(${caseId})`
        };

        Xrm.WebApi.createRecord("annotation", note).then(function (result) {
            console.log("✅ Note created:", result.id);
            alert("Comment submitted and saved to Notes.");
            window.top.document.getElementById("ticketReopenModal").remove();
            delete window.top.submitReopenModal;

            const process = formContext.data.process;

            if (process) {
                const currentStage = process.getActiveStage();
                if (currentStage && currentStage.getId().toLowerCase() === "ef0a2c39-d6d9-4b29-a39b-53dc539f0982") {
                    console.log("🔁 Moving from 'Ticket Closure' to next stage...");
                    process.moveNext(function (result) {
                        if (result === "success") {
                            console.log("✅ Stage moved successfully.");
                            Xrm.WebApi.updateRecord("incident", caseId, {
                                "statecode": 0,
                                "statuscode": 1
                            }).then(function () {
                                console.log("✅ Case status set to Active - In Progress.");
                                formContext.data.refresh();
                            }, function (error) {
                                console.error("❌ Failed to update case status:", error.message);
                            });
                        } else {
                            console.warn("⚠️ Stage move result:", result);
                        }
                    });
                } else {
                    console.warn("⚠️ Not at 'Ticket Closure' stage.");
                }
            } else {
                console.error("❌ BPF process not found.");
            }
        }, function (error) {
            console.error("❌ Failed to save note:", error.message);
            alert("Error saving comment: " + error.message);
        });
    };
}
