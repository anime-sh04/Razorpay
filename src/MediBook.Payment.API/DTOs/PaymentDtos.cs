namespace MediBook.Payment.API.DTOs;

// ── Request DTOs ──────────────────────────────────────────────────────────────

/// <summary>Initiate a new payment (creates Razorpay order for online modes, or records Cash).</summary>
public sealed record ProcessPaymentRequest(
    int     AppointmentId,
    Guid    PatientId,
    Guid    ProviderId,
    decimal Amount,
    string  Mode,         // Card | UPI | Wallet | Cash
    string  Currency = "INR",
    string? Notes    = null
);

/// <summary>Confirm a Razorpay payment after frontend verification.</summary>
public sealed record ConfirmPaymentRequest(
    int    PaymentId,
    string RazorpayOrderId,
    string RazorpayPaymentId,
    string RazorpaySignature,
    string TransactionId
);

// ── Response DTOs ─────────────────────────────────────────────────────────────

public sealed record PaymentDto(
    int      PaymentId,
    int      AppointmentId,
    Guid     PatientId,
    Guid     ProviderId,
    decimal  Amount,
    string   Status,
    string   Mode,
    string   TransactionId,
    string   Currency,
    string   RazorpayOrderId,
    string   RazorpayPaymentId,
    DateTime? PaidAt,
    string   Notes
);

/// <summary>Returned when a Razorpay order is created — sent to the frontend to render the checkout widget.</summary>
public sealed record RazorpayOrderResponse(
    int    PaymentId,
    string RazorpayOrderId,
    decimal AmountInPaise,   // Razorpay expects amount in smallest currency unit
    string Currency,
    string KeyId             // public Razorpay key for frontend SDK
);

public sealed record InvoiceDto(
    int      PaymentId,
    int      AppointmentId,
    Guid     PatientId,
    Guid     ProviderId,
    decimal  Amount,
    string   Currency,
    string   Mode,
    string   TransactionId,
    DateTime PaidAt,
    string   InvoiceNumber
);

public sealed record TotalRevenueDto(Guid ProviderId, decimal TotalRevenue);

// ── Shared ────────────────────────────────────────────────────────────────────
public sealed record ApiErrorResponse(
    string               Message,
    IEnumerable<string>? Errors = null
);
