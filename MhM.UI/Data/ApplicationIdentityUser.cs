using Microsoft.AspNetCore.Identity;

namespace MhM.UI.Data;

public class ApplicationIdentityUser : IdentityUser
{
    /// <summary>
    /// User's first name
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// User's last name
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Full name (FirstName + LastName)
    /// </summary>
    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>
    /// Indicates if the account is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Account created date
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}