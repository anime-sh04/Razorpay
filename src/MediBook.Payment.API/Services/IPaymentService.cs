using MediBook.Payment.API.DTOs;

namespace MediBook.Payment.API.Services;

/// <summary>
/// Business-logic contract for the Payment-Service.
/// Matches the IPaymentService class diagram (no refund operations).
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Initiates a payment:
    /// - For online modes (Card/UPI/Wallet): creates a Razorpay order and returns order details.
    /// - For Cash: records a Pending payment immediately.
    /// Returns the created PaymentDto plus Razorpay order metadata when applicable.
    /// </summary>
    Task<(PaymentDto Payment, RazorpayOrderResponse? RazorpayOrder)> ProcessPaymentAsync(
        ProcessPaymentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Verifies the Razorpay signature and marks the payment as Paid.
    /// Throws if signature verification fails.
    /// </summary>
    Task<PaymentDto> ConfirmPaymentAsync(ConfirmPaymentRequest request, CancellationToken ct = default);

    /// <summary>Returns the payment linked to the given appointment.</summary>
    Task<PaymentDto?> GetPaymentByAppointmentAsync(int appointmentId, CancellationToken ct = default);

    /// <summary>Returns all payments made by a patient.</summary>
    Task<IReadOnlyList<PaymentDto>> GetPaymentsByPatientAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>Returns all payments (across all patients). Admin use.</summary>
    Task<IReadOnlyList<PaymentDto>> GetPaymentHistoryAsync(CancellationToken ct = default);

    /// <summary>Returns the current status string for a payment.</summary>
    Task<string> GetPaymentStatusAsync(int paymentId, CancellationToken ct = default);

    /// <summary>Generic status update (admin/internal override).</summary>
    Task<PaymentDto> UpdatePaymentStatusAsync(int paymentId, string status, CancellationToken ct = default);

    /// <summary>
    /// Generates an invoice DTO for a completed (Paid) payment.
    /// </summary>
    Task<InvoiceDto> GenerateInvoiceAsync(int paymentId, CancellationToken ct = default);

    /// <summary>Returns the total paid revenue for a provider.</summary>
    Task<TotalRevenueDto> GetTotalRevenueAsync(Guid providerId, CancellationToken ct = default);
}
