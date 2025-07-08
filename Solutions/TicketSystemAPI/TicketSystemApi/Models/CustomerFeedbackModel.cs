using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace TicketSystemApi.Models
{
    public class CustomerFeedbackModel
    {
        public string CaseId { get; set; }
        public int Rating { get; set; }
        public string Comment { get; set; }
        public int? TimeAppropriate { get; set; }
    }
}