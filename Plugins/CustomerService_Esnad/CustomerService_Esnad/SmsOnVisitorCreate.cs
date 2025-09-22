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
        private const string BaseUrl = "https://api.oursms.com/api-a/msgs";
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

            try
            {
                tracing.Trace("=== SmsOnVisitorCreate START ===");

                if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(context.PrimaryEntityName, ENTITY_VISITOR, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidPluginExecutionException("Plugin triggered on wrong message/entity. Message="
                        + context.MessageName + " Entity=" + context.PrimaryEntityName);
                }

                if (!context.OutputParameters.Contains("id"))
                {
                    throw new InvalidPluginExecutionException("OutputParameters does not contain 'id'");
                }

                var visitorId = (Guid)context.OutputParameters["id"];
                var cols = new ColumnSet("new_contactname", "new_companyname");
                var visitor = service.Retrieve(ENTITY_VISITOR, visitorId, cols);
                if (visitor == null) throw new InvalidPluginExecutionException("Visitor record could not be retrieved");

                string phone = null;

                // 1. Contact
                var contactRef = visitor.GetAttributeValue<EntityReference>("new_contactname");
                if (contactRef != null)
                {
                    try
                    {
                        phone = ResolvePhoneForContact(service, contactRef, tracing);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidPluginExecutionException("Error resolving phone for Contact: " + ex.Message);
                    }
                }

                // 2. Account
                if (string.IsNullOrWhiteSpace(phone))
                {
                    var accountRef = visitor.GetAttributeValue<EntityReference>("new_companyname");
                    if (accountRef != null)
                    {
                        try
                        {
                            phone = ResolvePhoneForAccount(service, accountRef, tracing);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidPluginExecutionException("Error resolving phone for Account: " + ex.Message);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(phone))
                {
                    throw new InvalidPluginExecutionException("No phone number found for Visitor.");
                }

                // ✅ Build SMS Body
                var body = SmsTemplates.ForVisitorCreate();

                // 🔗 Try to resolve ticketnumber
                var ticketNumber = ResolveTicketNumber(service, visitor, tracing);
                if (!string.IsNullOrWhiteSpace(ticketNumber))
                {
                    body += $"\nhttps://feedback-dev.crm-esnad.com/?ticketNumber={ticketNumber}";
                }

                if (string.IsNullOrWhiteSpace(body))
                {
                    throw new InvalidPluginExecutionException("SMS body is empty after processing.");
                }

                try
                {
                    SendSms(phone, body, tracing);
                }
                catch (Exception ex)
                {
                    throw new InvalidPluginExecutionException("Error while sending SMS: " + ex.Message);
                }

                tracing.Trace("=== SmsOnVisitorCreate END ===");
            }
            catch (Exception ex)
            {
                tracing.Trace("Exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("SmsOnVisitorCreate1 failed at: " + ex.Message, ex);
            }
        }

        private string ResolvePhoneForContact(IOrganizationService service, EntityReference contactRef, ITracingService tracing)
        {
            if (contactRef == null) throw new InvalidPluginExecutionException("Contact reference is null");

            var c = service.Retrieve("contact", contactRef.Id, new ColumnSet("mobilephone", "telephone1", "telephone2"));
            if (c == null) throw new InvalidPluginExecutionException("Contact record could not be retrieved");

            var raw = FirstNonEmpty(
                c.GetAttributeValue<string>("mobilephone"),
                c.GetAttributeValue<string>("telephone1"),
                c.GetAttributeValue<string>("telephone2")
            );
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidPluginExecutionException("No phone found on Contact");

            return CleanPhone(raw);
        }

        private string ResolvePhoneForAccount(IOrganizationService service, EntityReference accountRef, ITracingService tracing)
        {
            if (accountRef == null) throw new InvalidPluginExecutionException("Account reference is null");

            var a = service.Retrieve("account", accountRef.Id, new ColumnSet("new_companyrepresentativephonenumber", "telephone1", "telephone2", "telephone3"));
            if (a == null) throw new InvalidPluginExecutionException("Account record could not be retrieved");

            var raw = FirstNonEmpty(
                a.GetAttributeValue<string>("new_companyrepresentativephonenumber"),
                a.GetAttributeValue<string>("telephone1"),
                a.GetAttributeValue<string>("telephone2"),
                a.GetAttributeValue<string>("telephone3")
            );
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidPluginExecutionException("No phone found on Account");

            return CleanPhone(raw);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            var v = values?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (string.IsNullOrWhiteSpace(v)) throw new InvalidPluginExecutionException("FirstNonEmpty returned null/empty");
            return v;
        }

        private static string CleanPhone(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidPluginExecutionException("Raw phone is null or empty before cleaning");
            return Regex.Replace(raw, @"[^\d+]", "");
        }

        private void SendSms(string phone, string body, ITracingService tracing)
        {
            if (string.IsNullOrWhiteSpace(phone)) throw new InvalidPluginExecutionException("SendSms: phone is null/empty");
            if (string.IsNullOrWhiteSpace(body)) throw new InvalidPluginExecutionException("SendSms: body is null/empty");

            string enc(string s) => Uri.EscapeDataString(s ?? string.Empty);

            var url = $"{BaseUrl}?username={enc(Username)}&token={enc(Token)}" +
                      $"&dests={enc(phone)}&body={enc(body)}" +
                      $"&priority=0&delay=0&validity=0&maxParts=0&dlr=0&prevDups=0" +
                      $"&src={enc(Sender)}";

            using (var http = new HttpClient())
            {
                var resp = http.GetAsync(url).GetAwaiter().GetResult();
                var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!resp.IsSuccessStatusCode)
                {
                    throw new InvalidPluginExecutionException($"SMS API failed: {resp.StatusCode} Content={content}");
                }
            }
        }

        /// <summary>
        /// Resolve one ticket number from related Case (incident).
        /// </summary>
        private string ResolveTicketNumber(IOrganizationService service, Entity visitor, ITracingService tracing)
        {
            // Try Contact first
            var contactRef = visitor.GetAttributeValue<EntityReference>("new_contactname");
            if (contactRef != null)
            {
                var query = new QueryExpression("incident")
                {
                    ColumnSet = new ColumnSet("ticketnumber"),
                    TopCount = 1
                };
                query.Criteria.AddCondition("customerid", ConditionOperator.Equal, contactRef.Id);

                var caseRecord = service.RetrieveMultiple(query).Entities.FirstOrDefault();
                if (caseRecord != null)
                {
                    var t = caseRecord.GetAttributeValue<string>("ticketnumber");
                    tracing.Trace($"Resolved ticketnumber from Contact: {t}");
                    return t;
                }
            }

            // Fallback: Account
            var accountRef = visitor.GetAttributeValue<EntityReference>("new_companyname");
            if (accountRef != null)
            {
                var query = new QueryExpression("incident")
                {
                    ColumnSet = new ColumnSet("ticketnumber"),
                    TopCount = 1
                };
                query.Criteria.AddCondition("customerid", ConditionOperator.Equal, accountRef.Id);

                var caseRecord = service.RetrieveMultiple(query).Entities.FirstOrDefault();
                if (caseRecord != null)
                {
                    var t = caseRecord.GetAttributeValue<string>("ticketnumber");
                    tracing.Trace($"Resolved ticketnumber from Account: {t}");
                    return t;
                }
            }

            tracing.Trace("No related case found for visitor.");
            return null;
        }

        private static class SmsTemplates
        {
            public static string ForVisitorCreate() =>
                "عزيزنا المستثمر,\r\n" +
                "حرصاً منا لرفع مستوى الجودة يسعدنا تقييمكم للخدمة المقدمة عبر مركز الخدمة:";
        }
    }
}
