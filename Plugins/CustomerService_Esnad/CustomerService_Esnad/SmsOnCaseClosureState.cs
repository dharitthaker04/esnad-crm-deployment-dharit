using System;
using System.Linq;
using System.Net.Http;
using System.ServiceModel;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Taadeen.Crm.Plugins
{
    public class SmsOnCaseClosureState : IPlugin
    {
        private const string BaseUrl = "https://api.oursms.com/api-a/msgs";
        private const string Username = "Taadeen2.0";
        private const string Token = "7sgOnsFhAuYdNgg5a3R4";
        private const string Sender = "Taadeen";

        private const int STATE_ACTIVE = 0;
        private const int STATE_RESOLVED = 1;
        private const int STATE_CANCELED = 2;

        public SmsOnCaseClosureState(string unsecure, string secure) { }

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);
            var adminService = factory.CreateOrganizationService(null); // System context
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            tracing.Trace("=== SmsOnCaseClosureState START ===");

            try
            {
                if (context.Depth > 1)
                {
                    tracing.Trace("Depth > 1 → skipping recursive execution.");
                    return;
                }

                if (context.PrimaryEntityName != "incident" ||
                    !context.InputParameters.Contains("Target") ||
                    !(context.InputParameters["Target"] is Entity target))
                {
                    tracing.Trace("Invalid context or no target → exiting.");
                    return;
                }

                int? oldState = null;
                if (context.PreEntityImages.Contains("PreImage") && context.PreEntityImages["PreImage"].Contains("statecode"))
                    oldState = ((OptionSetValue)context.PreEntityImages["PreImage"]["statecode"]).Value;

                if (!target.Attributes.Contains("statecode"))
                {
                    tracing.Trace("statecode not present in Target → skipping.");
                    return;
                }

                var newState = ((OptionSetValue)target["statecode"]).Value;
                tracing.Trace($"Old state={oldState}, New state={newState}");

                if (oldState == newState)
                {
                    tracing.Trace("No actual state change detected.");
                    return;
                }

                // Retrieve record details
                var incident = service.Retrieve("incident", context.PrimaryEntityId,
                    new ColumnSet("ticketnumber", "customerid", "new_smssentonclosure"));
                var ticket = incident.GetAttributeValue<string>("ticketnumber");

                // === CASE 1: Reopen case ===
                if (newState == STATE_ACTIVE)
                {
                    tracing.Trace("Case reopened → reset closure flag.");
                    TrySafeUpdateFlag(adminService, incident.Id, false, tracing);
                    return;
                }

                // === CASE 2: Case resolved ===
                if (newState == STATE_RESOLVED)
                {
                    bool already = incident.Contains("new_smssentonclosure") &&
                                   incident.GetAttributeValue<bool>("new_smssentonclosure");
                    if (already)
                    {
                        tracing.Trace("Closure SMS already sent → skipping.");
                        return;
                    }

                    tracing.Trace("Resolved state detected → sending closure SMS.");

                    var phone = ResolvePhone(incident, tracing, service);
                    if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(ticket))
                    {
                        tracing.Trace("Missing phone or ticket number → aborting SMS.");
                        return;
                    }

                    var baseUrl = GetConfigValue(service, "FeedbackBaseUrl", tracing)
                                  ?? "https://feedback.crm-esnad.com";

                    var body = SmsTemplates.ForTicketClosure(ticket, baseUrl);
                    SendSms(phone, body, tracing);

                    TrySafeUpdateFlag(adminService, incident.Id, true, tracing);
                    tracing.Trace("✅ Closure SMS sent and flag updated.");
                }

                // === CASE 3: Canceled ===
                else if (newState == STATE_CANCELED)
                {
                    tracing.Trace("Case canceled → skipping SMS but resetting flag.");
                    TrySafeUpdateFlag(adminService, incident.Id, false, tracing);
                }

                tracing.Trace("=== SmsOnCaseClosureState END ===");
            }
            catch (Exception ex)
            {
                tracing.Trace("❌ Exception: " + ex);
                throw new InvalidPluginExecutionException($"SmsOnCaseClosureState failed: {ex.Message}", ex);
            }
        }

        private static void TrySafeUpdateFlag(IOrganizationService adminService, Guid id, bool value, ITracingService tracing)
        {
            try
            {
                var update = new Entity("incident", id)
                {
                    ["new_smssentonclosure"] = value
                };
                adminService.Update(update);
                tracing.Trace($"new_smssentonclosure set to {(value ? "true" : "false")}.");
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                var msg = ex.Detail?.Message?.ToLowerInvariant() ?? "";
                if (msg.Contains("inactive") || msg.Contains("resolved"))
                    tracing.Trace("Ignored inactive record exception.");
                else
                    tracing.Trace("Flag update failed: " + ex.ToString());
            }
            catch (Exception ex)
            {
                tracing.Trace("Flag update failed: " + ex.Message);
            }
        }

        private static string GetConfigValue(IOrganizationService service, string name, ITracingService tracing)
        {
            try
            {
                var query = new QueryExpression("new_environmentvariable")
                {
                    ColumnSet = new ColumnSet("new_value"),
                    Criteria = { Conditions = { new ConditionExpression("new_name", ConditionOperator.Equal, name) } }
                };
                var record = service.RetrieveMultiple(query).Entities.FirstOrDefault();
                var value = record?.GetAttributeValue<string>("new_value");
                tracing.Trace($"Config {name} = {value}");
                return value;
            }
            catch (Exception ex)
            {
                tracing.Trace($"Config retrieval failed for {name}: {ex.Message}");
                return null;
            }
        }

        private static string ResolvePhone(Entity incident, ITracingService tracing, IOrganizationService service)
        {
            var cust = incident.GetAttributeValue<EntityReference>("customerid");
            if (cust == null) return null;

            Entity row;
            string raw = null;

            if (cust.LogicalName == "contact")
            {
                row = service.Retrieve(cust.LogicalName, cust.Id,
                    new ColumnSet("mobilephone", "telephone1", "telephone2"));
                raw = FirstNonEmpty(row.GetAttributeValue<string>("mobilephone"),
                                    row.GetAttributeValue<string>("telephone1"),
                                    row.GetAttributeValue<string>("telephone2"));
            }
            else if (cust.LogicalName == "account")
            {
                row = service.Retrieve(cust.LogicalName, cust.Id,
                    new ColumnSet("new_companyrepresentativephonenumber", "telephone1", "telephone2", "telephone3"));
                raw = FirstNonEmpty(row.GetAttributeValue<string>("new_companyrepresentativephonenumber"),
                                    row.GetAttributeValue<string>("telephone1"),
                                    row.GetAttributeValue<string>("telephone2"),
                                    row.GetAttributeValue<string>("telephone3"));
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                tracing.Trace("No phone found for customer.");
                return null;
            }

            var cleaned = Regex.Replace(raw, @"[^\d+]", "");
            tracing.Trace($"Resolved phone: {cleaned}");
            return cleaned;
        }

        private static string FirstNonEmpty(params string[] values) =>
            values?.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        private static void SendSms(string phone, string body, ITracingService tracing)
        {
            string enc(string s) => Uri.EscapeDataString(s ?? "");
            var url = $"{BaseUrl}?username={enc(Username)}&token={enc(Token)}" +
                      $"&dests={enc(phone)}&body={enc(body)}" +
                      $"&priority=0&delay=0&validity=0&maxParts=0&dlr=0&prevDups=0&src={enc(Sender)}";

            tracing.Trace("📤 Sending SMS → " + url);

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(15);
                var resp = http.GetAsync(url).GetAwaiter().GetResult();
                var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                tracing.Trace($"SMS Response: {(int)resp.StatusCode} | {content}");

                if (!resp.IsSuccessStatusCode)
                    throw new InvalidPluginExecutionException($"SMS failed: {resp.StatusCode} | {content}");
            }
        }

        private static class SmsTemplates
        {
            private const string RLE = "\u202B";
            private const string PDF = "\u202C";
            private const string RLM = "\u200F";

            public static string ForTicketClosure(string ticket, string baseUrl) =>
                $"{RLE}عزيزنا المستثمر,\r\n" +
                $"تم اغلاق التذكرة رقم {RLM}{ticket} وحرصاً منا لرفع مستوى الجودة يسعدنا تقييمكم للخدمة المقدمة:\r\n" +
                $"{PDF}{baseUrl}?ticketNumber={ticket}";
        }
    }
}
