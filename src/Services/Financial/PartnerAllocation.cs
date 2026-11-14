using System;
using System.Collections.Generic;
using System.Linq;

namespace Services.Financial;

public static class PartnerAllocation
{
    public static decimal[] Allocate(decimal amount, IReadOnlyList<(int PartnerId, decimal Percent)> shares)
    {
        if (shares.Count == 0) return Array.Empty<decimal>();
        var sign = amount < 0m ? -1m : 1m;
        var cents = decimal.ToInt64(decimal.Round(decimal.Abs(amount) * 100m, 0, MidpointRounding.AwayFromZero));
        var allocations = shares.Select((share, index) =>
        {
            var exact = cents * share.Percent / 100m;
            var floor = decimal.ToInt64(decimal.Floor(exact));
            return new { share.PartnerId, Index = index, Cents = floor, Remainder = exact - floor };
        }).ToArray();
        var remaining = cents - allocations.Sum(a => a.Cents);
        var extra = allocations.OrderByDescending(a => a.Remainder).ThenBy(a => a.PartnerId).Take((int)remaining)
            .Select(a => a.Index).ToHashSet();
        return allocations.Select(a => sign * (a.Cents + (extra.Contains(a.Index) ? 1 : 0)) / 100m).ToArray();
    }
}
