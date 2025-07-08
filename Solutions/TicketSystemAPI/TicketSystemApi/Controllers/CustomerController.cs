using System;
using System.Linq;
using System.Net;
using System.Web.Http;
using System.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TicketSystemApi.Services;
using TicketSystemApi.Models;

namespace TicketSystemApi.Controllers
{
    [RoutePrefix("customers")]
    public class CustomerController : ApiController
    {
        private readonly ICrmService _crmService;

        public CustomerController()
        {
            _crmService = new CrmService();
        }

        [HttpGet]
        [Route("by-ticket/{ticketNumber}")]
        public IHttpActionResult GetCustomerByTicket(string ticketNumber)
        {
            var authHeader = Request.Headers.Authorization;
            string expectedToken = ConfigurationManager.AppSettings["ApiBearerToken"];

            if (authHeader == null || authHeader.Scheme != "Bearer" || authHeader.Parameter != expectedToken)
                return Content(HttpStatusCode.Unauthorized,
                    ApiResponse<object>.Error("Unauthorized - Invalid bearer token"));

            if (string.IsNullOrWhiteSpace(ticketNumber))
                return Content(HttpStatusCode.BadRequest,
                    ApiResponse<object>.Error("Ticket number is required."));

            ticketNumber = ticketNumber.Trim().ToUpper();

            try
            {
                var service = _crmService.GetService();

                var query = new QueryExpression("incident")
                {
                    ColumnSet = new ColumnSet("ticketnumber", "customerid", "incidentid"),
                    Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("ticketnumber", ConditionOperator.Equal, ticketNumber)
                }
            }
                };

                var result = service.RetrieveMultiple(query);
                var incident = result.Entities.FirstOrDefault();

                if (incident == null)
                    return Content(HttpStatusCode.NotFound,
                        ApiResponse<object>.Error($"No case found for ticket number: {ticketNumber}"));

                if (!incident.Contains("customerid") || !(incident["customerid"] is EntityReference customerRef))
                    return Ok(ApiResponse<object>.Error("Customer is not linked with the specified case."));

                var customer = service.Retrieve("contact", customerRef.Id,
                    new ColumnSet("firstname", "lastname", "emailaddress1"));

                if (customer == null)
                    return Ok(ApiResponse<object>.Error("Customer record could not be retrieved."));

                // 🔍 Check if feedback already exists for the case
                var feedbackQuery = new QueryExpression("new_customersatisfactionscore")
                {
                    ColumnSet = new ColumnSet("new_customersatisfactionscoreid"),
                    Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("new_csatcase", ConditionOperator.Equal, incident.Id)
                }
            }
                };

                var feedbackResult = service.RetrieveMultiple(feedbackQuery);
                if (feedbackResult.Entities.Any())
                {
                    return Content(HttpStatusCode.BadRequest,
                        ApiResponse<object>.Error("Feedback already submitted for this case."));
                }

                return Ok(ApiResponse<object>.Success(new
                {
                    CaseId = incident.Id,
                    TicketNumber = ticketNumber,
                    CustomerId = customer.Id,
                    FirstName = customer.GetAttributeValue<string>("firstname"),
                    LastName = customer.GetAttributeValue<string>("lastname"),
                    Email = customer.GetAttributeValue<string>("emailaddress1")
                }, "Customer retrieved successfully"));
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.InternalServerError,
                    ApiResponse<object>.Error($"CRM error: {ex.Message}"));
            }
        }



        [HttpPost]
        [Route("submit-feedback")]
        public IHttpActionResult SubmitCustomerFeedback([FromBody] CustomerFeedbackModel model)
        {
            var authHeader = Request.Headers.Authorization;
            string expectedToken = ConfigurationManager.AppSettings["ApiBearerToken"];

            if (authHeader == null || authHeader.Scheme != "Bearer" || authHeader.Parameter != expectedToken)
                return Content(HttpStatusCode.Unauthorized,
                    ApiResponse<object>.Error("Unauthorized - Invalid bearer token"));

            if (model == null || string.IsNullOrWhiteSpace(model.CaseId))
                return Content(HttpStatusCode.BadRequest,
                    ApiResponse<object>.Error("Case ID is required."));

            if (model.Rating < 1 || model.Rating > 5)
                return Content(HttpStatusCode.BadRequest,
                    ApiResponse<object>.Error("Rating must be between 1 and 5."));

            try
            {
                var service = _crmService.GetService();
                Guid caseGuid = new Guid(model.CaseId);

                // 🔍 Check if feedback already exists for this case
                var existingQuery = new QueryExpression("new_customersatisfactionscore")
                {
                    ColumnSet = new ColumnSet("new_customersatisfactionrating"),
                    Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("new_csatcase", ConditionOperator.Equal, caseGuid)
                }
            }
                };

                var existingFeedback = service.RetrieveMultiple(existingQuery);
                if (existingFeedback.Entities.Any())
                {
                    return Content(HttpStatusCode.Conflict,
                        ApiResponse<object>.Error("Feedback already submitted for this case."));
                }

                // ✅ Create new feedback
                if (model.TimeAppropriate != 1 && model.TimeAppropriate != 2)
                    return Content(HttpStatusCode.BadRequest,
                        ApiResponse<object>.Error("Please answer whether the time taken was appropriate."));

                var commentValue = string.IsNullOrWhiteSpace(model.Comment) ? "No comments added by customer" : model.Comment.Trim();

                var feedback = new Entity("new_customersatisfactionscore");
                feedback["new_customersatisfactionrating"] = new OptionSetValue(model.Rating);
                feedback["new_comment"] = commentValue;
                feedback["new_customersatisfactionscore"] = commentValue;  // ⬅️ Additional field to store same comment
                feedback["new_csatcase"] = new EntityReference("incident", caseGuid);
                feedback["new_wasthetimetakentoprocesstheticketappropri"] = (model.TimeAppropriate == 1);

                var feedbackId = service.Create(feedback);

                return Ok(ApiResponse<object>.Success(new
                {
                    FeedbackId = feedbackId
                }, "Feedback submitted successfully"));
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.InternalServerError,
                    ApiResponse<object>.Error($"CRM error: {ex.Message}"));
            }
        }
    }
}
