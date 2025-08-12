using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomerService_Esnad
{
    public class TicketCloseEmailToCustomer : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            // Get the context
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            ITracingService tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IOrganizationServiceFactory factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("SLALevel1Escalation Plugin execution started.");

            try
            {
                // Get Case ID from InputParameters
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference caseRef))
                {
                    tracing.Trace("CaseId not found in input parameters.");
                    return;
                }

                Guid caseId = caseRef.Id;
                tracing.Trace($"Processing Case ID: {caseId}");

                // Retrieve Case details
                Entity caseEntity = service.Retrieve("incident", caseId, new ColumnSet("ownerid", "title", "customerid"));
                if (!caseEntity.Contains("ownerid") || !caseEntity.Contains("customerid"))
                {
                    tracing.Trace("Case does not have an owner or customer. Exiting.");
                    return;
                }

                string caseTitle = caseEntity.GetAttributeValue<string>("title") ?? "(No Title)";
                EntityReference customerRef = caseEntity.GetAttributeValue<EntityReference>("customerid");
                tracing.Trace($"Processing Case: {caseTitle}, Customer: {customerRef.Name}");

                // Fetch crmadmin as sender
                Entity crmAdminUser = GetCRMAdminUser(service);
                if (crmAdminUser == null)
                    throw new InvalidPluginExecutionException("CRM Admin user not found or missing email.");

                var fromParty = new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference("systemuser", crmAdminUser.Id)
                };

                string orgURL = GetOrgURL(service);
                string caseUrl = $"{orgURL}/main.aspx?appid=yourappguid&pagetype=entityrecord&id={caseId}";

                // Send email to the customer (either contact or account)
                SendEmailToCustomer(service, fromParty, caseId, customerRef, caseTitle, caseUrl, tracing);

                tracing.Trace("SLALevel1Escalation Plugin execution completed.");
            }
            catch (Exception ex)
            {
                tracing.Trace("Error: " + ex.ToString());
                throw new InvalidPluginExecutionException("Failed in SLALevel1Escalation plugin.", ex);
            }
        }

        private void SendEmailToCustomer(IOrganizationService service, Entity fromParty, Guid caseId, EntityReference customerRef, string caseTitle, string caseUrl, ITracingService tracing)
        {
            // Fetch email address dynamically based on whether it's Contact or Account
            string customerEmail = GetCustomerEmail(service, customerRef);
            if (string.IsNullOrEmpty(customerEmail))
            {
                tracing.Trace("No customer email found.");
                return;
            }

            tracing.Trace($"Creating email for customer: {customerEmail}");

            string ticketNumber = GetTicketNumberFromGuid(service, caseId);
            if (string.IsNullOrEmpty(ticketNumber))
            {
                tracing.Trace($"Ticket number not found for ticket ID {caseId}");
                return;
            }

            // Prepare the email subject and body
            string subject = $"Ticket {ticketNumber} Closure Notification";
            string emailBody = $@"
            <html>
                <body>
                    <p>عزيزنا المستثمر،</p>
                    <p>تتم اغلاق التذكرة رقم  {ticketNumber};</p>
                    <p>في حال وجود أي استفسار يرجى التواصل معنا عبر الرقم الموحد 8001281111</p>
                    <p><a href='https://feedback.crm-esnad.com/?ticketNumber={ticketNumber}'>Click Here to Share Your Experience</a></p>
                    <p>مركز دعم المستثمرين.</p>
                    <br/>
                    <p>Dear Investor,</p>
                    <p>Ticket number {ticketNumber} is being closed.</p>
                    <p>If you have any inquiries, please contact us through the unified number 8001281111.</p>
                    <p><a href='https://feedback.crm-esnad.com/?ticketNumber={ticketNumber}'>Click Here to Share Your Experience</a></p>
                    <p>Investor Support Center</p>
                </body>
            </html>";

            var toParties = new List<Entity>
            {
                new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference(customerRef.LogicalName, customerRef.Id) // Send to the customer (either contact or account)
                }
            };

            // Create email entity
            var email = new Entity("email")
            {
                ["subject"] = subject,
                ["description"] = emailBody,
                ["directioncode"] = true, // Email sent to customer (outbound)
                ["from"] = new EntityCollection(new[] { fromParty }),
                ["to"] = new EntityCollection(toParties),
                ["regardingobjectid"] = new EntityReference("incident", caseId),
                ["statuscode"] = new OptionSetValue(1) // Sent status
            };

            // Create email in CRM
            Guid emailId = service.Create(email);

            // Send email request
            var sendRequest = new OrganizationRequest("SendEmail")
            {
                ["EmailId"] = emailId,
                ["IssueSend"] = true,
                ["TrackingToken"] = "" // Empty tracking token
            };

            // Execute the request to send the email
            service.Execute(sendRequest);
        }

        private string GetCustomerEmail(IOrganizationService service, EntityReference customerRef)
        {
            string email = string.Empty;

            // Check if customer is Contact or Account
            if (customerRef.LogicalName == "contact")
            {
                // Fetch email for Contact
                Entity contact = service.Retrieve("contact", customerRef.Id, new Microsoft.Xrm.Sdk.Query.ColumnSet("emailaddress1"));
                email = contact.Contains("emailaddress1") ? contact["emailaddress1"].ToString() : string.Empty;
            }
            else if (customerRef.LogicalName == "account")
            {
                // Fetch email for Account
                Entity account = service.Retrieve("account", customerRef.Id, new Microsoft.Xrm.Sdk.Query.ColumnSet("emailaddress1"));
                email = account.Contains("emailaddress1") ? account["emailaddress1"].ToString() : string.Empty;
            }

            return email;
        }

        private string GetTicketNumberFromGuid(IOrganizationService service, Guid ticketGuid)
        {
            // Retrieve the ticket (incident) based on its Guid
            Entity ticket = service.Retrieve("incident", ticketGuid, new Microsoft.Xrm.Sdk.Query.ColumnSet("ticketnumber"));
            // Return the ticket number
            return ticket.Contains("ticketnumber") ? ticket["ticketnumber"].ToString() : string.Empty;
        }

        private Entity GetCRMAdminUser(IOrganizationService service)
        {
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid", "internalemailaddress"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("domainname", ConditionOperator.Equal, "CRM-ESNAD\\crmadmin"),
                        new ConditionExpression("accessmode", ConditionOperator.Equal, 0)
                    }
                }
            };

            var user = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            return user;
        }

        private string GetOrgURL(IOrganizationService service)
        {
            var query = new QueryExpression("new_environmentvariable")
            {
                ColumnSet = new ColumnSet("new_value"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("new_name", ConditionOperator.Equal, "OrgURL")
                    }
                }
            };

            EntityCollection result = service.RetrieveMultiple(query);
            if (result.Entities.Count > 0)
                return result.Entities[0].GetAttributeValue<string>("new_value");

            throw new InvalidPluginExecutionException("OrgURL environment variable not found.");
        }
    }
}
