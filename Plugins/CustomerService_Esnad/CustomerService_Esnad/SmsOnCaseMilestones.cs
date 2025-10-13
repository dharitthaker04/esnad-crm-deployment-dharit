using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Taadeen.Crm.Plugins
{
    public class SmsOnCaseMilestones : IPlugin
    {
        // ========================================
        // 🔹 Static gateway settings
        // ========================================
        private const string BaseUrl = "https://api.oursms.com/api-a/msgs";
        private const string Username = "Taadeen2.0";
        private const string Token = "7sgOnsFhAuYdNgg5a3R4";
        private const string Sender = "Taadeen";

        // ========================================
        // 🔹 StatusCode values
        // ========================================
        private const int STATUS_TICKET_CREATION = 100000000;
        private const int STATUS_RETURN_TO_CUSTOMER = 100000001;
        private const int STATUS_SOLUTION_VERIFICATION = 100000002;
        private const int STATUS_TICKET_CLOSURE = 5; // closure

        public SmsOnCaseMilestones(string unsecureConfig, string secureConfig) { }

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
                    tracing.Trace("Depth > 1 detected → skipping duplicate execution.");
                    return;
                }

                if (context.PrimaryEntityName != "incident")
                {
                    tracing.Trace("Not incident, exiting.");
                    return;
                }

                Entity incident;

                if (context.MessageName.Equals("Create", StringComparison.OrdinalIgnoreCase))
                {
                    tracing.Trace("Processing Create event...");
                    var id = (Guid)context.OutputParameters["id"];
                    incident = service.Retrieve("incident", id, new ColumnSet("ticketnumber", "customerid", "statuscode"));
                    SendForCreate(incident, tracing, service);
                }
                else if (context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase))
                {
                    tracing.Trace("Processing Update event...");
                    var target = (Entity)context.InputParameters["Target"];

                    if (!target.Attributes.Contains("statuscode"))
                    {
                        tracing.Trace("statuscode not in Target, exiting.");
                        return;
                    }

                    var id = target.Id;
                    incident = service.Retrieve("incident", id,
                        new ColumnSet("ticketnumber", "customerid", "statuscode", "new_smssentonclosure"));

                    int? oldStatus = null;
                    if (context.PreEntityImages.Contains("PreImage") && context.PreEntityImages["PreImage"].Contains("statuscode"))
                        oldStatus = ((OptionSetValue)context.PreEntityImages["PreImage"]["statuscode"]).Value;

                    var newStatus = incident.Contains("statuscode") ? ((OptionSetValue)incident["statuscode"]).Value : (int?)null;

                    tracing.Trace($"Old status: {oldStatus}, New status: {newStatus}");

                    if (newStatus == null || (oldStatus.HasValue && oldStatus.Value == newStatus.Value))
                    {
                        tracing.Trace("No status change detected → exiting.");
                        return;
                    }

                    SendForStatus(incident, newStatus.Value, tracing, service);
                }

                tracing.Trace("=== SmsOnCaseMilestones END ===");
            }
            catch (Exception ex)
            {
                tracing.Trace("Exception caught: " + ex.ToString());
                throw new InvalidPluginExecutionException("SmsOnCaseMilestones failed: " + ex.Message, ex);
            }
        }

        private string GetConfigValue(IOrganizationService service, string name, ITracingService tracing)
        {
            tracing.Trace($"Fetching config value for: {name}");

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
                var value = result.GetAttributeValue<string>("new_value");
                tracing.Trace($"✅ Config {name} resolved to: {value}");
                return value;
            }

            tracing.Trace($"❌ Config {name} not found.");
            throw new InvalidPluginExecutionException($"Config {name} missing in new_environmentvariable.");
        }

        private void SendForCreate(Entity incident, ITracingService tracing, IOrganizationService service)
        {
            tracing.Trace("SendForCreate called");

            var ticket = incident.GetAttributeValue<string>("ticketnumber");
            var phone = ResolvePhone(incident, tracing, service);

            if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(phone))
            {
                tracing.Trace("Missing ticketnumber or phone, aborting SMS.");
                return;
            }

            var body = SmsTemplates.ForTicketCreation(ticket);
            SendSms(phone, body, tracing);
        }

        private void SendForStatus(Entity incident, int newStatus, ITracingService tracing, IOrganizationService service)
        {
            tracing.Trace($"SendForStatus called for statuscode={newStatus}");
            var ticket = incident.GetAttributeValue<string>("ticketnumber");
            var phone = ResolvePhone(incident, tracing, service);

            if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(phone))
            {
                tracing.Trace("Missing ticketnumber or phone, aborting SMS.");
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

                /* 🚫 Ticket Closure logic temporarily disabled
                case STATUS_TICKET_CLOSURE:
                    tracing.Trace("Status = Ticket Closure");
                    if (incident.Contains("new_smssentonclosure") && incident.GetAttributeValue<bool>("new_smssentonclosure"))
                    {
                        tracing.Trace("Closure SMS already sent → skipping.");
                        return;
                    }

                    var baseUrl = GetConfigValue(service, "FeedbackBaseUrl", tracing)
                                  ?? "https://feedback.crm-esnad.com";

                    body = SmsTemplates.ForTicketClosure(ticket, baseUrl);
                    SendSms(phone, body, tracing);

                    var update = new Entity("incident", incident.Id)
                    {
                        ["new_smssentonclosure"] = true
                    };
                    service.Update(update);

                    tracing.Trace("✅ Closure SMS sent and flag updated.");
                    break;
                */

                default:
                    tracing.Trace("No SMS mapped for this status, skipping.");
                    return;
            }

            if (!string.IsNullOrWhiteSpace(body))
            {
                SendSms(phone, body, tracing);
            }
        }

        private string ResolvePhone(Entity incident, ITracingService tracing, IOrganizationService service)
        {
            tracing.Trace("ResolvePhone called");
            var cust = incident.GetAttributeValue<EntityReference>("customerid");
            if (cust == null)
            {
                tracing.Trace("No customerid on case.");
                return null;
            }

            Entity row;
            string raw = null;

            if (cust.LogicalName == "contact")
            {
                row = service.Retrieve(cust.LogicalName, cust.Id,
                    new ColumnSet("mobilephone", "telephone1", "telephone2"));
                raw = FirstNonEmpty(
                    row.GetAttributeValue<string>("mobilephone"),
                    row.GetAttributeValue<string>("telephone1"),
                    row.GetAttributeValue<string>("telephone2"));
            }
            else if (cust.LogicalName == "account")
            {
                row = service.Retrieve(cust.LogicalName, cust.Id,
                    new ColumnSet("new_companyrepresentativephonenumber", "telephone1", "telephone2", "telephone3"));
                raw = FirstNonEmpty(
                    row.GetAttributeValue<string>("new_companyrepresentativephonenumber"),
                    row.GetAttributeValue<string>("telephone1"),
                    row.GetAttributeValue<string>("telephone2"),
                    row.GetAttributeValue<string>("telephone3"));
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                tracing.Trace("No phone found on customer.");
                return null;
            }

            var cleaned = Regex.Replace(raw, @"[^\d+]", "");
            tracing.Trace($"Cleaned phone: {cleaned}");
            return cleaned;
        }

        private static string FirstNonEmpty(params string[] items) =>
            items?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        private void SendSms(string phone, string body, ITracingService tracing)
        {
            string enc(string s) => Uri.EscapeDataString(s ?? string.Empty);

            var url = $"{BaseUrl}?username={enc(Username)}&token={enc(Token)}" +
                      $"&dests={enc(phone)}&body={enc(body)}" +
                      $"&priority=0&delay=0&validity=0&maxParts=0&dlr=0&prevDups=0" +
                      $"&src={enc(Sender)}";

            tracing.Trace($"Sending SMS → {url}");

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(15);
                var resp = http.GetAsync(url).GetAwaiter().GetResult();
                var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                tracing.Trace($"SMS Response: HTTP {(int)resp.StatusCode}, Body={content}");

                if (!resp.IsSuccessStatusCode)
                    throw new InvalidPluginExecutionException($"SMS failed: {resp.StatusCode} {content}");
            }
        }

        private static class SmsTemplates
        {
            private const string RLE = "\u202B";
            private const string PDF = "\u202C";
            private const string RLM = "\u200F";

            public static string ForTicketCreation(string ticket) =>
                $"{RLE}عزيزنا المستثمر,\r\nنشكر لكم تواصلكم معنا, ونفيدكم بأنه تم إنشاء تذكرة جديدة برقم {RLM}{ticket}.{PDF}";

            public static string ForReturnToCustomer(string ticket) =>
                $"{RLE}عزيزنا المستثمر,\r\nتم إعادة التذكرة رقم {RLM}{ticket} لاستكمال بعض المتطلبات، يرجى التكرم بإستكمالها عبر الرد على البريد الإلكتروني المرسل. علمًا بأن التذكرة ستغلق تلقائيًا خلال خمسة أيام عمل في حال عدم الرد.{PDF}";

            public static string ForSolutionVerification(string ticket) =>
                $"{RLE}عزيزنا المستثمر،\r\nتم معالجة التذكرة رقم {RLM}{ticket}. وفي حال استمرار المشكلة، يرجى التكرم بالرد على البريد الإلكتروني المرسل. علمًا بأن التذكرة ستغلق تلقائيًا خلال خمسة أيام عمل في حال عدم الرد.{PDF}";

            public static string ForTicketClosure(string ticket, string baseUrl) =>
                $"{RLE}عزيزنا المستثمر,\r\n" +
                $"تم اغلاق التذكرة رقم {RLM}{ticket} وحرصاً منا لرفع مستوى الجودة يسعدنا تقييمكم للخدمة المقدمة:\r\n" +
                $"{PDF}{baseUrl}?ticketNumber={ticket}";
        }
    }
}
