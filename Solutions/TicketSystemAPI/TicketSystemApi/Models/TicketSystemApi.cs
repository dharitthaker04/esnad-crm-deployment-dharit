using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace TicketSystemApi.Models
{
    public class InteractionNoteModel
    {
        public string InteractionId { get; set; }
        public string PhoneNumber { get; set; }
        public string DispositionCode { get; set; }
        public string RecordingUrl { get; set; }
    }
}