using System;

namespace Api.Data;

public class ExternalTokenRedemption
{
    public string Provider { get; set; } = null!;
    public string TokenId { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime RedeemedAtUtc { get; set; }
}
