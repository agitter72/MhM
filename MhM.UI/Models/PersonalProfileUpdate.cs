using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace MhM.UI.Models;

public sealed class PersonalProfileUpdate
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Username]
    public string Username { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [InternationalPhone]
    public string? Phone { get; set; }

    [Required, MaxLength(20)]
    public string PostalCode { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string City { get; set; } = string.Empty;
}

public sealed record ProfileUpdateResult(bool Success, string Message, string? Phone = null, string? Username = null);

public sealed class UsernameAttribute : ValidationAttribute
{
    public UsernameAttribute()
    {
        ErrorMessage = "Der Nutzername muss 3 bis 30 Zeichen lang sein und darf nur Kleinbuchstaben, Zahlen, Punkte und Unterstriche enthalten.";
    }

    public override bool IsValid(object? value) => UsernameRules.TryNormalize(value?.ToString(), out _);
}

public static partial class UsernameRules
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._]{1,28}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidUsernameRegex();

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return ValidUsernameRegex().IsMatch(normalized)
            && !normalized.Contains("..", StringComparison.Ordinal)
            && !normalized.Contains("__", StringComparison.Ordinal)
            && !normalized.Contains("._", StringComparison.Ordinal)
            && !normalized.Contains("_.", StringComparison.Ordinal);
    }
}

public sealed class InternationalPhoneAttribute : ValidationAttribute
{
    public InternationalPhoneAttribute()
    {
        ErrorMessage = "Bitte eine internationale Telefonnummer mit Ländervorwahl eingeben, z. B. +49 151 234 567 89.";
    }

    public override bool IsValid(object? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.ToString()))
        {
            return true;
        }

        return InternationalPhone.TryNormalize(value.ToString(), out _);
    }
}

public static partial class InternationalPhone
{
    [GeneratedRegex("^\\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164Regex();

    public static bool TryNormalize(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith("00", StringComparison.Ordinal))
        {
            candidate = $"+{candidate[2..]}";
        }

        candidate = $"{(candidate.StartsWith('+') ? "+" : string.Empty)}{new string(candidate.Where(char.IsDigit).ToArray())}";
        if (!E164Regex().IsMatch(candidate))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }
}
