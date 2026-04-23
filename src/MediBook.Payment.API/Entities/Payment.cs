namespace MediBook.Payment.API.Entities;

/// <summary>
/// Payment aggregate linked to exactly one appointment.
/// Status lifecycle:
///   Pending → Paid      (on successful gateway capture)
///   Pending → Failed    (on gateway error / timeout)
///   Paid    → Refunded  (on appointment cancellation — no automated refund flow)
/// Cash payments move directly: Pending → Paid (manual confirmation).
/// </summary>
public sealed class Payment
{
    // ── Status constants ─────────────────────────────────────────────────────
    public const string StatusPending  = "Pending";
    public const string StatusPaid     = "Paid";
    public const string StatusFailed   = "Failed";
    public const string StatusRefunded = "Refunded";

    public static readonly IReadOnlySet<string> ValidStatuses =
        new HashSet<string> { StatusPending, StatusPaid, StatusFailed, StatusRefunded };

    // ── Mode constants ───────────────────────────────────────────────────────
    public const string ModeCard   = "Card";
    public const string ModeUpi    = "UPI";
    public const string ModeWallet = "Wallet";
    public const string ModeCash   = "Cash";

    public static readonly IReadOnlySet<string> ValidModes =
        new HashSet<string> { ModeCard, ModeUpi, ModeWallet, ModeCash };

    // ── Properties ────────────────────────────────────────────────────────────
    public int      PaymentId     { get; private set; }
    public int      AppointmentId { get; private set; }
    public Guid     PatientId     { get; private set; }
    public Guid     ProviderId    { get; private set; }
    public decimal  Amount        { get; private set; }
    public string   Status        { get; private set; } = StatusPending;
    public string   Mode          { get; private set; } = string.Empty;
    public string   TransactionId { get; private set; } = string.Empty;
    public string   Currency      { get; private set; } = "INR";
    public string   RazorpayOrderId  { get; private set; } = string.Empty;
    public string   RazorpayPaymentId { get; private set; } = string.Empty;
    public DateTime? PaidAt        { get; private set; }
    public string   Notes         { get; private set; } = string.Empty;

    private Payment() { } // EF Core

    // ── Factory ───────────────────────────────────────────────────────────────
    public static Payment Create(
        int     appointmentId,
        Guid    patientId,
        Guid    providerId,
        decimal amount,
        string  mode,
        string  currency = "INR",
        string? notes    = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.");
        if (!ValidModes.Contains(mode))
            throw new ArgumentException(
                $"'{mode}' is not a valid payment mode. Valid: {string.Join(", ", ValidModes)}");
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.");

        return new Payment
        {
            AppointmentId = appointmentId,
            PatientId     = patientId,
            ProviderId    = providerId,
            Amount        = amount,
            Mode          = mode,
            Currency      = currency.Trim().ToUpperInvariant(),
            Status        = StatusPending,
            Notes         = notes?.Trim() ?? string.Empty
        };
    }

    // ── State transitions ─────────────────────────────────────────────────────

    /// <summary>Records a successful Razorpay capture.</summary>
    public void MarkPaid(string razorpayOrderId, string razorpayPaymentId, string transactionId)
    {
        if (Status != StatusPending)
            throw new InvalidOperationException(
                $"Cannot mark payment as Paid from status '{Status}'. Only Pending payments can be captured.");
        if (string.IsNullOrWhiteSpace(razorpayPaymentId))
            throw new ArgumentException("RazorpayPaymentId is required.");

        RazorpayOrderId   = razorpayOrderId?.Trim()   ?? string.Empty;
        RazorpayPaymentId = razorpayPaymentId.Trim();
        TransactionId     = transactionId?.Trim()     ?? string.Empty;
        Status            = StatusPaid;
        PaidAt            = DateTime.UtcNow;
    }

    /// <summary>Records a gateway or processing failure.</summary>
    public void MarkFailed(string? reason = null)
    {
        if (Status != StatusPending)
            throw new InvalidOperationException(
                $"Cannot mark payment as Failed from status '{Status}'.");

        Status = StatusFailed;
        if (!string.IsNullOrWhiteSpace(reason))
            Notes = reason.Trim();
    }

    /// <summary>
    /// Marks a Paid payment as Refunded (status only — actual gateway refund is caller's responsibility).
    /// </summary>
    public void MarkRefunded()
    {
        if (Status != StatusPaid)
            throw new InvalidOperationException(
                $"Cannot refund a payment with status '{Status}'. Only Paid payments can be refunded.");

        Status = StatusRefunded;
    }

    /// <summary>Generic status update — used by admin/internal calls.</summary>
    public void SetStatus(string status)
    {
        if (!ValidStatuses.Contains(status))
            throw new ArgumentException(
                $"'{status}' is not a valid payment status. Valid: {string.Join(", ", ValidStatuses)}");
        Status = status;
    }

    // ── Read helpers (match class diagram) ────────────────────────────────────
    public int    GetPaymentId() => PaymentId;
    public string GetStatus()   => Status;
}
