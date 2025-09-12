using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomerService_Esnad
{
    public class SLALevel4 : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            // Get the context
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            ITracingService tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IOrganizationServiceFactory factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("SLALevel4 Escalation Plugin execution started.");

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
                Entity caseEntity = service.Retrieve("incident", caseId, new ColumnSet("ownerid", "title", "ticketnumber"));
                if (!caseEntity.Contains("ownerid"))
                {
                    tracing.Trace("Case does not have an owner. Exiting.");
                    return;
                }

                string caseTitle = caseEntity.GetAttributeValue<string>("title") ?? "(No Title)";
                EntityReference ownerRef = caseEntity.GetAttributeValue<EntityReference>("ownerid");
                string TicketNumber = caseEntity.GetAttributeValue<string>("ticketnumber") ?? "(No Title)";
                tracing.Trace($"Case Owner: {ownerRef.Name}, Type: {ownerRef.LogicalName}");

                // Fetch CRM Admin user (sender of the email)
                Entity crmAdminUser = GetCRMAdminUser(service);
                if (crmAdminUser == null)
                    throw new InvalidPluginExecutionException("CRM Admin user not found or missing email.");

                var fromParty = new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference("systemuser", crmAdminUser.Id)
                };

                string orgURL = GetOrgURL(service);
                string caseUrl = $"{orgURL}{caseId}";

                // Determine if the case owner is a team or a user
                if (ownerRef.LogicalName == "team")
                {
                    tracing.Trace("Owner is a Team. Sending email to the team members.");
                    SendEmailToTeam(service, crmAdminUser, fromParty, caseId, caseTitle, ownerRef, ownerRef.Id, caseUrl, TicketNumber, tracing);
                }
                else if (ownerRef.LogicalName == "systemuser")
                {
                    tracing.Trace("Owner is a User. Fetching user's teams...");
                    var teams = GetUserTeams(service, ownerRef.Id, tracing);
                    tracing.Trace($"Found {teams.Count} teams for user.");

                    foreach (var team in teams)
                    {
                        tracing.Trace($"Processing team: {team.GetAttributeValue<string>("name")}");
                        SendEmailToTeam(service, crmAdminUser, fromParty, caseId, caseTitle, ownerRef, team.Id, caseUrl,  TicketNumber, tracing);
                    }
                }

                tracing.Trace("SLALevel3 Escalation Plugin execution completed.");
            }
            catch (Exception ex)
            {
                tracing.Trace("Error: " + ex.ToString());
                throw new InvalidPluginExecutionException("Failed in SLALevel3 Escalation plugin.", ex);
            }
        }

        private void SendEmailToTeam(IOrganizationService service, Entity crmAdminUser, Entity fromParty, Guid caseId, string caseTitle, EntityReference ownerRef, Guid teamId, string caseUrl, string TicketNumber, ITracingService tracing)
        {
            // Fetch the Department Manager, Sector Head, and CEO for the team
            var departmentManagers = GetDepartmentManagerInTeam(service, teamId, tracing);
            var sectorHeads = GetSectorHeadInTeam(service, teamId, tracing);
            var ceo = GetCEO(service, tracing);

            var toParties = new List<Entity>();

            // Add Department Managers to email recipients
            foreach (var manager in departmentManagers)
            {
                toParties.Add(new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference("systemuser", manager.Id)
                });
            }

            // Add Sector Heads to email recipients
            foreach (var head in sectorHeads)
            {
                toParties.Add(new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference("systemuser", head.Id)
                });
            }

            // Add CEO to email recipients
            if (ceo != null)
            {
                toParties.Add(new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference("systemuser", ceo.Id)
                });
            }

            // Create the email subject and body
            string subject = $"[SLA Escalation Level 4] Case Breach Alert - {caseTitle}";
            string imageUrl = "https://feedback-dev.crm-esnad.com/Esnad-Logo.jpg";

            var email = new Entity("email")
            {
                ["subject"] = subject,
                ["description"] = $@"
        <html>
         <body>
<p>مع التحية والتقدير،</p>
                    <p>نود إعلامكم بأن التذكرة التالية قد تجاوزت المدة المحددة في اتفاقية مستوى الخدمة (SLA):</p>
                   
                    <p><a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a></p>
                    <p>يرجى اتخاذ الإجراءات اللازمة حسب آلية التصعيد المعتمدة لضمان سرعة المعالجة.</p>
                    <p>شكرًا لتعاونكم،</p>
                    <p>مركز دعم المستثمرين لقطاع التعدين</p>
             <p>With Regards and Appreciation</p>
             <p>We would like to inform you that the following ticket has exceeded the time frame specified in the Service Level Agreement (SLA):</p>
             <p><a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a></p>
            
             <pPlease take the necessary actions according to the approved escalation procedure to ensure prompt handling.</p>
             <p><strong>Assigned Agent:</strong> {ownerRef.Name}</p>
             <br/>
             <p>Thank you for your cooperation,</p>
             <p>Investor Support Center – Mining Sector</p>
             <p><img src='{imageUrl}' alt='CRM Logo' style='width:200px; margin-bottom:10px;' /></p>
         </body>
        </html>",
                ["directioncode"] = true,
                ["from"] = new EntityCollection(new[] { fromParty }),
                ["to"] = new EntityCollection(toParties),
                ["regardingobjectid"] = new EntityReference("incident", caseId),
                ["statuscode"] = new OptionSetValue(1) // Draft
            };

            Guid emailId = service.Create(email);
            tracing.Trace($"Email created for case ID {caseId}. ID: {emailId}");

            var sendRequest = new OrganizationRequest("SendEmail");
            sendRequest["EmailId"] = emailId;
            sendRequest["IssueSend"] = true;
            sendRequest["TrackingToken"] = "";

            service.Execute(sendRequest);
            tracing.Trace($"Email sent successfully for case ID {caseId}.");
        }

        private List<Entity> GetUserTeams(IOrganizationService service, Guid userId, ITracingService tracing)
        {
            var fetchXml = $@"
            <fetch>
              <entity name='team'>
                <attribute name='name'/>
                <attribute name='teamid'/>
                <link-entity name='teammembership' from='teamid' to='teamid' intersect='true'>
                  <filter>
                    <condition attribute='systemuserid' operator='eq' value='{userId}'/>
                  </filter>
                </link-entity>
              </entity>
            </fetch>";

            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"Found {result.Entities.Count} teams for user {userId}.");
            return result.Entities.ToList();
        }

        private List<Entity> GetSectorHeadInTeam(IOrganizationService service, Guid teamId, ITracingService tracing)
        {
            var fetchXml = $@"
<fetch>
  <entity name='systemuser'>
    <attribute name='systemuserid'/>
    <attribute name='internalemailaddress'/>
    <filter>
      <condition attribute='accessmode' operator='eq' value='0' />
    </filter>
    <link-entity name='teammembership' from='systemuserid' to='systemuserid' link-type='inner'>
      <filter>
        <condition attribute='teamid' operator='eq' value='{teamId}' />
      </filter>
    </link-entity>
    <link-entity name='systemuserroles' from='systemuserid' to='systemuserid' link-type='inner'>
      <link-entity name='role' from='roleid' to='roleid' link-type='inner'>
        <filter>
          <condition attribute='name' operator='eq' value='Esnad: Sector Head' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";

            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"Found {result.Entities.Count} Sector Head in team {teamId}.");
            return result.Entities.ToList();
        }

        private List<Entity> GetDepartmentManagerInTeam(IOrganizationService service, Guid teamId, ITracingService tracing)
        {
            var fetchXml = $@"
    <fetch>
      <entity name='systemuser'>
        <attribute name='systemuserid'/>
        <attribute name='internalemailaddress'/>
        <filter>
          <condition attribute='accessmode' operator='eq' value='0' />
        </filter>
        <link-entity name='teammembership' from='systemuserid' to='systemuserid' link-type='inner'>
          <filter>
            <condition attribute='teamid' operator='eq' value='{teamId}' />
          </filter>
        </link-entity>
        <link-entity name='systemuserroles' from='systemuserid' to='systemuserid' link-type='inner'>
          <link-entity name='role' from='roleid' to='roleid' link-type='inner'>
            <filter>
              <condition attribute='name' operator='eq' value='Esnad: Department Manager' />
            </filter>
          </link-entity>
        </link-entity>
      </entity>
    </fetch>";

            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"Found {result.Entities.Count} Department Managers in team {teamId}.");
            return result.Entities.ToList();
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

            return service.RetrieveMultiple(query).Entities.FirstOrDefault();
        }

        private Entity GetCEO(IOrganizationService service, ITracingService tracing)
        {
            var fetchXml = $@"
            <fetch>
              <entity name='systemuser'>
                <attribute name='systemuserid'/>
                <attribute name='internalemailaddress'/>
                <link-entity name='systemuserroles' from='systemuserid' to='systemuserid' link-type='inner'>
                  <link-entity name='role' from='roleid' to='roleid' link-type='inner'>
                    <filter>
                      <condition attribute='name' operator='eq' value='Esnad: CEO' />
                    </filter>
                  </link-entity>
                </link-entity>
              </entity>
            </fetch>";


            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"Found {result.Entities.Count} CEO.");
            return result.Entities.FirstOrDefault();
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
