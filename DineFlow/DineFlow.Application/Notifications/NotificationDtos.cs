using System.ComponentModel.DataAnnotations;
using DineFlow.Domain.Notifications;

namespace DineFlow.Application.Notifications;

public record AlertContactDto(Guid Id, string Name, string PhoneNumber, DateTimeOffset CreatedAt);

/// <param name="Provider">Where WhatsApp messages go: "outbox" (written to a folder, development) or "meta".</param>
public record AlertSettingsDto(bool Enabled, IReadOnlyList<AlertContactDto> Contacts, string Provider);

public record AlertToggleRequest(bool Enabled);

public record AlertContactRequest(
    [Required, StringLength(60, MinimumLength = 1)] string Name,
    [Required, StringLength(30, MinimumLength = 8)] string PhoneNumber);

public record DeliveryQuery(
    [Required] Guid VenueId,
    NotificationChannel? Channel = null,
    DeliveryStatus? Status = null,
    [Range(1, 10_000)] int Page = 1,
    [Range(1, 100)] int PageSize = 25);

public record DeliveryDto(
    Guid Id,
    NotificationChannel Channel,
    NotificationKind Kind,
    string Recipient,
    string? Subject,
    string TextBody,
    DeliveryStatus Status,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? NextAttemptAt,
    string? LastError,
    Guid? SubjectId);

/// <param name="Failed">Messages that gave up and are waiting for someone to resend them (any page).</param>
public record DeliveryPageDto(IReadOnlyList<DeliveryDto> Items, int Total, int Page, int PageSize, int Failed);
