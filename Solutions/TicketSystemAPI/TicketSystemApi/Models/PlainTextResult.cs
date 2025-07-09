using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web;
using System.Web.Http;

namespace TicketSystemApi.Models
{
    public class PlainTextResult : IHttpActionResult
    {
        private readonly string _content;
        private readonly HttpRequestMessage _request;

        public PlainTextResult(string content, HttpRequestMessage request)
        {
            _content = content;
            _request = request;
        }

        public Task<HttpResponseMessage> ExecuteAsync(CancellationToken cancellationToken)
        {
            var response = _request.CreateResponse(HttpStatusCode.OK);
            response.Content = new StringContent(_content, Encoding.UTF8, "text/plain");
            return Task.FromResult(response);
        }
    }
}