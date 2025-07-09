// new_/AutoCloseOnClosure.js
var AutoCloseOnClosure = AutoCloseOnClosure || {};

(function () {
  /**
   * Logs messages to the browser console with a consistent prefix.
   */
  function log(level, message) {
    var prefix = "[AutoCloseOnClosure][" + level + "] ";
    if (level === "ERROR")    console.error(prefix + message);
    else if (level === "WARN") console.warn(prefix + message);
    else                       console.log(prefix + message);
  }

  /**
   * Form OnLoad: start polling for BPF stage changes.
   */
  AutoCloseOnClosure.onLoad = function (executionContext) {
    var formContext = executionContext.getFormContext();

    try {
      var process = formContext.data.process;
      if (!process) {
        log("ERROR", "No BPF process available.");
        return;
      }

      // Capture initial stage
      var lastStage = process.getActiveStage();
      if (lastStage) {
        log("INFO", "Starting poll. Initial stage: " + lastStage.getName());
      } else {
        log("WARN", "Starting poll but no initial active stage found.");
      }

      // Poll every 2 seconds
      var poller = setInterval(function () {
        var active = process.getActiveStage();
        if (!active) { return; }

        // If stage changed
        if (!lastStage || active.getId() !== lastStage.getId()) {
          lastStage = active;
          log("INFO", "BPF moved to: " + active.getName());

          if (active.getName() === "Ticket Closure") {
            clearInterval(poller);
            log("INFO", "Detected Ticket Closure → auto-closing case.");
            AutoCloseOnClosure.closeCaseViaWebApi(formContext, formContext.data.entity.getId(), /*resolvedReason=*/5);
          }
        }
      }, 2000);

    } catch (e) {
      log("ERROR", "onLoad exception: " + (e.message || e));
    }
  };

  /**
   * Closes the Case via the IncidentClose Web API action.
   */
  AutoCloseOnClosure.closeCaseViaWebApi = function (formContext, caseId, statusReason) {
    try {
      caseId = caseId.replace(/[{}]/g, "").toLowerCase();
      var resolution = {
        "@odata.type": "mscrm.incidentresolution",
        subject:     "Auto-closed on Ticket Closure",
        description: "Automatically closed when BPF hit Ticket Closure.",
        "incidentid@odata.bind": "/incidents(" + caseId + ")"
      };
      var data = {
        IncidentResolution: resolution,
        Status: statusReason
      };

      Xrm.WebApi.online.execute({
        entityName:    "incidentclose",
        operationName: "IncidentClose",
        data:           data
      }).then(function () {
        log("INFO", "Case auto-closed successfully.");
        formContext.data.refresh(true);
      }).catch(function (error) {
        log("ERROR", "Auto-close failed: " + (error.message || JSON.stringify(error)));
      });
    } catch (e) {
      log("ERROR", "closeCaseViaWebApi exception: " + (e.message || e));
    }
  };

})();
