﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace TicketSystemApi.Models
{
    public class CaseRequestModel
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Incident { get; set; }
        public string BeneficiaryType { get; set; }
        public string PrimaryContactPhone { get; set; }
    }

}