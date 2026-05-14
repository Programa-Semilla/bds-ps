using Microsoft.AspNetCore.Identity;

namespace FundingPlatform.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool IsSystemSentinel { get; init; }
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// Spec 021 / FR-018 / FR-019 — admin-set free-text code (e.g. employee /
    /// affiliate identifier). Read-only to the user on <c>/profile</c>; visible
    /// on the admin user form and on admin reports. Nullable, ≤ 40 chars (column
    /// is <c>NVARCHAR(40)</c> per the dacpac alter in T017).
    /// </summary>
    public string? CodigoPersonal { get; set; }

    /// <summary>
    /// Spec 016 — group memberships. Admins MUST never carry rows here
    /// (FR-009); enforcement is at the Web/Service boundary, not the column.
    /// </summary>
    public virtual ICollection<UserGroupMembership> Memberships { get; private set; }
        = new List<UserGroupMembership>();

    public ApplicationUser()
    {
    }

    public ApplicationUser(string email, string firstName, string lastName, string? phone)
    {
        UserName = email;
        Email = email;
        NormalizedUserName = email.ToUpperInvariant();
        NormalizedEmail = email.ToUpperInvariant();
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phone;
    }

    public static ApplicationUser CreateSentinel(string email)
    {
        return new ApplicationUser
        {
            UserName = email,
            Email = email,
            NormalizedUserName = email.ToUpperInvariant(),
            NormalizedEmail = email.ToUpperInvariant(),
            IsSystemSentinel = true,
            MustChangePassword = false,
        };
    }
}
