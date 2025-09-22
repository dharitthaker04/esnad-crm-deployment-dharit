using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CasesPlugin
    {
        public class TicketFromForwardedMail : IPlugin
        {
            public void Execute(IServiceProvider serviceProvider)
            {
                ITracingService tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
                IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
                IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

                try
                {
                    if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity entity && entity.LogicalName == "email")
                    {
                        Entity email = service.Retrieve("email", entity.Id, new ColumnSet(true));

                        if (email.Attributes.Contains("directioncode")) // Incoming email
                        {
                            if (email.Attributes.Contains("from"))
                            {
                                EntityCollection fromCollection = email.GetAttributeValue<EntityCollection>("from");
                                if (fromCollection != null && fromCollection.Entities.Count > 0)
                                {
                                    foreach (var activityParty in fromCollection.Entities)
                                    {
                                        string senderEmail = activityParty.GetAttributeValue<string>("addressused");

                                        if (!string.IsNullOrEmpty(senderEmail) &&
                                            senderEmail.Equals("no-reply@taadeen.sa", StringComparison.OrdinalIgnoreCase))
                                        {
                                            string plainTextBody = email.GetAttributeValue<string>("description") ?? string.Empty;
                                            string body = StripHtml(plainTextBody);
                                            string subject1 = email.GetAttributeValue<string>("subject");

                                            if (subject1.ToLower() == "book an appointment" || subject1.ToLower() == "حجز موعد")
                                            {
                                                HandleAppointmentEmail(service, email, body, subject1);
                                            }
                                            else if (subject1.ToLower() == "contact us" || subject1.ToLower() == "اتصل بنا")
                                            {
                                                HandleContactUsEmail(service, email, body);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    tracing.Trace("EmailToCasePlugin Error: " + ex.ToString());
                    throw new InvalidPluginExecutionException("An error occurred in EmailToCasePlugin.", ex);
                }
            }

            // ---------------- Appointment Flow ----------------
            private static void HandleAppointmentEmail(IOrganizationService service, Entity email, string body, string subject1)
            {
                string beneficiaryType = MatchValueForAppointment(body, @"(?:Type of Beneficiary|مقدم الطلب)\s*\n(.+)");
                string idNumber = MatchValueForAppointment(body, @"(?:ID Number|رقم الهوية)\s*\n(.+)");
                string companyName = MatchValueForAppointment(body, @"(?:Company Name|اسم الشركة)\s*\n(.+)");
                string CRN = MatchValueForAppointment(body, @"(?:Commercial Registration Number|رقم السجل التجاري)\s*\n(.+)");
                string fullName = MatchValueForAppointment(body, @"(?:Full Name|الإسم الثلاثي)\s*\n(.+)");
                string phone2 = MatchValueForAppointment(body, @"(?:Phone Number|رقم الهاتف)\s*\n(.+)").Replace("&#43;", "+");
                string email1 = MatchValueForAppointment(body, @"(?:Email Address|البريد الإلكتروني)\s*\n(.+)");
                string requestType2 = MatchValueForAppointment(body, @"(?:Request Type|نوع الموعد)\s*\n(.+)");
                string department = MatchValueForAppointment(body, @"(?:Sector|القطاع)\s*\n(.+)");
                string complianceType = MatchValueForAppointment(body, @"(?<=\r?\n\r?\n)(?:الامتثال|Compliance)\s*\r?\n\s*([^\r\n]+)");
                string licenseType = MatchValueForAppointment(body, @"(?<=\r?\n\r?\n)(?:الرخص|Licenses)\s*\r?\n\s*([^\r\n]+)");
                string reason = MatchValueForAppointment(body, @"(?:Reason of this request|سبب حجز الموعد)\s*\n([\s\S]+)");

                string normalizedValue = NormalizeInput(requestType2);
                EntityReference customerRef = null;

                if (beneficiaryType == "فرد" || beneficiaryType.ToLower() == "individual")
                    customerRef = GetOrCreateContact(service, fullName, email1, phone2, idNumber);
                else if (beneficiaryType == "شركة" || beneficiaryType.ToLower() == "company")
                    customerRef = GetOrCreateAccount(service, companyName, email1, phone2, CRN);

                int beneficiaryValue = (beneficiaryType.Equals("فرد", StringComparison.OrdinalIgnoreCase) || beneficiaryType.Equals("Individual", StringComparison.OrdinalIgnoreCase)) ? 1 :
                                       (beneficiaryType.Equals("شركة", StringComparison.OrdinalIgnoreCase) || beneficiaryType.Equals("Company", StringComparison.OrdinalIgnoreCase)) ? 2 : -1;

                EntityReference ticketTypeRef = GetTicketType(service, normalizedValue);

                var incident = new Entity("incident")
                {
                    ["title"] = "book an appointment",
                    ["new_tickettype"] = ticketTypeRef,
                    ["description"] = reason,
                    ["new_beneficiarytype"] = new OptionSetValue(beneficiaryValue),
                    ["new_ticketsubmissionchannel"] = new OptionSetValue(7),
                    ["customerid"] = customerRef,
                    ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("b70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))
                };

                if ((department.Equals("Licensing", StringComparison.OrdinalIgnoreCase) || department.Equals("الرخص", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(licenseType))
                    incident["new_licenses"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_licenses", licenseType));

                else if ((department.Equals("Compliance", StringComparison.OrdinalIgnoreCase) || department.Equals("الامتثال", StringComparison.OrdinalIgnoreCase)) &&
                         !string.IsNullOrWhiteSpace(complianceType))
                    incident["new_compliance"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_compliance", complianceType));

                incident["new_sector"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_sector", department));

                Entity emailUpdate = new Entity("email")
                {
                    Id = email.Id,
                    ["regardingobjectid"] = new EntityReference("incident", service.Create(incident))
                };
                service.Update(emailUpdate);
            }

            // ---------------- Contact Us Flow ----------------
            private static void HandleContactUsEmail(IOrganizationService service, Entity email, string body)
            {
                string beneficiaryType = MatchValue(body, @"(?:نوع المستفيد|Type of Beneficiary)[:\-]?\s*(مستثمر|فرد|Investor|Individual|وكيل لمستثمر|Investor Representative|أخرى|Other)");
                string company = MatchValue(body, @"(?:اسم الشركة|Company Name)[:\-]?\s*([^\r\n]+?)\s*(?=رقم السجل التجاري|Commercial Registration Number)");
                string crNumber = MatchValue(body, @"(?:رقم السجل التجاري|Commercial Registration Number(?:\s*\(CR\))?)[:\-]?\s*(\d{5,})");
                string phone = MatchValue(body, @"(?:رقم الهاتف|Mobile Number)[:\-]?\s*(\d+)");
                string emailAddr = MatchValue(body, @"(?:عنوان البريد الإلكتروني|Email Address)[:\-]?\s*([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})");
                string requestType = MatchValue(body, @"(?:نوع الطلب|Request Type)[:\-]?\s*(.+?)\s*(?=الموضوع|Subject)");
                string subject = MatchValue(body, @"(?:الموضوع|Subject)[:\-]?\s*(.+?)\s*(?=نص الرسالة|Message Text)");
                string message = MatchValue(body, @"(?:نص الرسالة|Message Text)[:\-]?\s*([\s\S]+?)(?=تحميل ملفات|Attachments|$)");
                string nationalId = MatchValue(body, @"(?:رقم الهوية|National ID Number)[:\-]?\s*(\d{10,15})");

                string normalizedValue = NormalizeInput(requestType);
                EntityReference customerRef;

                if (beneficiaryType == "فرد" || beneficiaryType.ToLower() == "individual" ||
                    beneficiaryType == "أخرى" || beneficiaryType.ToLower() == "other")
                    customerRef = GetOrCreateContact(service, emailAddr, emailAddr, phone, nationalId);
                else if (beneficiaryType == "مستثمر" || beneficiaryType.ToLower() == "investor" ||
                         beneficiaryType == "وكيل لمستثمر" || beneficiaryType.ToLower() == "investor representative")
                    customerRef = GetOrCreateAccount(service, company, emailAddr, phone, crNumber);
                else
                    return;

                int beneficiaryValue = -1;
                if (beneficiaryType.Equals("فرد", StringComparison.OrdinalIgnoreCase) || beneficiaryType.Equals("Individual", StringComparison.OrdinalIgnoreCase))
                    beneficiaryValue = 1;
                else if (beneficiaryType.Equals("مستثمر", StringComparison.OrdinalIgnoreCase) || beneficiaryType.Equals("Investor", StringComparison.OrdinalIgnoreCase))
                    beneficiaryValue = 3;
                else if (beneficiaryType.Equals("وكيل لمستثمر", StringComparison.OrdinalIgnoreCase) || beneficiaryType.Equals("Investor Representative", StringComparison.OrdinalIgnoreCase))
                    beneficiaryValue = 4;
                else if (beneficiaryType.Equals("أخرى", StringComparison.OrdinalIgnoreCase) || beneficiaryType.Equals("Other", StringComparison.OrdinalIgnoreCase))
                    beneficiaryValue = 5;

                EntityReference ticketTypeRef = GetTicketType(service, normalizedValue);

                var incident = new Entity("incident")
                {
                    ["title"] = subject,
                    ["description"] = message,
                    ["new_tickettype"] = ticketTypeRef,
                    ["new_beneficiarytype"] = new OptionSetValue(beneficiaryValue),
                    ["new_ticketsubmissionchannel"] = new OptionSetValue(8),
                    ["customerid"] = customerRef,
                    ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))
                };

                Entity emailUpdate = new Entity("email")
                {
                    Id = email.Id,
                    ["regardingobjectid"] = new EntityReference("incident", service.Create(incident))
                };
                service.Update(emailUpdate);
            }

            // ---------------- Utilities ----------------
            public static string StripHtml(string html)
            {
                try
                {
                    var doc = System.Xml.Linq.XDocument.Parse($"<root>{html}</root>");
                    return string.Concat(doc.DescendantNodes().OfType<System.Xml.Linq.XText>().Select(t => t.Value));
                }
                catch
                {
                    return Regex.Replace(html, "<.*?>", string.Empty);
                }
            }

            public static string NormalizeInput(string input)
            {
                if (string.IsNullOrWhiteSpace(input)) return null;
                input = input.Trim();
                return translations.ContainsKey(input) ? translations[input] : null;
            }

            public static EntityReference GetTicketType(IOrganizationService service, string value)
            {
                var query = new QueryExpression("new_tickettype")
                {
                    ColumnSet = new ColumnSet("new_tickettypeid", "new_tickettype"),
                    Criteria = { Conditions = { new ConditionExpression("new_tickettype", ConditionOperator.Equal, value) } }
                };
                EntityCollection result = service.RetrieveMultiple(query);
                if (result.Entities.Count > 0)
                    return new EntityReference("new_tickettype", result.Entities[0].Id);
                return null;
            }

            private static EntityReference GetOrCreateContact(IOrganizationService service, string name, string email, string phone, string idNumber)
            {
                var query = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet("contactid"),
                    Criteria = { Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.Equal, email) } }
                };
                var result = service.RetrieveMultiple(query);
                Guid contactId;
                if (result.Entities.Count > 0)
                    contactId = result.Entities[0].Id;
                else
                {
                    var contact = new Entity("contact")
                    {
                        ["lastname"] = name,
                        ["emailaddress1"] = email,
                        ["mobilephone"] = phone,
                        ["new_nationalidnumber"] = idNumber
                    };
                    contactId = service.Create(contact);
                }
                return new EntityReference("contact", contactId);
            }

            private static EntityReference GetOrCreateAccount(IOrganizationService service, string companyName, string email, string phone, string crn)
            {
                var query = new QueryExpression("account")
                {
                    ColumnSet = new ColumnSet("accountid"),
                    Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, companyName) } }
                };
                var result = service.RetrieveMultiple(query);
                Guid accountId;
                if (result.Entities.Count > 0)
                    accountId = result.Entities[0].Id;
                else
                {
                    var account = new Entity("account")
                    {
                        ["name"] = companyName,
                        ["emailaddress1"] = email,
                        ["new_companyrepresentativephonenumber"] = phone,
                        ["new_crnumber"] = crn,
                        ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))
                    };
                    accountId = service.Create(account);
                }
                return new EntityReference("account", accountId);
            }

            private static int GetOptionSetValue(IOrganizationService service, string entityName, string fieldName, string label)
            {
                var response = (RetrieveAttributeResponse)service.Execute(new RetrieveAttributeRequest
                {
                    EntityLogicalName = entityName,
                    LogicalName = fieldName,
                    RetrieveAsIfPublished = true
                });
                var metadata = (PicklistAttributeMetadata)response.AttributeMetadata;
                foreach (var opt in metadata.OptionSet.Options)
                    if (opt.Label.UserLocalizedLabel.Label == label)
                        return opt.Value.Value;
                throw new InvalidPluginExecutionException($"Option '{label}' not found in '{fieldName}' on '{entityName}'");
            }

            static string MatchValueForAppointment(string input, string pattern)
            {
                var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value.Trim() : "Not Found";
            }

            private static string MatchValue(string input, string pattern)
            {
                input = System.Net.WebUtility.HtmlDecode(input);
                input = input.Replace("\u00A0", " ");
                input = Regex.Replace(input, @"\s+", " ");
                var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
            }

            private static readonly Dictionary<string, string> translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "استفسار", "استفسار" }, { "Inquiry", "استفسار" },
            { "متابعة طلب", "متابعة طلب" }, { "Follow-up Request", "متابعة طلب" },
            { "دعم تقني", "دعم تقني" }, { "Technical Support", "دعم تقني" },
            { "اقتراح", "اقتراح" }, { "Suggestion", "اقتراح" },
            { "شكوى", "شكوى" }, { "Complaint", "شكوى" },
            { "حجز موعد", "حجز موعد" }, { "Meeting request", "حجز موعد" }
        };
        }
    }
