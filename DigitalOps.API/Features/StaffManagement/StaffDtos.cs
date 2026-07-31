using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.StaffManagement;

public sealed record StaffCreateRequest(
    [param: Required, StringLength(256)] string UserName,
    [param: Required, EmailAddress, StringLength(254)] string Email,
    [param: Required] string TemporaryPassword,
    [param: Required, StringLength(200)] string FullName,
    [param: StringLength(150)] string? Position,
    [param: StringLength(200)] string? Department,
    [param: StringLength(30)] string? Phone,
    [param: Required, MinLength(1)] string[] Roles);

public sealed class StaffUpdateRequest
{
    private string? _fullName;
    private string? _position;
    private string? _department;
    private string? _email;
    private string? _phone;
    private bool? _isActive;

    [StringLength(200)]
    public string? FullName
    {
        get => _fullName;
        set
        {
            _fullName = value;
            HasFullName = true;
        }
    }

    [StringLength(150)]
    public string? Position
    {
        get => _position;
        set
        {
            _position = value;
            HasPosition = true;
        }
    }

    [StringLength(200)]
    public string? Department
    {
        get => _department;
        set
        {
            _department = value;
            HasDepartment = true;
        }
    }

    [EmailAddress, StringLength(254)]
    public string? Email
    {
        get => _email;
        set
        {
            _email = value;
            HasEmail = true;
        }
    }

    [StringLength(30)]
    public string? Phone
    {
        get => _phone;
        set
        {
            _phone = value;
            HasPhone = true;
        }
    }

    public bool? IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            HasIsActive = true;
        }
    }

    [JsonIgnore]
    public bool HasFullName { get; private set; }

    [JsonIgnore]
    public bool HasPosition { get; private set; }

    [JsonIgnore]
    public bool HasDepartment { get; private set; }

    [JsonIgnore]
    public bool HasEmail { get; private set; }

    [JsonIgnore]
    public bool HasPhone { get; private set; }

    [JsonIgnore]
    public bool HasIsActive { get; private set; }
}

public sealed record RoleAssignmentRequest(
    [param: Required, MinLength(1)] string[] Roles);

public sealed record ResetPasswordRequest(
    [param: Required] string TemporaryPassword);

public sealed record StaffResponse(
    Guid Id,
    Guid IdentityUserId,
    string UserName,
    string FullName,
    string? Position,
    string? Department,
    string Email,
    string? Phone,
    bool IsActive,
    string[] Roles,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed class StaffListQuery
{
    [FromQuery(Name = "activeOnly")]
    public bool? ActiveOnly { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
