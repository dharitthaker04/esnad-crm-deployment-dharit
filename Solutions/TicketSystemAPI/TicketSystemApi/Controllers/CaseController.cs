using System;
using System.Linq;
using System.Net;
using System.Web.Http;
using System.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TicketSystemApi.Models;
using TicketSystemApi.Services;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace TicketSystemApi.Controllers
{
    [RoutePrefix("cases")]
    public class CaseController : ApiController
    {
        private readonly ICrmService _crmService;

        public CaseController()
        {
            _crmService = new CrmService();
        }

        [HttpPost]
        [Route("create")]
        public IHttpActionResult CreateCase([FromBody] CaseRequestModel model)
        {
            // ✅ Step 0: Validate Bearer Token
            var authHeader = Request.Headers.Authorization;
            string expectedToken = ConfigurationManager.AppSettings["AzureBearerToken"];

            if (authHeader == null || authHeader.Scheme != "Bearer" || authHeader.Parameter != expectedToken)
            {
                return Content(HttpStatusCode.Unauthorized,
                    ApiResponse<object>.Error("Unauthorized - Invalid bearer token"));
            }

            // ✅ Step 1: Validate input
            if (model == null || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Incident))
            {
                return Ok(ApiResponse<object>.Error("Missing required fields"));
            }

            try
            {
                var service = _crmService.GetService();

                // ✅ Step 2: Check for existing contact by email
                var contactQuery = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet("contactid", "firstname", "lastname", "telephone1"),
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression("emailaddress1", ConditionOperator.Equal, model.Email)
                        }
                    }
                };

                var result = service.RetrieveMultiple(contactQuery);
                var contact = result.Entities.FirstOrDefault();
                Guid contactId;

                // ✅ Step 3: Create or update contact
                if (contact == null)
                {
                    var newContact = new Entity("contact");
                    newContact["firstname"] = model.FirstName;
                    newContact["lastname"] = model.LastName;
                    newContact["emailaddress1"] = model.Email;

                    if (!string.IsNullOrWhiteSpace(model.PrimaryContactPhone))
                        newContact["telephone1"] = model.PrimaryContactPhone;

                    contactId = service.Create(newContact);
                }
                else
                {
                    contact["firstname"] = model.FirstName;
                    contact["lastname"] = model.LastName;

                    if (!string.IsNullOrWhiteSpace(model.PrimaryContactPhone))
                        contact["telephone1"] = model.PrimaryContactPhone;

                    service.Update(contact);
                    contactId = contact.Id;
                }

                // ✅ Step 4: Create Case (incident)
                var caseEntity = new Entity("incident");
                caseEntity["title"] = "Case created via Chatbot";
                caseEntity["description"] = model.Incident;
                caseEntity["customerid"] = new EntityReference("contact", contactId);
                caseEntity["new_ticketsubmissionchannel"] = new OptionSetValue(6); // example value for chatbot

                int? beneficiaryTypeValue = MapBeneficiaryType(model.BeneficiaryType);
                if (beneficiaryTypeValue.HasValue)
                {
                    caseEntity["new_beneficiarytype"] = new OptionSetValue(beneficiaryTypeValue.Value);
                }

                var caseId = service.Create(caseEntity);

                // ✅ Step 5: Retrieve ticket number, beneficiary type, and contact phone
                var createdCase = service.Retrieve("incident", caseId, new ColumnSet("ticketnumber", "new_beneficiarytype", "customerid"));

                string ticketNumber = createdCase.GetAttributeValue<string>("ticketnumber");
                OptionSetValue beneficiaryTypeOption = createdCase.GetAttributeValue<OptionSetValue>("new_beneficiarytype");
                EntityReference customerRef = createdCase.GetAttributeValue<EntityReference>("customerid");

                string beneficiaryTypeLabel = beneficiaryTypeOption != null
                    ? GetOptionSetLabel(service, "incident", "new_beneficiarytype", beneficiaryTypeOption.Value)
                    : null;

                string phoneNumber = null;

                if (customerRef != null)
                {
                    if (customerRef.LogicalName == "contact")
                    {
                        var contactFromCase = service.Retrieve("contact", customerRef.Id, new ColumnSet("telephone1"));
                        phoneNumber = contactFromCase.GetAttributeValue<string>("telephone1");
                        Console.WriteLine($"📞 Contact telephone1: {phoneNumber}");
                    }
                    else if (customerRef.LogicalName == "account")
                    {
                        var accountFromCase = service.Retrieve("account", customerRef.Id, new ColumnSet(true)); // ✅ Retrieve all fields

                        Console.WriteLine("🔎 Account fields:");
                        foreach (var kv in accountFromCase.Attributes)
                        {
                            Console.WriteLine($"{kv.Key}: {kv.Value}");
                        }

                        // Replace below with actual field name once confirmed
                        phoneNumber = accountFromCase.GetAttributeValue<string>("new_companyrepresentativephone")
                                     ?? accountFromCase.GetAttributeValue<string>("telephone1");

                        Console.WriteLine($"📞 Account phone: {phoneNumber}");
                    }
                }

                return Ok(ApiResponse<object>.Success(new
                {
                    CaseId = caseId,
                    TicketNumber = ticketNumber,
                    BeneficiaryType = beneficiaryTypeLabel,
                    Phone = phoneNumber
                }, "Case created successfully"));
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.InternalServerError,
                    ApiResponse<object>.Error($"CRM error: {ex.Message}"));
            }
        }

        // ✅ Helper to resolve OptionSet label from value
        private string GetOptionSetLabel(IOrganizationService service, string entityLogicalName, string attributeLogicalName, int optionSetValue)
        {
            var req = new RetrieveAttributeRequest
            {
                EntityLogicalName = entityLogicalName,
                LogicalName = attributeLogicalName,
                RetrieveAsIfPublished = true
            };

            var response = (RetrieveAttributeResponse)service.Execute(req);
            var metadata = (EnumAttributeMetadata)response.AttributeMetadata;

            var option = metadata.OptionSet.Options.FirstOrDefault(o => o.Value == optionSetValue);
            return option?.Label?.UserLocalizedLabel?.Label;
        }

        // ✅ Helper to map label to OptionSet value
        private int? MapBeneficiaryType(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return null;

            switch (label.Trim().ToLower())
            {
                case "individual":
                    return 1; // ✅ Replace with actual value
                case "company":
                    return 2; // ✅ Replace with actual value
                default:
                    return null;
            }
        }
    }
}
