using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Api.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccountGo.ServicesTests
{
    public class WorkspaceEmailTests
    {
        [Fact]
        public async Task Send_ProviderRejection_ThrowsWithoutRedirectingToAnotherRecipient()
        {
            var handler = new RejectingHandler();
            using (var client = new HttpClient(handler))
            {
                var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
                config["RESEND_API_KEY"] = "test-only-key";
                IEmailSender sender = new ResendEmailSender(client, config, NullLogger<ResendEmailSender>.Instance);
                // The provider's reason is passed on, and the message is not retried to any other address.
                var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync("recipient@example.com", "Invitation", "<p>Invitation</p>"));
                Assert.Contains("domain is not verified", error.Message);
                Assert.Equal(1, handler.RequestCount);
            }
        }

        private class RejectingHandler : HttpMessageHandler
        {
            public int RequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"message\":\"domain is not verified\"}")
                });
            }
        }
    }
}
