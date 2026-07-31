using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Members;

public sealed class MemberUpsertRequest
{
    private string? _fullName;
    private DateOnly? _dateOfBirth;
    private string? _gender;
    private string? _address;
    private string? _phone;
    private string? _email;
    private string? _position;
    private DateOnly? _joinDate;
    private MemberStatus? _status;
    private string? _notes;

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

    public DateOnly? DateOfBirth
    {
        get => _dateOfBirth;
        set
        {
            _dateOfBirth = value;
            HasDateOfBirth = true;
        }
    }

    [StringLength(20)]
    public string? Gender
    {
        get => _gender;
        set
        {
            _gender = value;
            HasGender = true;
        }
    }

    public string? Address
    {
        get => _address;
        set
        {
            _address = value;
            HasAddress = true;
        }
    }

    [Phone, StringLength(30)]
    public string? Phone
    {
        get => _phone;
        set
        {
            _phone = value;
            HasPhone = true;
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

    public DateOnly? JoinDate
    {
        get => _joinDate;
        set
        {
            _joinDate = value;
            HasJoinDate = true;
        }
    }

    public MemberStatus? Status
    {
        get => _status;
        set
        {
            _status = value;
            HasStatus = true;
        }
    }

    public string? Notes
    {
        get => _notes;
        set
        {
            _notes = value;
            HasNotes = true;
        }
    }

    [JsonIgnore]
    public bool HasFullName { get; private set; }

    [JsonIgnore]
    public bool HasDateOfBirth { get; private set; }

    [JsonIgnore]
    public bool HasGender { get; private set; }

    [JsonIgnore]
    public bool HasAddress { get; private set; }

    [JsonIgnore]
    public bool HasPhone { get; private set; }

    [JsonIgnore]
    public bool HasEmail { get; private set; }

    [JsonIgnore]
    public bool HasPosition { get; private set; }

    [JsonIgnore]
    public bool HasJoinDate { get; private set; }

    [JsonIgnore]
    public bool HasStatus { get; private set; }

    [JsonIgnore]
    public bool HasNotes { get; private set; }
}

public sealed record MemberResponse(
    Guid Id,
    string FullName,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Address,
    string? Phone,
    string? Email,
    string? Position,
    DateOnly? JoinDate,
    MemberStatus Status,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record MemberLookupResponse(
    Guid Id,
    string FullName,
    string? Position);

public sealed class MemberListQuery
{
    [FromQuery(Name = "q")]
    [StringLength(200)]
    public string? Q { get; init; }

    [FromQuery(Name = "status")]
    public MemberStatus? Status { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed class MemberLookupQuery
{
    [FromQuery(Name = "q")]
    [StringLength(200)]
    public string? Q { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
