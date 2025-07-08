function reactivateAndSetStage(primaryControl) {
    console.log("✅ Modal trigger started");

    const caseId = primaryControl.data.entity.getId().replace(/[{}]/g, "");
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

    // Store modal submit logic into window.top for global visibility
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

        Xrm.WebApi.createRecord("annotation", note).then(function success(result) {
            console.log("✅ Note created:", result.id);
            alert("Comment submitted and saved to Notes.");
            window.top.document.getElementById("ticketReopenModal").remove();
            delete window.top.submitReopenModal;

            // 🔁 After note saved → Update BPF stage
            updateBPFStageAfterReopen(caseId, primaryControl);

        }, function (error) {
            console.error("❌ Failed to create note:", error.message);
            alert("Failed to save note: " + error.message);
        });
    };
}

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

function updateBPFStageAfterReopen(caseId, formContext) {
    try {
        if (!formContext || !formContext.data) {
            console.error("❌ Form context is not available.");
            return;
        }

        const bpfEntityName = "phonetocaseprocess";
        const targetStageName = "Approval and Forwarding"; // <-- Set your correct stage name

        console.log("🔎 Step 1: Fetching BPF record linked to case...");

	Xrm.WebApi.retrieveMultipleRecords(bpfEntityName, `?$filter=_incidentid_value eq ${caseId}`).then(function (bpfResult) {
            if (!bpfResult.entities || bpfResult.entities.length === 0) {
                console.error("❌ No BPF instance found for this case.");
                return;
            }

            const bpfRecord = bpfResult.entities[0];
            const bpfStatus = bpfRecord["statecode"];
            let bpfId = null;

            for (let key in bpfRecord) {
                if (key.endsWith("id") && key !== "_incidentid_value") {
                    bpfId = bpfRecord[key];
                    console.log("✅ BPF ID:", key, "=", bpfId);
                    break;
                }
            }

            if (!bpfId) {
                console.error("❌ Could not detect BPF ID from record.");
                return;
            }

            const proceedToStageUpdate = () => {
                console.log(`🔎 Step 2: Fetching stage ID for '${targetStageName}'...`);

                Xrm.WebApi.retrieveMultipleRecords("processstage", `?$filter=stagename eq '${targetStageName}'`).then(function (stageResult) {
                    if (!stageResult.entities || stageResult.entities.length === 0) {
                        console.error(`❌ Stage '${targetStageName}' not found.`);
                        return;
                    }

                    const targetStageId = stageResult.entities[0]["processstageid"];
                    console.log("✅ Target Stage ID:", targetStageId);

                    const updatePayload = {
                        "activestageid@odata.bind": `/processstages(${targetStageId})`
                    };

                    console.log(`🔄 Step 3: Updating BPF stage to '${targetStageName}'...`);

                    Xrm.WebApi.updateRecord(bpfEntityName, bpfId, updatePayload).then(function () {
                        console.log(`✅ Successfully moved to '${targetStageName}' stage.`);

                        // ✅ Step 4: Set Case Status to Active (statecode = 0) and In Progress (statuscode = 1)
                        Xrm.WebApi.updateRecord("incident", caseId, {
                            "statecode": 0,
                            "statuscode": 1
                        }).then(function () {
                            console.log("✅ Case status set to Active - In Progress.");
                            formContext.data.refresh();
                        }, function (error) {
                            console.error("❌ Failed to update case status:", error.message);
                        });

                    }, function (error) {
                        console.error("❌ Failed to update BPF stage:", error.message);
                    });
                });
            };

            if (bpfStatus === 1) {
                console.log("♻️ BPF is inactive. Reactivating first...");
                const reactivationPayload = {
                    "statecode": 0,
                    "statuscode": 1
                };

                Xrm.WebApi.updateRecord(bpfEntityName, bpfId, reactivationPayload).then(function () {
                    console.log("✅ BPF reactivated.");
                    proceedToStageUpdate();
                }, function (error) {
                    console.error("❌ Failed to reactivate BPF:", error.message);
                });
            } else {
                proceedToStageUpdate();
            }

        }, function (error) {
            console.error("❌ Failed to retrieve BPF record:", error.message);
        });

    } catch (e) {
        console.error("❌ Exception:", e.message);
    }
}
