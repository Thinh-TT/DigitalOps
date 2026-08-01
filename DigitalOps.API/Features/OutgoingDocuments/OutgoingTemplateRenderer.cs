using System.Globalization;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.Members;

namespace DigitalOps.API.Features.OutgoingDocuments;

internal static class OutgoingTemplateRenderer
{
    private static readonly IReadOnlyDictionary<string, Func<Member, string?>> MemberTokens =
        new Dictionary<string, Func<Member, string?>>(StringComparer.Ordinal)
        {
            ["{{member.fullName}}"] = member => member.FullName,
            ["{{member.dateOfBirth}}"] = member => FormatDate(member.DateOfBirth),
            ["{{member.gender}}"] = member => FormatGender(member.Gender),
            ["{{member.address}}"] = member => member.Address,
            ["{{member.phone}}"] = member => member.Phone,
            ["{{member.email}}"] = member => member.Email,
            ["{{member.position}}"] = member => member.Position,
            ["{{member.joinDate}}"] = member => FormatDate(member.JoinDate)
        };

    private static readonly IReadOnlyDictionary<string, Func<IncomingDocument, string?>> IncomingTokens =
        new Dictionary<string, Func<IncomingDocument, string?>>(StringComparer.Ordinal)
        {
            ["{{incoming.referenceNumber}}"] = document => document.ReferenceNumber,
            ["{{incoming.senderOrg}}"] = document => document.SenderOrg,
            ["{{incoming.summary}}"] = document => document.Summary,
            ["{{incoming.receivedDate}}"] = document => FormatDate(document.ReceivedDate),
            ["{{incoming.deadline}}"] = document => FormatDate(document.Deadline)
        };

    public static string Render(
        string templateContent,
        Member? member,
        IncomingDocument? incoming)
    {
        var content = templateContent;

        if (member is not null)
        {
            foreach (var (token, valueFactory) in MemberTokens)
            {
                content = ReplaceWhenValueExists(content, token, valueFactory(member));
            }
        }

        if (incoming is not null)
        {
            foreach (var (token, valueFactory) in IncomingTokens)
            {
                content = ReplaceWhenValueExists(content, token, valueFactory(incoming));
            }
        }

        return content;
    }

    private static string ReplaceWhenValueExists(string content, string token, string? value) =>
        value is null ? content : content.Replace(token, value, StringComparison.Ordinal);

    private static string? FormatDate(DateOnly? value) =>
        value?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly value) =>
        value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string? FormatGender(string? value) =>
        value switch
        {
            "Male" => "Nam",
            "Female" => "Nữ",
            "Other" => "Khác",
            _ => value
        };
}
