using System.Linq;
using System.Web.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TicketSystemApi.Services;
using TicketSystemApi.Models;
using System.Configuration;
using System;
using System.Net;


namespace TicketSystemApi.Controllers
{
    [RoutePrefix("ticket")]
    public class ContactController : ApiController
    {
        private readonly ICrmService _crmService;

        public ContactController()
        {
            _crmService = new CrmService();
        }

        [HttpGet]
        [Route("customer")]
        public IHttpActionResult GetContactUrlByPhone([FromUri] string phone)
        {
            try
            {
                var service = _crmService.GetService();

                var query = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet("contactid"),
                    Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("mobilephone", ConditionOperator.Equal, phone)
                }
            }
                };

                var contacts = service.RetrieveMultiple(query);
                var contact = contacts.Entities.FirstOrDefault();

                string baseUrl = ConfigurationManager.AppSettings["CrmBaseUrl"];
                string crmUrl;

                if (contact != null)
                {
                    Guid contactId = contact.Id;
                    crmUrl = $"{baseUrl}/main.aspx?pagetype=entityrecord&etn=contact&id={contactId}";
                }
                else
                {
                    crmUrl = $"{baseUrl}/main.aspx?pagetype=entityrecord&etn=contact";
                }

                // Return plain text result instead of JSON
                return new PlainTextResult(crmUrl, Request);
            }
            catch (Exception ex)
            {
                return new PlainTextResult($"CRM error: {ex.Message}", Request);
            }
        }



        [HttpPost]
        [Route("CreateNote")]
        public IHttpActionResult CreateNote([FromBody] InteractionNoteModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.PhoneNumber))
                return Ok(ApiResponse<object>.Error("Invalid request"));

            try
            {
                var service = _crmService.GetService();

                // Find the Contact by phone number
                var contactQuery = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet("contactid"),
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression("mobilephone", ConditionOperator.Equal, model.PhoneNumber)
                        }
                    }
                };

                var contactResult = service.RetrieveMultiple(contactQuery);
                var contact = contactResult.Entities.FirstOrDefault();

                if (contact == null)
                    return Ok(ApiResponse<object>.Error("Contact not found for provided phone number"));

                var note = new Entity("annotation");
                note["subject"] = "Genesys Call Summary";
                note["notetext"] = $"Interaction ID: {model.InteractionId}\nDisposition: {model.DispositionCode}\nRecording URL: {model.RecordingUrl}";
                note["objectid"] = new EntityReference("contact", contact.Id);

                service.Create(note);

                return Ok(ApiResponse<object>.Success("Note created successfully"));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse<object>.Error($"CRM error: {ex.Message}"));
            }
        }


        [HttpPost]
        [Route("interactions/disposition")]
        public IHttpActionResult SaveDisposition([FromBody] InteractionNoteModel model)
        {
            // ✅ Step 1: Validate Bearer Token
            var authHeader = Request.Headers.Authorization;
            string expectedToken = ConfigurationManager.AppSettings["GenesysBearerToken"];

            if (authHeader == null || authHeader.Scheme != "Bearer" || authHeader.Parameter != expectedToken)
            {
                return Content(HttpStatusCode.Unauthorized, ApiResponse<object>.Error("Unauthorized - Invalid bearer token"));
            }

            // ✅ Step 2: Validate Payload
            if (model == null || string.IsNullOrEmpty(model.PhoneNumber))
            {
                return Ok(ApiResponse<object>.Error("Invalid request: PhoneNumber is required"));
            }

            try
            {
                var service = _crmService.GetService();

                // ✅ Step 3: Search for contact by phone number
                var contactQuery = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet("contactid"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                {
                    new ConditionExpression("mobilephone", ConditionOperator.Equal, model.PhoneNumber)
                }
                    }
                };

                var contactResult = service.RetrieveMultiple(contactQuery);
                var contact = contactResult.Entities.FirstOrDefault();

                if (contact == null)
                    return Ok(ApiResponse<object>.Error("Contact not found for provided phone number"));

                // ✅ Step 4: Create annotation note
                var note = new Entity("annotation")
                {
                    ["subject"] = "Genesys Call Summary",
                    ["notetext"] = $"Interaction ID: {model.InteractionId}\nDisposition: {model.DispositionCode}\nRecording URL: {model.RecordingUrl}",
                    ["objectid"] = new EntityReference("contact", contact.Id)
                };

                service.Create(note);

                // ✅ Step 5: Return success response
                return Ok(ApiResponse<object>.Success("Note created successfully"));
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.InternalServerError, ApiResponse<object>.Error($"CRM error: {ex.Message}"));
            }
        }
    }
}
