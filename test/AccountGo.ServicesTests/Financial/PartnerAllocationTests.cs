using Services.Financial;
using Xunit;

namespace AccountGo.ServicesTests.Financial;

public sealed class PartnerAllocationTests
{
    [Fact]
    public void LargestRemainderRoundsToCentsAndBreaksTiesByPartnerId()
    {
        var result = PartnerAllocation.Allocate(1000.01m, new[]
        {
            (PartnerId: 3, Percent: 33.3333m),
            (PartnerId: 1, Percent: 33.3333m),
            (PartnerId: 2, Percent: 33.3334m)
        });

        Assert.Equal(new[] { 333.33m, 333.34m, 333.34m }, result);
        Assert.Equal(1000.01m, result.Sum());
    }
}
