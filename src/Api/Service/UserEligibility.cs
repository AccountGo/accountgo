using System;
using Api.Data;

namespace Api.Service;

public static class UserEligibility
{
    public static bool IsEligible(ApplicationUser? user)
    {
        if (user == null || user.DeletedAt != null)
        {
            return false;
        }

        if (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return false;
        }

        return true;
    }
}
