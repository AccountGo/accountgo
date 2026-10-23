using System;
using System.Threading.RateLimiting;

namespace Api.Service
{
    /// <summary>
    /// Caps how many invoice emails one workspace can send. Customer addresses are whatever a workspace typed in, so
    /// without a cap the feature could be used to send bulk mail from our domain and damage its sending reputation.
    /// Keyed by workspace after tenant resolution, so one workspace can never use up another's allowance.
    /// </summary>
    public class InvoiceEmailLimiter : IDisposable
    {
        public const int PermitsPerHour = 30;

        private readonly PartitionedRateLimiter<int> _limiter = PartitionedRateLimiter.Create<int, int>(companyId =>
            RateLimitPartition.GetSlidingWindowLimiter(companyId, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = PermitsPerHour,
                Window = TimeSpan.FromHours(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));

        public bool TryAcquire(int companyId)
        {
            using var lease = _limiter.AttemptAcquire(companyId);
            return lease.IsAcquired;
        }

        public void Dispose() => _limiter.Dispose();
    }
}
