/*******************************
 * Dynamics CRM BPF Handler
 * Works in EN + AR
 * Entry point = syncBPFStatus_OnLoad
 *******************************/

/** ============ CONFIG ============ **/

// Show the comment modal when the ACTIVE stage matches this GUID (no braces)
const MODAL_TRIGGER_STAGE_ID = "3b5a344f-9f9d-466b-aa08-611e60964b46";

// After submitting the comment, ACTIVATE this stage (no braces)
const ACTIVATE_STAGE_ID = "91153307-982f-479d-af7f-73048b80e52c";

// Optional fallback names (used only if you leave the IDs empty)
var Processing = [
  "Processing- Department","معالجة التذكرة من الإدارة المختصة"
];
var ACTIVATE_STAGE_NAMES = [
  "Processing","التحقق من الحل"
];

// Map of stage GUID (no braces, lowercase) -> statuscode value
var stageToStatusMap = {
  "15322a8f-67b8-47fb-8763-13a28686c29d": 100000000,
  "92a6721b-d465-4d36-aef7-e8822d7a5a6a": 100000006,
  "3b5a344f-9f9d-466b-aa08-611e60964b46": 1,          // Processing
  "65894155-4ed9-449b-ab1d-d4d4fb196e48": 100000001,
  "1ee2e3b4-3e83-4fe3-9b5b-490b6e91e8af": 100000002,
  "91153307-982f-479d-af7f-73048b80e52c": 100000008,  // Next stage after comment
  "ef0a2c39-d6d9-4b29-a39b-53dc539f0982": 100000003
};

// Team fallback list for assignment
var ASSIGN_TEAM_GUIDS = [
  "fca3c311-074c-f011-a400-fbb6a348b744", // Production
  "2c80efda-7c4b-f011-a3ff-af212fee8ea9"  // Development
];

/** ============ UTIL ============ **/

function norm(s){ return (s || "").toString().trim().toLowerCase(); }

function stageNameMatchesAgainstList(stageObj, names) {
  try {
    if (!names || !names.length) return false;
    var name = stageObj && stageObj.getName ? stageObj.getName() : "";
    var n = norm(name);
    for (var i = 0; i < names.length; i++) {
      if (n === norm(names[i])) return true;
    }
  } catch(e) {}
  return false;
}

function findNextStageButton(doc) {
  var selectors = [
    '[data-id="process-actionbar-next"]',
    '[data-id="header_process-actionbar-next"]',
    'button[aria-label*="Next"]',
    'button[title*="Next"]',
    'button[aria-label*="التالي"]', // Arabic
    'button[title*="التالي"]'
  ];
  for (var i=0;i<selectors.length;i++){
    var el = doc.querySelector(selectors[i]);
    if (el) return el;
  }
  return null;
}

function waitForBpfReady(formCtx, cb){
  var retries = 0, maxRetries = 20;
  var iv = setInterval(function(){
    var p = formCtx.data && formCtx.data.process;
    if (p && p.getActiveStage && p.getActiveStage()) {
      clearInterval(iv); cb();
    } else if (++retries >= maxRetries) {
      clearInterval(iv);
      console.warn("BPF not ready after retries");
    }
  }, 500);
}

/** ============ ENTRY POINT ============ **/

function onLoadStageChangeEvent(executionContext) {
  var formCtx = executionContext.getFormContext();
  waitForBpfReady(formCtx, function(){
    bindStageChangeListener(formCtx);
    monitorStageAndAttachModal(formCtx);
  });
}

// Expose the name expected by the form
//window.syncBPFStatus_OnLoad = function(executionContext){
 // onLoadStageChangeEvent(executionContext);
//};

/** ============ STAGE -> STATUSCODE MAPPING ============ **/

function bindStageChangeListener(formCtx){
  formCtx.data.process.addOnStageChange(function(){
    var stg = formCtx.data.process.getActiveStage();
    var stageId = stg && stg.getId ? stg.getId().replace(/[{}]/g,"").toLowerCase() : null;
    var newStatus = stageToStatusMap[stageId];
    if (!stageId || typeof newStatus === "undefined") return;

    var statusAttr = formCtx.getAttribute("statuscode");
    if (statusAttr && statusAttr.getValue() !== newStatus) {
      statusAttr.setValue(newStatus);
      // Optionally auto-save:
      // formCtx.data.save();
    }
  });
}

/** ============ MODAL MANAGEMENT ============ **/

function injectBootstrapCss() {
  var head = window.top.document.head;
  if (!head.querySelector("#bootstrap-css")) {
    var link = document.createElement("link");
    link.id = "bootstrap-css";
    link.rel = "stylesheet";
    link.href = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css";
    head.appendChild(link);
  }
}

function openCommentModal(formContext) {
  injectBootstrapCss();
  var doc = window.top.document;

  var wrapper = doc.createElement("div");
  wrapper.innerHTML =
    '<div id="statusCommentModal" class="modal fade show" tabindex="-1" style="background-color:rgba(0,0,0,0.5);position:fixed;top:0;left:0;width:100%;height:100%;z-index:1055;display:flex;justify-content:center;align-items:center;">' +
      '<div class="modal-dialog modal-dialog-centered" style="max-width:500px;width:100%;">' +
        '<div class="modal-content shadow-lg border-0 rounded-3">' +
          '<div class="modal-header">' +
            '<h5 class="modal-title">Status Change Comment</h5>' +
            '<button type="button" class="btn-close" onclick="window.top.closeStatusCommentModal()"></button>' +
          '</div>' +
          '<div class="modal-body">' +
            '<textarea id="statusCommentText" class="form-control w-100" style="min-height:120px;" placeholder="Enter your comment..."></textarea>' +
          '</div>' +
          '<div class="modal-footer justify-content-end">' +
            '<button class="btn btn-primary px-4" onclick="window.top.submitStatusComment()">Submit</button>' +
          '</div>' +
        '</div>' +
      '</div>' +
    '</div>';

  doc.body.appendChild(wrapper);
  window.top._statusCommentContext = formContext;

  window.top.submitStatusComment = function(){
    var el = doc.getElementById("statusCommentText");
    var comment = el && el.value ? el.value.trim() : "";
    if (!comment) { alert("Please enter a comment."); return; }

    formContext.data.save().then(function(){
      var caseId = formContext.data.entity.getId().replace(/[{}]/g,"");
      var statusAttr = formContext.getAttribute("statuscode");
      var statusLabel = (statusAttr && statusAttr.getText) ? statusAttr.getText() : (statusAttr ? statusAttr.getValue() : "");

      var note = {
        subject: "Stage Change Comment",
        notetext: "[" + statusLabel + "] " + comment,
        "objectid_incident@odata.bind": "/incidents(" + caseId + ")"
      };

      Xrm.WebApi.createRecord("annotation", note).then(function(){
        assignCaseToCustomerService(formContext);
        // ➜ After comment, activate the TARGET stage
        setTimeout(function(){ activateProcessingStage(formContext); }, 800);
        window.top.closeStatusCommentModal();
      }, function(err){
        alert("Failed to save comment: " + err.message);
      });
    });
  };

  window.top.closeStatusCommentModal = function(){
    var modal = doc.getElementById("statusCommentModal");
    if (modal) modal.remove();
    window.top._statusCommentContext = null;
  };
}

/** ============ HOOK MODAL ON NEXT BUTTON (EN/AR) ============ **/

function monitorStageAndAttachModal(formCtx){
  var modalStageIds = MODAL_TRIGGER_STAGE_ID ? [norm(MODAL_TRIGGER_STAGE_ID)] : [];

  setInterval(function(){
    var stg = formCtx.data.process.getActiveStage();
    if (!stg) return;

    var stgId = stg.getId ? stg.getId().replace(/[{}]/g,"").toLowerCase() : null;

    var inTarget = false;
    if (modalStageIds.length) {
      inTarget = stgId && modalStageIds.indexOf(norm(stgId)) !== -1;
    } else {
      inTarget = stageNameMatchesAgainstList(stg, MODAL_TRIGGER_STAGE_NAMES);
    }

    var nextBtn = findNextStageButton(window.top.document);
    if (!nextBtn) return;

    if (!inTarget) {
      nextBtn.dataset.modalAttached = "";
      return;
    }

    if (nextBtn.dataset.modalAttached !== "true") {
      nextBtn.dataset.modalAttached = "true";
      nextBtn.addEventListener("click", function(e){
        e.preventDefault(); e.stopPropagation();
        if (!window.top.document.getElementById("statusCommentModal")) {
          openCommentModal(formCtx);
        }
      }, true);
    }
  }, 800);
}

/** ============ ASSIGN TO TEAM(S) ============ **/

function assignCaseToCustomerService(formContext){
  var caseId = formContext.data.entity.getId();
  (function tryAssign(i){
    if (i >= ASSIGN_TEAM_GUIDS.length) return;
    Xrm.WebApi.updateRecord("incident", caseId, {
      "ownerid@odata.bind": "/teams(" + ASSIGN_TEAM_GUIDS[i] + ")"
    }).then(function(){
      formContext.data.refresh(false);
    }).catch(function(){
      tryAssign(i+1);
    });
  })(0);
}

/** ============ ACTIVATE ANY STAGE BY ID/NAME ============ **/

  function activateProcessingStage(formContext) {
    const targetStageName = "Processing";

    try {
        const activePath = formContext.data.process.getActivePath();
        let targetStage = null;

        for (let i = 0; i < activePath.length; i++) {
            const s = activePath[i];
            if (s.getName().trim().toLowerCase() === targetStageName.toLowerCase()) {
                targetStage = s;
                break;
            }
        }

        if (!targetStage) {
            console.warn("⚠ Stage not found in UI path. Using Web API.");
            forceChangeViaWebAPI(formContext, targetStageName);
            return;
        }

        formContext.data.process.setActiveStage(targetStage.getId(), function (result) {
            if (result === "success") {
                console.log("✅ Stage changed to:", targetStageName);
				bindStageChangeListener(formCtx);
            } else {
                forceChangeViaWebAPI(formContext, targetStageName);
            }
        });
    } catch (err) {
        console.error("❌ Error in activateProcessingStage:", err.message);
    }
}

function forceChangeViaWebAPI(formContext, targetStageName) {
    try {
        const instanceId = formContext.data.process.getInstanceId();
        const processId = formContext.data.process.getActiveProcess().getId();
        if (!instanceId || !processId) return;

        Xrm.WebApi.retrieveRecord("workflow", processId, "?$select=uniquename").then(function (workflow) {
            const bpfEntityLogicalName = workflow.uniquename.toLowerCase();

            Xrm.WebApi.retrieveRecord(bpfEntityLogicalName, instanceId, "?$expand=processid($select=workflowid)").then(function (bpfRecord) {
                const actualProcessId = bpfRecord.processid.workflowid;

                Xrm.WebApi.retrieveMultipleRecords("processstage", `?$filter=processid/workflowid eq ${actualProcessId}`).then(function (stageResults) {
                    let matchedStage = null;
                    for (let i = 0; i < stageResults.entities.length; i++) {
                        const stage = stageResults.entities[i];
                        if (stage.stagename.trim().toLowerCase() === targetStageName.toLowerCase()) {
                            matchedStage = stage;
                            break;
                        }
                    }

                    if (!matchedStage) return;

                    const updateData = {
                        "activestageid@odata.bind": `/processstages(${matchedStage.processstageid})`
                    };

                    Xrm.WebApi.updateRecord(bpfEntityLogicalName, instanceId, updateData).then(function () {
                        console.log("✅ Stage updated via Web API.");
                    }, function (err) {
                        console.error("❌ Web API stage update failed:", err.message);
                    });
                });
            });
        });
    } catch (err) {
        console.error("❌ forceChangeViaWebAPI failed:", err.message);
    }
}
