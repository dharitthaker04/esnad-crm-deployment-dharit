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
                // ✅ Setup tracing + context + service
                var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
                var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                var service = serviceFactory.CreateOrganizationService(context.UserId);
                var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

                try
                {
                    // Run only for "email" create/update
                    if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity entity)
                    {
                        if (entity.LogicalName != "email")
                            return;

                        var email = service.Retrieve("email", entity.Id, new ColumnSet(true));

                        if (email.Contains("directioncode"))
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
                                            string subject1 = email.GetAttributeValue<string>("subject") ?? string.Empty;

                                            // ------------------------------------------
                                            // BOOK AN APPOINTMENT
                                            // ------------------------------------------
                                            if (subject1.Equals("book an appointment", StringComparison.OrdinalIgnoreCase) ||
                                                subject1.Equals("حجز موعد", StringComparison.OrdinalIgnoreCase))
                                            {
                                                HandleBookAppointment(service, email, body, subject1);
                                            }
                                            // ------------------------------------------
                                            // CONTACT US
                                            // ------------------------------------------
                                            else if (subject1.Equals("contact us", StringComparison.OrdinalIgnoreCase) ||
                                                     subject1.Equals("اتصل بنا", StringComparison.OrdinalIgnoreCase))
                                            {
                                                HandleContactUs(service, email, body);
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
                    tracing.Trace("❌ Error: " + ex.ToString());
                    throw new InvalidPluginExecutionException("EmailToCasePlugin failed", ex);
                }
            }

            // ======================================================
            // 🔹 HANDLERS (same as console logic, just refactored)
            // ======================================================

            private void HandleBookAppointment(IOrganizationService service, Entity email, string body, string subject1)
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

                EntityReference customerRef = CreateOrRetrieveCustomer(service, beneficiaryType, fullName, email1, phone2, idNumber, companyName, CRN);

                EntityReference ticketTypeRef = GetTicketType(service, normalizedValue);

                var incident = new Entity("incident")
                {
                    ["title"] = "book an appointment",
                    ["new_tickettype"] = ticketTypeRef,
                    ["description"] = reason,
                    ["new_beneficiarytype"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_beneficiarytype", ConvertBeneficiaryTypeToEnglish(beneficiaryType))),
                    ["new_ticketsubmissionchannel"] = new OptionSetValue(7),
                    ["customerid"] = customerRef,
                    ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))
                };

                if ((department.Equals("Licensing", StringComparison.OrdinalIgnoreCase) || department.Equals("الرخص", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(licenseType))
                {
                    incident["new_licenses"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_licenses", licenseType));
                }
                else if ((department.Equals("Compliance", StringComparison.OrdinalIgnoreCase) || department.Equals("الامتثال", StringComparison.OrdinalIgnoreCase)) &&
                         !string.IsNullOrWhiteSpace(complianceType))
                {
                    incident["new_compliance"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_compliance", complianceType));
                }

                incident["new_sector"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_sector", department));

                email["regardingobjectid"] = new EntityReference("incident", service.Create(incident));
                service.Update(email);
            }

            private void HandleContactUs(IOrganizationService service, Entity email, string body)
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

                EntityReference customerRef = CreateOrRetrieveCustomer(service, beneficiaryType, subject, emailAddr, phone, nationalId, company, crNumber);
                EntityReference ticketTypeRef = GetTicketType(service, normalizedValue);

                var incident = new Entity("incident")
                {
                    ["title"] = subject,
                    ["description"] = message,
                    ["new_tickettype"] = ticketTypeRef,
                    ["new_beneficiarytype"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_beneficiarytype", beneficiaryType)),
                    ["new_ticketsubmissionchannel"] = new OptionSetValue(8),
                    ["customerid"] = customerRef,
                    ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))
                };

                email["regardingobjectid"] = new EntityReference("incident", service.Create(incident));
                service.Update(email);
            }

            // ======================================================
            // 🔹 HELPERS (same as your console functions)
            // ======================================================

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

            private static EntityReference CreateOrRetrieveCustomer(IOrganizationService service, string beneficiaryType,
                string fullNameOrSubject, string email, string phone, string idNumber, string company = null, string crn = null)
            {
                if (beneficiaryType == "فرد" || beneficiaryType.Equals("individual", StringComparison.OrdinalIgnoreCase))
                {
                    var query = new QueryExpression("contact")
                    {
                        ColumnSet = new ColumnSet("contactid"),
                        Criteria = new FilterExpression
                        {
                            Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.Equal, email) }
                        }
                    };

                    var result = service.RetrieveMultiple(query);
                    Guid contactId;

                    if (result.Entities.Count > 0)
                        contactId = result.Entities[0].Id;
                    else
                    {
                        var contact = new Entity("contact")
                        {
                            ["lastname"] = fullNameOrSubject,
                            ["emailaddress1"] = email,
                            ["mobilephone"] = phone,
                            ["new_beneficiarytype"] = new OptionSetValue(GetOptionSetValue(service, "contact", "new_beneficiarytype", beneficiaryType)),
                            ["new_nationalidnumber"] = idNumber
                        };
                        contactId = service.Create(contact);
                    }

                    return new EntityReference("contact", contactId);
                }
                else if (beneficiaryType == "شركة" || beneficiaryType.Equals("company", StringComparison.OrdinalIgnoreCase) ||
                         beneficiaryType == "مستثمر" || beneficiaryType.Equals("investor", StringComparison.OrdinalIgnoreCase))
                {
                    var query = new QueryExpression("account")
                    {
                        ColumnSet = new ColumnSet("accountid"),
                        Criteria = new FilterExpression
                        {
                            Conditions = { new ConditionExpression("name", ConditionOperator.Equal, company) }
                        }
                    };

                    var result = service.RetrieveMultiple(query);
                    Guid accountId;

                    if (result.Entities.Count > 0)
                        accountId = result.Entities[0].Id;
                    else
                    {
                        var account = new Entity("account")
                        {
                            ["name"] = company,
                            ["emailaddress1"] = email,
                            ["new_companyrepresentativephonenumber"] = phone,
                            ["new_crnumber"] = crn,
                            ["new_beneficiarytype"] = new OptionSetValue(GetOptionSetValue(service, "account", "new_beneficiarytype", beneficiaryType)),
                            ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))
                        };
                        accountId = service.Create(account);
                    }

                    return new EntityReference("account", accountId);
                }

                throw new InvalidPluginExecutionException("Unsupported beneficiary type: " + beneficiaryType);
            }

            public static string NormalizeInput(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    return null;

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

                var result = service.RetrieveMultiple(query);

                if (result.Entities.Count > 0)
                {
                    var record = result.Entities[0];
                    string name = record.GetAttributeValue<string>("new_name");
                    return new EntityReference("new_tickettype", record.Id) { Name = name };
                }

                return null;
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
                {
                    if (opt.Label.UserLocalizedLabel.Label == label)
                        return opt.Value.Value;
                }

                throw new InvalidPluginExecutionException($"Option '{label}' not found in '{fieldName}' on '{entityName}'");
            }

            private static string MatchValueForAppointment(string input, string pattern)
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

            private static string ConvertBeneficiaryTypeToEnglish(string arabicType)
            {
                switch (arabicType.Trim())
                {
                    case "مستثمر": return "Investor";
                    case "Investor": return "Investor";
                    case "فرد": return "Individual";
                    case "Individual": return "Individual";
                    case "Company":
                    case "شركة": return "Company";
                    default: return "Unknown";
                }
            }

            private static readonly Dictionary<string, string> translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ار", "ار" },
            { "inquiry", "ار" },
            { "متابعة طلب", "متابعة طلب" },
            { "follow-up request", "متابعة طلب" },
            { "دعم فني", "دعم فني" },
            { "technical support", "دعم فني" },
            { "اقتراح", "اقتراح" },
            { "suggestion", "اقتراح" },
            { "شكوى", "شكوى" },
            { "complaint", "شكوى" },
            { "طلب مقابلة", "طلب مقابلة" },
            { "meeting request", "طلب مقابلة" }
        };
        }
    }
