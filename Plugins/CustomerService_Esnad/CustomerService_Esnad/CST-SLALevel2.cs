using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomerService_Esnad
    {
        public class CSTSLALevel2 : IPlugin
        {
            public void Execute(IServiceProvider serviceProvider)
            {
                IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
                ITracingService tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
                IOrganizationServiceFactory factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                IOrganizationService service = factory.CreateOrganizationService(context.UserId);

                tracing.Trace("SLALevel2 Plugin execution started.");

                try
                {
                    // The Action must pass "Target" as an EntityReference (incident)
                    if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference caseRef))
                    {
                        tracing.Trace("Target not found or not an EntityReference.");
                        return;
                    }

                    Guid caseId = caseRef.Id;
                    tracing.Trace($"Case ID received from Action: {caseId}");

                    // 🔹 Retrieve incident explicitly
                    Entity caseEntity = service.Retrieve(
                        "incident",
                        caseId,
                        new ColumnSet("ticketnumber", "title", "ownerid")
                    );

                    string caseTitle = caseEntity.GetAttributeValue<string>("title") ?? "(No Title)";
                    EntityReference ownerRef = caseEntity.GetAttributeValue<EntityReference>("ownerid");

                    // 🔹 Safe retrieval of ticketnumber
                    string ticketNumber = caseEntity.GetAttributeValue<string>("ticketnumber");
                    if (string.IsNullOrEmpty(ticketNumber) && caseEntity.FormattedValues.Contains("ticketnumber"))
                    {
                        ticketNumber = caseEntity.FormattedValues["ticketnumber"];
                    }
                    ticketNumber = ticketNumber ?? "(No number)";

                    tracing.Trace($"Case Title: {caseTitle}");
                    tracing.Trace($"Case Owner: {ownerRef?.Name}, Type: {ownerRef?.LogicalName}");
                    tracing.Trace($"Ticket Number: {ticketNumber}");

                    // Fetch crmadmin as sender
                    Entity crmAdminUser = GetCRMAdminUser(service);
                    if (crmAdminUser == null)
                        throw new InvalidPluginExecutionException("CRM Admin user not found or missing email.");

                    var fromParty = new Entity("activityparty")
                    {
                        ["partyid"] = new EntityReference("systemuser", crmAdminUser.Id)
                    };

                    string orgURL = GetOrgURL(service);
                    string caseUrl = $"{orgURL}{caseId}";

                    // If owner is Team → send to Department Managers in that team
                    if (ownerRef.LogicalName == "team")
                    {
                        tracing.Trace("Owner is a Team. Sending email to Department Manager(s).");
                        SendEmailToTeam(service, crmAdminUser, fromParty, caseId, caseTitle, ownerRef, ownerRef.Id, caseUrl, ticketNumber, tracing, ownerRef.Name);
                    }
                    else if (ownerRef.LogicalName == "systemuser")
                    {
                        tracing.Trace("Owner is a User. Fetching user's teams...");
                        var teams = GetUserTeams(service, ownerRef.Id, tracing);
                        tracing.Trace($"Found {teams.Count} teams for user.");

                        foreach (var team in teams)
                        {
                            string teamName = team.GetAttributeValue<string>("name");
                            tracing.Trace($"Processing team: {teamName}");
                            SendEmailToTeam(service, crmAdminUser, fromParty, caseId, caseTitle, ownerRef, team.Id, caseUrl, ticketNumber, tracing, teamName);
                        }
                    }

                    tracing.Trace("SLALevel2 Plugin execution completed.");
                }
                catch (Exception ex)
                {
                    tracing.Trace("❌ Error: " + ex.ToString());
                    throw new InvalidPluginExecutionException("Failed in SLALevel2 plugin.", ex);
                }
            }

            private void SendEmailToTeam(
                IOrganizationService service,
                Entity crmAdminUser,
                Entity fromParty,
                Guid caseId,
                string caseTitle,
                EntityReference ownerRef,
                Guid teamId,
                string caseUrl,
                string ticketNumber,
                ITracingService tracing,
                string teamName)
            {
                var users = GetDepartmentManagerInTeam(service, teamId, tracing);
                if (users.Count == 0)
                {
                    tracing.Trace($"No Department Manager found in team {teamId}");
                    return;
                }

                var toParties = users.Select(u => new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference("systemuser", u.Id)
                }).ToList();

                tracing.Trace($"Creating email for team: {teamName}");

            string subject = $"[SLA Escalation Level 2- Customer Service Team-Department Manager] - Case Breach Alert";
                string imageUrl = "https://feedback-dev.crm-esnad.com/Esnad-Logo.jpg";

                var email = new Entity("email")
                {
                    ["subject"] = subject,
                    ["description"] = $@"
                <html>
                <body>
                    <p>مع التحية والتقدير،</p>
                    <p>نود إعلامكم بأن التذكرة التالية قد تجاوزت المدة المحددة في اتفاقية مستوى الخدمة (SLA):</p>
                    <p>عنوان التذكرة:{teamName}</p>
                    <p><a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a></p>
                    <p>يرجى اتخاذ الإجراءات اللازمة حسب آلية التصعيد المعتمدة لضمان سرعة المعالجة.</p>
                    <p>شكرًا لتعاونكم،</p>
                    <p>مركز دعم المستثمرين لقطاع التعدين</p>
                    <p>With Regards and Appreciation</p>
                    <p>We would like to inform you that the following ticket has exceeded the time frame specified in the Service Level Agreement (SLA):</p>
                    <p> Responsible Team:  {teamName},<br/><br/></p>
                    <p><a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a></p>
                   
                    <pPlease take the necessary actions according to the approved escalation procedure to ensure prompt handling.</p>
                    
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
                tracing.Trace($"Email created for team {teamName}. ID: {emailId}");

                var sendRequest = new OrganizationRequest("SendEmail");
                sendRequest["EmailId"] = emailId;
                sendRequest["IssueSend"] = true;
                sendRequest["TrackingToken"] = "";
                service.Execute(sendRequest);

                tracing.Trace($"✅ Email sent to team {teamName} successfully.");
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
