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
        // Static values for your gateway
        private const string BaseUrl = "https://api.oursms.com/api-a/msgs";
        private const string Username = "Taadeen2.0";
        private const string Token = "7sgOnsFhAuYdNgg5a3R4";
        private const string Sender = "Taadeen";

        // StatusCode values
        private const int STATUS_TICKET_CREATION = 100000000;
        private const int STATUS_RETURN_TO_CUSTOMER = 100000001;
        private const int STATUS_SOLUTION_VERIFICATION = 100000002;
        private const int STATUS_TICKET_CLOSURE = 100000008;

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
                tracing.Trace($"MessageName: {context.MessageName}, PrimaryEntity: {context.PrimaryEntityName}");

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
                    tracing.Trace($"Retrieved incident {incident.Id}, ticketnumber={incident.GetAttributeValue<string>("ticketnumber")}");
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
                    incident = service.Retrieve("incident", id, new ColumnSet("ticketnumber", "customerid", "statuscode"));
                    tracing.Trace($"Retrieved incident {incident.Id}, ticketnumber={incident.GetAttributeValue<string>("ticketnumber")}");

                    int? oldStatus = null;
                    if (context.PreEntityImages.Contains("PreImage") && context.PreEntityImages["PreImage"].Contains("statuscode"))
                    {
                        oldStatus = ((OptionSetValue)context.PreEntityImages["PreImage"]["statuscode"]).Value;
                        tracing.Trace($"PreImage old statuscode: {oldStatus}");
                    }

                    var newStatus = incident.Contains("statuscode") ? ((OptionSetValue)incident["statuscode"]).Value : (int?)null;
                    tracing.Trace($"New statuscode: {newStatus}");

                    if (newStatus == null || (oldStatus.HasValue && oldStatus.Value == newStatus.Value))
                    {
                        tracing.Trace("No status change, exiting.");
                        return;
                    }

                    SendForStatus(incident, newStatus.Value, tracing, service);
                }

                tracing.Trace("=== SmsOnCaseMilestones END ===");
            }
            catch (Exception ex)
            {
                tracing.Trace("Exception caught: " + ex.ToString());
                throw new InvalidPluginExecutionException("SmsOnCaseMilestones failed, see trace log: " + ex.Message, ex);
            }
        }

        // 🔹 Helper to fetch values from your custom environmentvariable entity
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
            tracing.Trace($"Ticket={ticket}, Phone={phone}");

            if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(phone))
            {
                tracing.Trace("Missing ticketnumber or phone, aborting SMS.");
                return;
            }

            var body = SmsTemplates.ForTicketCreation(ticket);
            tracing.Trace("Generated SMS body for Create.");
            SendSms(phone, body, tracing);
        }

        private void SendForStatus(Entity incident, int newStatus, ITracingService tracing, IOrganizationService service)
        {
            tracing.Trace($"SendForStatus called for statuscode={newStatus}");
            var ticket = incident.GetAttributeValue<string>("ticketnumber");
            var phone = ResolvePhone(incident, tracing, service);
            tracing.Trace($"Ticket={ticket}, Phone={phone}");

            if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(phone))
            {
                tracing.Trace("Missing ticketnumber or phone, aborting SMS.");
                return;
            }

            string body = null;
            if (newStatus == STATUS_RETURN_TO_CUSTOMER)
                body = SmsTemplates.ForReturnToCustomer(ticket);
            else if (newStatus == STATUS_SOLUTION_VERIFICATION)
                body = SmsTemplates.ForSolutionVerification(ticket);
            else if (newStatus == STATUS_TICKET_CLOSURE)
            {
                var baseUrl = GetConfigValue(service, "FeedbackBaseUrl", tracing)
                              ?? "https://feedback.crm-esnad.com"; // fallback if not found
                body = SmsTemplates.ForTicketClosure(ticket, baseUrl);
            }

            if (!string.IsNullOrWhiteSpace(body))
            {
                tracing.Trace("Generated SMS body for Status.");
                SendSms(phone, body, tracing);
            }
            else
            {
                tracing.Trace("No SMS body mapped for this statuscode, skipping.");
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

            tracing.Trace($"Customer logicalName={cust.LogicalName}, Id={cust.Id}");

            Entity row;
            string raw = null;

            if (cust.LogicalName == "contact")
            {
                row = service.Retrieve(cust.LogicalName, cust.Id,
                    new ColumnSet("mobilephone", "telephone1", "telephone2"));
                tracing.Trace("Retrieved contact record.");

                raw = FirstNonEmpty(
                    row.GetAttributeValue<string>("mobilephone"),
                    row.GetAttributeValue<string>("telephone1"),
                    row.GetAttributeValue<string>("telephone2")
                );
            }
            else if (cust.LogicalName == "account")
            {
                row = service.Retrieve(cust.LogicalName, cust.Id,
                    new ColumnSet("new_companyrepresentativephonenumber", "telephone1", "telephone2", "telephone3"));
                tracing.Trace("Retrieved account record.");

                raw = FirstNonEmpty(
                    row.GetAttributeValue<string>("new_companyrepresentativephonenumber"),
                    row.GetAttributeValue<string>("telephone1"),
                    row.GetAttributeValue<string>("telephone2"),
                    row.GetAttributeValue<string>("telephone3")
                );
            }
            else
            {
                tracing.Trace("Unsupported customer type, exiting.");
                return null;
            }

            tracing.Trace($"Raw phone={raw}");
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var cleaned = Regex.Replace(raw, @"[^\d+]", "");
            tracing.Trace($"Cleaned phone={cleaned}");
            return cleaned;
        }

        private static string FirstNonEmpty(params string[] items) =>
            items?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        private void SendSms(string phone, string body, ITracingService tracing)
        {
            tracing.Trace("SendSms called");
            string enc(string s) => Uri.EscapeDataString(s ?? string.Empty);

            var url = $"{BaseUrl}?username={enc(Username)}&token={enc(Token)}" +
                      $"&dests={enc(phone)}&body={enc(body)}" +
                      $"&priority=0&delay=0&validity=0&maxParts=0&dlr=0&prevDups=0" +
                      $"&src={enc(Sender)}";

            tracing.Trace($"Final SMS URL: {url}");

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(15);
                try
                {
                    var resp = http.GetAsync(url).GetAwaiter().GetResult();
                    var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    tracing.Trace($"SMS response: HTTP {(int)resp.StatusCode}, Content={content}");

                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidPluginExecutionException($"SMS failed: {resp.StatusCode} {content}");
                }
                catch (Exception ex)
                {
                    tracing.Trace("HTTP call exception: " + ex.ToString());
                    throw;
                }
            }
        }

        private static class SmsTemplates
        {
            private const string RLE = "\u202B"; // Right-to-Left Embedding
            private const string PDF = "\u202C"; // Pop Directional Formatting
            private const string RLM = "\u200F"; // Right-to-Left Mark (for numbers)

            public static string ForTicketCreation(string ticket) =>
                $"{RLE}عزيزنا المستثمر,\r\nنشكر لكم تواصلكم معنا, ونفيدكم بأنه تم إنشاء تذكرة جديدة برقم {RLM}{ticket}.{PDF}";

            public static string ForReturnToCustomer(string ticket) =>
                $"{RLE}عزيزنا المستثمر,\r\nتم إعادة التذكرة رقم {RLM}{ticket} لاستكمال بعض المتطلبات، يرجى التكرم بإستكمالها عبر الرد على البريد الإلكتروني المرسل. علمًا بأن التذكرة ستغلق تلقائيًا خلال خمسة أيام عمل في حال عدم الرد.{PDF}";

            public static string ForSolutionVerification(string ticket) =>
                $"{RLE}عزيزنا المستثمر،\r\nتم معالجة التذكرة رقم {RLM}{ticket}. وفي حال استمرار المشكلة، يرجى التكرم بالرد على البريد الإلكتروني المرسل. علمًا بأن التذكرة ستغلق تلقائيًا خلال خمسة أيام عمل في حال عدم الرد.{PDF}";

            // 🔹 Closure now uses environment variable
            public static string ForTicketClosure(string ticket, string baseUrl) =>
                $"\u202Bعزيزنا المستثمر,\r\n" +
                $"تم اغلاق التذكرة رقم \u200F{ticket} وحرصاً منا لرفع مستوى الجودة يسعدنا تقييمكم للخدمة المقدمة:\r\n" +
                $"\u202C{baseUrl}?ticketNumber={ticket}";
        }
    }
}
