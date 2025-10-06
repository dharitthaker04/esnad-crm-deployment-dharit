using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Taadeen.Crm.Plugins
{
    public class SmsOnVisitorCreate : IPlugin
    {
        private const string SmsGatewayUrl = "https://api.oursms.com/api-a/msgs";
        private const string Username = "Taadeen2.0";
        private const string Token = "7sgOnsFhAuYdNgg5a3R4";
        private const string Sender = "Taadeen";

        private const string ENTITY_VISITOR = "new_visitor";

        public SmsOnVisitorCreate(string unsecure, string secure) { }

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            tracing.Trace("=== SmsOnVisitorCreate START ===");

            try
            {
                // ✅ Ensure plugin runs only on Create of new_visitor
                if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(context.PrimaryEntityName, ENTITY_VISITOR, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidPluginExecutionException(
                        $"Wrong trigger. Message={context.MessageName}, Entity={context.PrimaryEntityName}");
                }

                if (!context.OutputParameters.Contains("id"))
                    throw new InvalidPluginExecutionException("OutputParameters does not contain 'id'");

                var visitorId = (Guid)context.OutputParameters["id"];
                var cols = new ColumnSet("new_contactname", "new_companyname", "new_visitornumber");
                var visitor = service.Retrieve(ENTITY_VISITOR, visitorId, cols);
                if (visitor == null)
                    throw new InvalidPluginExecutionException("Visitor record could not be retrieved");

                string phone = null;

                // 1️⃣ Try Contact phone
                var contactRef = visitor.GetAttributeValue<EntityReference>("new_contactname");
                if (contactRef != null)
                    phone = ResolvePhoneForContact(service, contactRef, tracing);

                // 2️⃣ Fallback: Account phone
                if (string.IsNullOrWhiteSpace(phone))
                {
                    var accountRef = visitor.GetAttributeValue<EntityReference>("new_companyname");
                    if (accountRef != null)
                        phone = ResolvePhoneForAccount(service, accountRef, tracing);
                }

                if (string.IsNullOrWhiteSpace(phone))
                    throw new InvalidPluginExecutionException("No phone number found for Visitor.");

                // ✅ SMS Body
                var body = SmsTemplates.ForVisitorCreate();

                // 🔗 Append Visitor Feedback URL using environment variable
                var visitorNumber = visitor.GetAttributeValue<string>("new_visitornumber");
                if (!string.IsNullOrWhiteSpace(visitorNumber))
                {
                    var baseUrl = GetConfigValue(service, "FeedbackBaseUrl", tracing);
                    body += $"\n{baseUrl}/visitor?visitorId={visitorNumber}";
                    tracing.Trace($"Feedback URL built with VisitorId={visitorNumber}, base={baseUrl}");
                }
                else
                {
                    tracing.Trace("VisitorNumber missing, skipping feedback URL.");
                }

                // ✅ Send SMS
                SendSms(phone, body, tracing);

                tracing.Trace("=== SmsOnVisitorCreate END ===");
            }
            catch (Exception ex)
            {
                tracing.Trace("❌ Exception: " + ex);
                throw new InvalidPluginExecutionException("SmsOnVisitorCreate failed: " + ex.Message, ex);
            }
        }

        // 🔹 Helper to fetch values from your custom environmentvariable entity
        private string GetConfigValue(IOrganizationService service, string name, ITracingService tracing)
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
                var value = result.GetAttributeValue<string>("new_value");
                tracing.Trace($"Config {name} resolved to: {value}");
                return value;
            }

            tracing.Trace($"❌ Config {name} not found.");
            throw new InvalidPluginExecutionException($"Config {name} missing in new_environmentvariable.");
        }

        private string ResolvePhoneForContact(IOrganizationService service, EntityReference contactRef, ITracingService tracing)
        {
            var c = service.Retrieve("contact", contactRef.Id,
                new ColumnSet("mobilephone", "telephone1", "telephone2"));
            var raw = FirstNonEmpty(
                c.GetAttributeValue<string>("mobilephone"),
                c.GetAttributeValue<string>("telephone1"),
                c.GetAttributeValue<string>("telephone2")
            );
            tracing.Trace($"Resolved contact phone: {raw}");
            return CleanPhone(raw);
        }

        private string ResolvePhoneForAccount(IOrganizationService service, EntityReference accountRef, ITracingService tracing)
        {
            var a = service.Retrieve("account", accountRef.Id,
                new ColumnSet("new_companyrepresentativephonenumber", "telephone1", "telephone2", "telephone3"));
            var raw = FirstNonEmpty(
                a.GetAttributeValue<string>("new_companyrepresentativephonenumber"),
                a.GetAttributeValue<string>("telephone1"),
                a.GetAttributeValue<string>("telephone2"),
                a.GetAttributeValue<string>("telephone3")
            );
            tracing.Trace($"Resolved account phone: {raw}");
            return CleanPhone(raw);
        }

        private static string FirstNonEmpty(params string[] values) =>
            values?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        private static string CleanPhone(string raw) =>
            Regex.Replace(raw ?? string.Empty, @"[^\d+]", "");

        private void SendSms(string phone, string body, ITracingService tracing)
        {
            tracing.Trace($"📨 Sending SMS to {phone}. Body={body}");

            string enc(string s) => Uri.EscapeDataString(s ?? string.Empty);

            var url = $"{SmsGatewayUrl}?username={enc(Username)}&token={enc(Token)}" +
                      $"&dests={enc(phone)}&body={enc(body)}" +
                      $"&priority=0&delay=0&validity=0&maxParts=0&dlr=0&prevDups=0" +
                      $"&src={enc(Sender)}";

            using (var http = new HttpClient())
            {
                var resp = http.GetAsync(url).GetAwaiter().GetResult();
                var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                tracing.Trace($"SMS API response: {(int)resp.StatusCode} {content}");

                if (!resp.IsSuccessStatusCode)
                    throw new InvalidPluginExecutionException($"SMS API failed: {resp.StatusCode} {content}");
            }
        }

        private static class SmsTemplates
        {
            public static string ForVisitorCreate() =>
                "عزيزنا المستثمر,\r\n" +
                "حرصاً منا لرفع مستوى الجودة يسعدنا تقييمكم للخدمة المقدمة عبر مركز الخدمة:";
        }
    }
}
