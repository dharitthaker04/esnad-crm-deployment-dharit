using System;
using System.ServiceModel.Description;
using System.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;

namespace TicketSystemApi.Services
{

    public interface ICrmService
    {
        IOrganizationService GetService();
    }

    public class CrmService : ICrmService
    {
        public IOrganizationService GetService()
        {
            string username = ConfigurationManager.AppSettings["CrmUsername"];
            string password = ConfigurationManager.AppSettings["CrmPassword"];
            string serviceUrl = ConfigurationManager.AppSettings["CrmServiceUrl"];

            Uri serviceUri = new Uri(serviceUrl);

            ClientCredentials credentials = new ClientCredentials();
            credentials.UserName.UserName = username;
            credentials.UserName.Password = password;

            var proxy = new OrganizationServiceProxy(serviceUri, null, credentials, null);
            proxy.EnableProxyTypes();

            return (IOrganizationService)proxy;
        }
    }
}