using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Taadeen.Cm.Plugins
{
    public class SmsOnCaseMilestones1 : IPlugin
    {
        // ========================================
        // 🔹 SMS Gateway Settings
        // ========================================
        private const string BaseUrl = "https://api.oursms.com/api-a/msgs";
        private const string Username = "Taadeen2.0";
        private const string Token = "7sgOnsFhAuYdNgg5a3R4";
        private const string Sender = "Taadeen";

        // ========================================
        // 🔹 StatusCode and StateCode Values
        // ========================================
        private const int STATUS_RETURN_TO_CUSTOMER = 100000001;
        private const int STATUS_SOLUTION_VERIFICATION = 100000002;
        private const int STATE_RESOLVED = 1;

        public SmsOnCaseMilestones1(string unsecureConfig, string secureConfig) { }

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                tracing.Trace("=== SmsOnCaseMilestones START ===");

                if (context.Depth > 1)
                {
                    tracing.Trace("⚠️ Depth > 1 detected — skipping duplicate execution.");
                    return;
                }

                if (context.PrimaryEntityName != "incident")
                {
                    tracing.Trace("Not an incident entity — exiting.");
                    return;
                }

                // ------------------------------------------------
                // 🔹 Handle CREATE (Ticket Creation SMS)
                // ------------------------------------------------
                if (context.MessageName.Equals("Create", StringComparison.OrdinalIgnoreCase))
                {
                    tracing.Trace("Processing Create event...");
                    var id = (Guid)context.OutputParameters["id"];
                    var incident = service.Retrieve("incident", id, new ColumnSet("ticketnumber", "customerid"));
                    SendSmsTicketCreated(incident, tracing, service);
                    return;
                }

                // ------------------------------------------------
                // 🔹 Handle UPDATE (Milestones + Closure)
                // ------------------------------------------------
                if (context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase))
                {
                    var target = (Entity)context.InputParameters["Target"];
                    var id = target.Id;

                    // ✅ CASE 1 — Closure via StateCode = 1 (Resolved)
                    if (target.Attributes.Contains("statecode"))
                    {
                        var newState = ((OptionSetValue)target["statecode"]).Value;
                        tracing.Trace($"Detected statecode change: {newState}");

                        if (newState == STATE_RESOLVED)
                        {
                            tracing.Trace("Case moved to Resolved — sending closure SMS...");
                            SendSmsOnClosure(id, tracing, service);
                            return;
                        }
                    }

                    // ✅ CASE 2 — Milestone via StatusCode Change
                    if (target.Attributes.Contains("statuscode"))
                    {
                        var newStatus = ((OptionSetValue)target["statuscode"]).Value;
                        tracing.Trace($"Detected statuscode change: {newStatus}");

                        var incident = service.Retrieve("incident", id, new ColumnSet("ticketnumber", "customerid", "statuscode"));
                        SendSmsForMilestone(incident, newStatus, tracing, service);
                    }
                }

                tracing.Trace("=== SmsOnCaseMilestones END ===");
            }
            catch (Exception ex)
            {
                tracing.Trace("❌ Exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("SmsOnCaseMilestones failed: " + ex.Message, ex);
            }
        }

        // =====================================================
        // 🔹 SMS Senders
        // =====================================================
        private void SendSmsTicketCreated(Entity incident, ITracingService tracing, IOrganizationService service)
        {
            var ticket = incident.GetAttributeValue<string>("ticketnumber");
            var phone = ResolvePhone(incident, tracing, service);

            if (string.IsNullOrEmpty(ticket) || string.IsNullOrEmpty(phone))
            {
                tracing.Trace("Missing ticketnumber or phone — skipping SMS.");
                return;
            }

            var body = SmsTemplates.ForTicketCreation(ticket);
            SendSms(phone, body, tracing);
        }

        private void SendSmsForMilestone(Entity incident, int newStatus, ITracingService tracing, IOrganizationService service)
        {
            var ticket = incident.GetAttributeValue<string>("ticketnumber");
            var phone = ResolvePhone(incident, tracing, service);

            if (string.IsNullOrEmpty(ticket) || string.IsNullOrEmpty(phone))
            {
                tracing.Trace("Missing ticketnumber or phone — skipping SMS.");
                return;
            }

            string body = null;

            switch (newStatus)
            {
                case STATUS_RETURN_TO_CUSTOMER:
                    body = SmsTemplates.ForReturnToCustomer(ticket);
                    break;
                case STATUS_SOLUTION_VERIFICATION:
                    body = SmsTemplates.ForSolutionVerification(ticket);
                    break;
                default:
                    tracing.Trace("No milestone SMS defined for this status.");
                    return;
            }

            SendSms(phone, body, tracing);
        }

        private void SendSmsOnClosure(Guid incidentId, ITracingService tracing, IOrganizationService service)
        {
            var incident = service.Retrieve("incident", incidentId, new ColumnSet("ticketnumber", "customerid", "statecode"));

            var state = incident.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? -1;
            if (state != STATE_RESOLVED)
            {
                tracing.Trace("Incident is not resolved (statecode != 1) — skipping SMS.");
                return;
            }

            var ticket = incident.GetAttributeValue<string>("ticketnumber");
            var phone = ResolvePhone(incident, tracing, service);

            if (string.IsNullOrEmpty(ticket) || string.IsNullOrEmpty(phone))
            {
                tracing.Trace("Missing ticketnumber or phone — skipping SMS.");
                return;
            }

            var baseUrl = GetConfigValue(service, "FeedbackBaseUrl", tracing)
                          ?? "https://feedback.crm-esnad.com";

            var body = SmsTemplates.ForTicketClosure(ticket, baseUrl);
            SendSms(phone, body, tracing);
        }

        // =====================================================
        // 🔹 Helpers
        // =====================================================
        private string ResolvePhone(Entity incident, ITracingService tracing, IOrganizationService service)
        {
            var cust = incident.GetAttributeValue<EntityReference>("customerid");
            if (cust == null)
            {
                tracing.Trace("No customer found on case.");
                return null;
            }

            Entity record;
            string raw = null;

            if (cust.LogicalName == "contact")
            {
                record = service.Retrieve("contact", cust.Id, new ColumnSet("mobilephone", "telephone1", "telephone2"));
                raw = FirstNonEmpty(record.GetAttributeValue<string>("mobilephone"),
                                    record.GetAttributeValue<string>("telephone1"),
                                    record.GetAttributeValue<string>("telephone2"));
            }
            else if (cust.LogicalName == "account")
            {
                record = service.Retrieve("account", cust.Id, new ColumnSet("new_companyrepresentativephonenumber", "telephone1", "telephone2", "telephone3"));
                raw = FirstNonEmpty(record.GetAttributeValue<string>("new_companyrepresentativephonenumber"),
                                    record.GetAttributeValue<string>("telephone1"),
                                    record.GetAttributeValue<string>("telephone2"),
                                    record.GetAttributeValue<string>("telephone3"));
            }

            if (string.IsNullOrEmpty(raw))
            {
                tracing.Trace("No valid phone found.");
                return null;
            }

            var cleaned = Regex.Replace(raw, @"[^\d+]", "");
            tracing.Trace($"Resolved phone: {cleaned}");
            return cleaned;
        }

        private string GetConfigValue(IOrganizationService service, string name, ITracingService tracing)
        {
            try
            {
                var query = new QueryExpression("new_environmentvariable")
                {
                    ColumnSet = new ColumnSet("new_value"),
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression("new_name", ConditionOperator.Equal, name)
                        }
                    }
                };

                var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
                if (result != null && result.Contains("new_value"))
                {
                    return result.GetAttributeValue<string>("new_value");
                }
                return null;
            }
            catch (Exception ex)
            {
                tracing.Trace($"Error retrieving config {name}: {ex.Message}");
                return null;
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values?.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        private void SendSms(string phone, string body, ITracingService tracing)
        {
            string enc(string s) => Uri.EscapeDataString(s ?? string.Empty);

            var url = $"{BaseUrl}?username={enc(Username)}&token={enc(Token)}&dests={enc(phone)}&body={enc(body)}" +
                      $"&priority=0&delay=0&validity=0&maxParts=0&dlr=0&prevDups=0&src={enc(Sender)}";

            tracing.Trace($"Sending SMS to {phone} → {url}");

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(15);
                var resp = http.GetAsync(url).GetAwaiter().GetResult();
                var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                tracing.Trace($"SMS Response: HTTP {(int)resp.StatusCode}, Body={content}");
            }
        }

        // =====================================================
        // 🔹 SMS Templates
        // =====================================================
        private static class SmsTemplates
        {
            private const string RLE = "\u202B";
            private const string PDF = "\u202C";
            private const string RLM = "\u200F";

            public static string ForTicketCreation(string ticket) =>
                $"{RLE}عزيزنا المستثمر، نشكر لكم تواصلكم معنا. تم إنشاء تذكرة جديدة برقم {RLM}{ticket}.{PDF}";

            public static string ForReturnToCustomer(string ticket) =>
                $"{RLE}عزيزنا المستثمر، تم إعادة التذكرة رقم {RLM}{ticket} لاستكمال بعض المتطلبات، يرجى الرد على البريد الإلكتروني المرسل. ستُغلق تلقائيًا خلال خمسة أيام عمل في حال عدم الرد.{PDF}";

            public static string ForSolutionVerification(string ticket) =>
                $"{RLE}عزيزنا المستثمر، تم معالجة التذكرة رقم {RLM}{ticket}. في حال استمرار المشكلة، يرجى الرد على البريد الإلكتروني المرسل. ستُغلق تلقائيًا خلال خمسة أيام عمل في حال عدم الرد.{PDF}";

            public static string ForTicketClosure(string ticket, string baseUrl) =>
                $"{RLE}عزيزنا المستثمر، تم إغلاق التذكرة رقم {RLM}{ticket}. نأمل تقييمكم للخدمة المقدمة:\r\n{PDF}{baseUrl}?ticketNumber={ticket}";
        }
    }
}
