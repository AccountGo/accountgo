using System.Threading;
using System.Threading.Tasks;

namespace Api.Service
{
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
    }
}
