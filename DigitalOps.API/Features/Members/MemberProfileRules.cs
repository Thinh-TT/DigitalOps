using System.ComponentModel.DataAnnotations;

namespace DigitalOps.API.Features.Members;

internal static class MemberProfileRules
{
    private static readonly IReadOnlySet<string> AllowedGenders =
        new HashSet<string>(["Male", "Female", "Other"], StringComparer.Ordinal);

    public static string NormalizeFullName(string value) =>
        CollapseWhitespace(value);

    public static string? NormalizePhone(string? value)
    {
        var normalized = NormalizeOptional(value);
        return normalized is null ? null : CollapseWhitespace(normalized);
    }

    public static string? NormalizeEmail(string? value) =>
        NormalizeOptional(value)?.ToLowerInvariant();

    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? NormalizeSearchQuery(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : CollapseWhitespace(value).ToLowerInvariant();

    public static void ValidateFullName(
        string? value,
        IDictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, "fullName", "Họ và tên không được để trống.");
            return;
        }

        if (NormalizeFullName(value).Length > 200)
        {
            AddError(errors, "fullName", "Họ và tên không được vượt quá 200 ký tự.");
        }
    }

    public static void ValidateGender(
        string? value,
        IDictionary<string, List<string>> errors)
    {
        var gender = NormalizeOptional(value);
        if (gender is not null && !AllowedGenders.Contains(gender))
        {
            AddError(errors, "gender", "Giới tính phải là Male, Female hoặc Other.");
        }
    }

    public static void ValidatePhone(
        string? value,
        IDictionary<string, List<string>> errors)
    {
        var phone = NormalizePhone(value);
        if (phone is null)
        {
            return;
        }

        if (phone.Length > 30)
        {
            AddError(errors, "phone", "Số điện thoại không được vượt quá 30 ký tự.");
        }
        else if (!new PhoneAttribute().IsValid(phone))
        {
            AddError(errors, "phone", "Số điện thoại không đúng định dạng.");
        }
    }

    public static void ValidateEmail(
        string? value,
        IDictionary<string, List<string>> errors)
    {
        var email = NormalizeEmail(value);
        if (email is null)
        {
            return;
        }

        if (email.Length > 254)
        {
            AddError(errors, "email", "Email không được vượt quá 254 ký tự.");
        }
        else if (!new EmailAddressAttribute().IsValid(email))
        {
            AddError(errors, "email", "Email không đúng định dạng.");
        }
    }

    public static void ValidatePosition(
        string? value,
        IDictionary<string, List<string>> errors)
    {
        if (NormalizeOptional(value)?.Length > 150)
        {
            AddError(errors, "position", "Chức vụ không được vượt quá 150 ký tự.");
        }
    }

    public static void AddError(
        IDictionary<string, List<string>> errors,
        string field,
        string message)
    {
        if (!errors.TryGetValue(field, out var fieldErrors))
        {
            fieldErrors = [];
            errors[field] = fieldErrors;
        }

        fieldErrors.Add(message);
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
}
