using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Taadeen.Cm.Plugins
{
    public class SmsOnCaseClosureState : IPlugin
    {
        private const string BaseUrl = "https://api.oursms.com/api-a/msgs";
        private const string Username = "Taadeen2.0";
        private const string Token = "7sgOnsFhAuYdNgg5a3R4";
        private const string Sender = "Taadeen";

        // Replace this with your actual Ticket Closure status reason value
        private const int STATUS_CLOSURE = 5;

        public SmsOnCaseClosureState(string unsecure, string secure) { }

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            try
            {
                if (context.Depth > 1) return;
                if (!context.InputParameters.Contains("Target")) return;

                var target = (Entity)context.InputParameters["Target"];
                if (target.LogicalName != "incident") return;

                if (!target.Attributes.Contains("statuscode")) return;

                var newStatus = ((OptionSetValue)target["statuscode"]).Value;

                if (newStatus != STATUS_CLOSURE)
                    return;

                var incident = service.Retrieve("incident", context.PrimaryEntityId,
                    new ColumnSet("ticketnumber", "customerid"));

                var ticket = incident.GetAttributeValue<string>("ticketnumber");
                var phone = ResolvePhone(incident, service);

                if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(phone))
                    return;

                // DEBUG: confirm plugin runs
                var debug = new Entity("incident", incident.Id)
                {
                    ["description"] = $"SMS plugin executed on {DateTime.Now}"
                };
                service.Update(debug);

                var body = $"عزيزنا المستثمر، تم اغلاق التذكرة رقم {ticket}. نأمل تقييم تجربتكم عبر الرابط: https://feedback.crm-esnad.com?ticketNumber={ticket}";
                SendSms(phone, body);
            }
            catch (Exception ex)
            {
                // write simple trace to description for visibility
                var err = new Entity("incident", ((IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext))).PrimaryEntityId)
                {
                    ["description"] = "SMS plugin error: " + ex.Message
                };
                try
                {
                    ((IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory)))
                        .CreateOrganizationService(null).Update(err);
                }
                catch { }
                throw;
            }
        }

        private static string ResolvePhone(Entity incident, IOrganizationService service)
        {
            var cust = incident.GetAttributeValue<EntityReference>("customerid");
            if (cust == null) return null;

            string raw = null;
            if (cust.LogicalName == "contact")
            {
                var c = service.Retrieve("contact", cust.Id,
                    new ColumnSet("mobilephone", "telephone1"));
                raw = c.GetAttributeValue<string>("mobilephone") ?? c.GetAttributeValue<string>("telephone1");
            }
            else if (cust.LogicalName == "account")
            {
                var a = service.Retrieve("account", cust.Id,
                    new ColumnSet("telephone1", "new_companyrepresentativephonenumber"));
                raw = a.GetAttributeValue<string>("new_companyrepresentativephonenumber") ?? a.GetAttributeValue<string>("telephone1");
            }

            return string.IsNullOrWhiteSpace(raw) ? null : Regex.Replace(raw, @"[^\d+]", "");
        }

        private static void SendSms(string phone, string body)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            string enc(string s) => Uri.EscapeDataString(s ?? "");
            var url = $"{BaseUrl}?username={enc(Username)}&token={enc(Token)}" +
                      $"&dests={enc(phone)}&body={enc(body)}" +
                      $"&priority=0&delay=0&validity=0&maxParts=0&dlr=0&prevDups=0&src={enc(Sender)}";

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(15);
                var resp = http.GetAsync(url).GetAwaiter().GetResult();
                var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!resp.IsSuccessStatusCode)
                    throw new InvalidPluginExecutionException($"SMS failed: {resp.StatusCode} | {content}");
            }
        }
    }
}
