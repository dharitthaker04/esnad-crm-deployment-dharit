using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerService_Esnad
{
    public class TrackStageDuration : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            tracing.Trace("=== TrackStageDuration plugin start ===");

            try
            {
                // Only run for Update of activestageid
                if (context.MessageName != "Update" || !context.InputParameters.Contains("Target"))
                    return;

                var target = (Entity)context.InputParameters["Target"];
                if (!target.Contains("activestageid"))
                    return;

                // Retrieve the current BPF record to get case + stage info
                var bpfRecord = service.Retrieve(
                    context.PrimaryEntityName,
                    context.PrimaryEntityId,
                    new ColumnSet("incidentid", "activestageid", "modifiedon")
                );

                var ticketRef = bpfRecord.GetAttributeValue<EntityReference>("incidentid");
                var activeStage = bpfRecord.GetAttributeValue<EntityReference>("activestageid");

                if (ticketRef == null || activeStage == null)
                {
                    tracing.Trace("TicketRef or ActiveStage is null — skipping.");
                    return;
                }

                string currentStageName = activeStage.Name ?? "Unknown Stage";
                tracing.Trace($"Current Stage: {currentStageName}");

                // Retrieve last stage record for this ticket
                QueryExpression lastStageQuery = new QueryExpression("new_ticketstagehistory")
                {
                    ColumnSet = new ColumnSet("createdon", "new_name"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("new_ticket", ConditionOperator.Equal, ticketRef.Id)
                        }
                    },
                    Orders =
                    {
                        new OrderExpression("createdon", OrderType.Descending)
                    },
                    TopCount = 1
                };

                var historyResults = service.RetrieveMultiple(lastStageQuery);

                if (historyResults.Entities.Count > 0)
                {
                    // 🟩 Close the previous stage
                    var lastStage = historyResults.Entities[0];
                    string lastStageName = lastStage.GetAttributeValue<string>("new_name");
                    DateTime? lastStart = lastStage.GetAttributeValue<DateTime?>("createdon");

                    if (lastStart.HasValue)
                    {
                        DateTime endTime = DateTime.UtcNow;
                        TimeSpan stageDuration = endTime - lastStart.Value;

                        // 🕒 Convert to custom decimal format (1m = 0.01h, 30m = 0.30h)
                        int hours = (int)stageDuration.TotalHours;
                        int minutes = stageDuration.Minutes;
                        double durationHours = Math.Round(hours + (minutes / 100.0), 2);

                        // 🕒 Create readable text like "2h 35m"
                        string formattedDuration = $"{hours}h {minutes}m";

                        var updateStage = new Entity("new_ticketstagehistory", lastStage.Id)
                        {
                            ["new_endtime"] = endTime,
                            ["new_durationhours"] = durationHours
                            //["new_durationformatted"] = formattedDuration
                        };

                        service.Update(updateStage);

                        tracing.Trace($"✅ Closed '{lastStageName}' – Duration: {formattedDuration} ({durationHours} hours)");
                    }


                }
                else
                {
                    // ⚠ No prior history — initialize new record for current stage
                    tracing.Trace("No previous history found. Creating initial stage entry.");

                    var initStage = new Entity("new_ticketstagehistory")
                    {
                        ["new_ticket"] = ticketRef,
                        ["new_name"] = currentStageName,
                       // ["new_starttime"] = DateTime.UtcNow
                    };
                    service.Create(initStage);

                    tracing.Trace($"🟢 Created initial history for '{currentStageName}'.");
                    return;
                }

                // 🟦 Create new record for current stage
                var newStageHistory = new Entity("new_ticketstagehistory")
                {
                    ["new_ticket"] = ticketRef,
                    ["new_name"] = currentStageName,
                   // ["new_starttime"] = DateTime.UtcNow
                };
                service.Create(newStageHistory);

                tracing.Trace($"🟢 New stage '{currentStageName}' started at {DateTime.UtcNow:u}");
                tracing.Trace("=== TrackStageDuration plugin end ===");
            }
            catch (Exception ex)
            {
                tracing.Trace("❌ Exception in TrackStageDuration: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in TrackStageDuration plugin: " + ex.Message, ex);
            }
        }
    }
}
