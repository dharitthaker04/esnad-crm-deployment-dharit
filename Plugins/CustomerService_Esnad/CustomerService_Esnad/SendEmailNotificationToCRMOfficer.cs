using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System;
using System.Linq;

namespace CustomerService_Esnad
{
    public class SendEmailNotificationToCRMOfficer : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            tracing.Trace("🔔 Plugin execution started.");

            try
            {
                // ✅ Validate input parameters
                if (!context.InputParameters.Contains("CaseId") || !(context.InputParameters["CaseId"] is EntityReference caseRef))
                    throw new InvalidPluginExecutionException("Missing or invalid 'CaseId' input parameter.");

                if (!context.InputParameters.Contains("TeamId") || !(context.InputParameters["TeamId"] is EntityReference teamRef))
                    throw new InvalidPluginExecutionException("Missing or invalid 'TeamId' input parameter.");

                var caseId = caseRef.Id;
                var teamId = teamRef.Id;

                // ✅ Get case title
                var caseEntity = service.Retrieve("incident", caseId, new ColumnSet("title"));
                string caseTitle = caseEntity.GetAttributeValue<string>("title") ?? "Unknown";

                // ✅ Fetch only users in the team with Position = CRM Officer
                string fetchXml = $@"
<fetch>
   <entity name='systemuser'>
     <attribute name='systemuserid'/>
     <attribute name='internalemailaddress'/>
     <filter>
       <condition attribute='accessmode' operator='eq' value='0' /> <!-- Active user -->
     </filter>
     <link-entity name='teammembership' from='systemuserid' to='systemuserid' link-type='inner'>
       <filter>
         <condition attribute='teamid' operator='eq' value='{teamId}' />
       </filter>
     </link-entity>
     <link-entity name='position' from='positionid' to='positionid' link-type='inner'>
       <filter>
         <condition attribute='name' operator='eq' value='CRM Officer' />
       </filter>
     </link-entity>
   </entity>
</fetch>";

                var users = service.RetrieveMultiple(new FetchExpression(fetchXml)).Entities;

                if (!users.Any())
                {
                    tracing.Trace("❌ No users found in team with position CRM Officer.");
                    return;
                }

                // ✅ Build 'To' recipients
                var toParties = users.Select(u => new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference("systemuser", u.Id)
                }).ToList();

                // ✅ Get CRM Admin user (Sender)
                var crmAdmin = service.RetrieveMultiple(new QueryExpression("systemuser")
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
                }).Entities.FirstOrDefault();

                if (crmAdmin == null || !crmAdmin.Contains("internalemailaddress"))
                    throw new InvalidPluginExecutionException("CRM Admin user not found or missing email.");

                var fromParty = new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference("systemuser", crmAdmin.Id)
                };

                // ✅ Build email
                string orgUrl = GetOrgURL(service, tracing);
                string caseUrl = $"{orgUrl}{caseId}";
                string imageUrl = "https://d365.crm-esnad.com/WebResources/esnad_logo.png"; // Update with actual logo URL

                string caseTitleHtml = $"<a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a>";

                string emailBody = $@"
<html>
  <body>
    
    <p>Ticket No. {caseTitleHtml} has been processed by the relevant department.</p>
    <p>Kindly review the resolution and close the ticket in accordance with the approved Service Level Agreement (SLA).</p>
    <p><img src='{imageUrl}' alt='CRM Logo' style='max-width: 200px;' /></p>
  </body>
</html>";

                var email = new Entity("email")
                {
                    ["subject"] = $"Ticket Assigned to Customer service Team: {caseTitle}",
                    ["description"] = emailBody,
                    ["directioncode"] = true,
                    ["from"] = new EntityCollection(new[] { fromParty }),
                    ["to"] = new EntityCollection(toParties),
                    ["regardingobjectid"] = new EntityReference("incident", caseId),
                    ["statuscode"] = new OptionSetValue(1) // Draft
                };

                Guid emailId = service.Create(email);
                tracing.Trace($"✅ Email created. ID: {emailId}");

                // ✅ Send Email
                var sendRequest = new SendEmailRequest
                {
                    EmailId = emailId,
                    IssueSend = true,
                    TrackingToken = ""
                };

                service.Execute(sendRequest);
                tracing.Trace("✅ Email sent successfully via SendEmailRequest.");

                // ✅ Update case with copy of GUID
                var updateCase = new Entity("incident", caseId)
                {
                    ["new_copycaseguid"] = caseId.ToString()
                };
                service.Update(updateCase);
                tracing.Trace("✅ Case updated with new_copycaseguid.");
            }
            catch (Exception ex)
            {
                tracing.Trace("❌ Exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in SendEmailNotificationToCRMOfficer Plugin.", ex);
            }

            tracing.Trace("🏁 Plugin execution completed.");
        }

        private string GetOrgURL(IOrganizationService service, ITracingService tracing)
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

            var result = service.RetrieveMultiple(query);
            if (result.Entities.Count > 0)
            {
                return result.Entities[0].GetAttributeValue<string>("new_value");
            }

            tracing.Trace("❌ OrgURL environment variable not found.");
            throw new InvalidPluginExecutionException("OrgURL environment variable missing.");
        }
    }
}
