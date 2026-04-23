using System.Security.Cryptography;
using System.Text;
using MediBook.Payment.API.DTOs;
using MediBook.Payment.API.Helpers;
using MediBook.Payment.API.Repositories;
using Razorpay.Api;

namespace MediBook.Payment.API.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly IPaymentRepository      _repo;
    private readonly RazorpaySettings        _razorpay;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentRepository      repo,
        RazorpaySettings        razorpay,
        ILogger<PaymentService> logger)
    {
        _repo     = repo;
        _razorpay = razorpay;
        _logger   = logger;
    }

    // ── ProcessPayment ────────────────────────────────────────────────────────

    public async Task<(PaymentDto Payment, RazorpayOrderResponse? RazorpayOrder)> ProcessPaymentAsync(
        ProcessPaymentRequest request, CancellationToken ct = default)
    {
        var existing = await _repo.FindByAppointmentIdAsync(request.AppointmentId, ct);
        if (existing is not null)
            throw new InvalidOperationException(
                $"A payment record already exists for AppointmentId {request.AppointmentId}.");

        var payment = Entities.Payment.Create(
            appointmentId: request.AppointmentId,
            patientId:     request.PatientId,
            providerId:    request.ProviderId,
            amount:        request.Amount,
            mode:          request.Mode,
            currency:      request.Currency,
            notes:         request.Notes);

        await _repo.AddAsync(payment, ct);

        _logger.LogInformation(
            "Payment record created. PaymentId={PaymentId}, AppointmentId={AppointmentId}, Mode={Mode}",
            payment.PaymentId, payment.AppointmentId, payment.Mode);

        if (payment.Mode == Entities.Payment.ModeCash)
            return (ToDto(payment), null);

        var razorpayOrder = CreateRazorpayOrder(payment);
        return (ToDto(payment), razorpayOrder);
    }

    // ── ConfirmPayment ────────────────────────────────────────────────────────

    public async Task<PaymentDto> ConfirmPaymentAsync(
        ConfirmPaymentRequest request, CancellationToken ct = default)
    {
        var payment = await GetTrackedAsync(request.PaymentId, ct);

        VerifyRazorpaySignature(
            request.RazorpayOrderId,
            request.RazorpayPaymentId,
            request.RazorpaySignature);

        payment.MarkPaid(
            razorpayOrderId:   request.RazorpayOrderId,
            razorpayPaymentId: request.RazorpayPaymentId,
            transactionId:     request.TransactionId);

        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Payment confirmed. PaymentId={PaymentId}, RazorpayPaymentId={RpId}",
            payment.PaymentId, request.RazorpayPaymentId);

        return ToDto(payment);
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<PaymentDto?> GetPaymentByAppointmentAsync(int appointmentId, CancellationToken ct = default)
    {
        var p = await _repo.FindByAppointmentIdAsync(appointmentId, ct);
        return p is null ? null : ToDto(p);
    }

    public async Task<IReadOnlyList<PaymentDto>> GetPaymentsByPatientAsync(Guid patientId, CancellationToken ct = default)
    {
        var list = await _repo.FindByPatientIdAsync(patientId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<PaymentDto>> GetPaymentHistoryAsync(CancellationToken ct = default)
    {
        var list = await _repo.FindByPaidAtBetweenAsync(DateTime.MinValue, DateTime.UtcNow, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<string> GetPaymentStatusAsync(int paymentId, CancellationToken ct = default)
    {
        var p = await _repo.GetByIdAsync(paymentId, ct)
            ?? throw new KeyNotFoundException($"Payment with id {paymentId} not found.");
        return p.GetStatus();
    }

    public async Task<PaymentDto> UpdatePaymentStatusAsync(
        int paymentId, string status, CancellationToken ct = default)
    {
        var p = await GetTrackedAsync(paymentId, ct);
        p.SetStatus(status);
        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Payment status updated. PaymentId={PaymentId}, NewStatus={Status}", paymentId, status);

        return ToDto(p);
    }

    // ── Invoice ───────────────────────────────────────────────────────────────

    public async Task<InvoiceDto> GenerateInvoiceAsync(int paymentId, CancellationToken ct = default)
    {
        var p = await _repo.GetByIdAsync(paymentId, ct)
            ?? throw new KeyNotFoundException($"Payment with id {paymentId} not found.");

        if (p.Status != Entities.Payment.StatusPaid)
            throw new InvalidOperationException(
                $"Invoice can only be generated for Paid payments. Current status: {p.Status}");

        return new InvoiceDto(
            PaymentId:     p.PaymentId,
            AppointmentId: p.AppointmentId,
            PatientId:     p.PatientId,
            ProviderId:    p.ProviderId,
            Amount:        p.Amount,
            Currency:      p.Currency,
            Mode:          p.Mode,
            TransactionId: p.TransactionId,
            PaidAt:        p.PaidAt!.Value,
            InvoiceNumber: $"INV-{p.AppointmentId:D6}-{p.PaymentId:D6}");
    }

    // ── Revenue ───────────────────────────────────────────────────────────────

    public async Task<TotalRevenueDto> GetTotalRevenueAsync(Guid providerId, CancellationToken ct = default)
    {
        var total = await _repo.SumAmountByProviderIdAsync(providerId, ct);
        return new TotalRevenueDto(providerId, total);
    }

    // ── Razorpay helpers ──────────────────────────────────────────────────────

    private RazorpayOrderResponse CreateRazorpayOrder(Entities.Payment payment)
    {
        var amountInPaise = (long)(payment.Amount * 100);
        var client        = new RazorpayClient(_razorpay.KeyId, _razorpay.KeySecret);

        var options = new Dictionary<string, object>
        {
            { "amount",   amountInPaise },
            { "currency", payment.Currency },
            { "receipt",  $"rcpt_{payment.PaymentId}" },
            { "notes",    new Dictionary<string, string>
                          {
                              { "appointment_id", payment.AppointmentId.ToString() },
                              { "patient_id",     payment.PatientId.ToString()     }
                          }
            }
        };

        Order order;
        try
        {
            order = client.Order.Create(options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Razorpay order creation failed for PaymentId={PaymentId}", payment.PaymentId);
            throw new InvalidOperationException("Payment gateway error. Please try again.", ex);
        }

        var orderId = order["id"].ToString()!;

        _logger.LogInformation(
            "Razorpay order created. PaymentId={PaymentId}, OrderId={OrderId}",
            (object)payment.PaymentId,
            (object)orderId);

        return new RazorpayOrderResponse(
            PaymentId:       payment.PaymentId,
            RazorpayOrderId: orderId,
            AmountInPaise:   amountInPaise,
            Currency:        payment.Currency,
            KeyId:           _razorpay.KeyId);
    }

    /// <summary>
    /// Validates the Razorpay payment signature returned by the frontend.
    /// Expected = HMAC-SHA256( orderId + "|" + paymentId, KeySecret )
    /// </summary>
    private void VerifyRazorpaySignature(string orderId, string paymentId, string signature)
    {
        var payload  = $"{orderId}|{paymentId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_razorpay.KeySecret));
        var hash     = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computed = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        if (!computed.Equals(signature, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Razorpay signature mismatch. OrderId={OrderId}, PaymentId={PaymentId}",
                orderId, paymentId);
            throw new ArgumentException("Invalid Razorpay payment signature. Verification failed.");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<Entities.Payment> GetTrackedAsync(int paymentId, CancellationToken ct)
        => await _repo.GetTrackedByIdAsync(paymentId, ct)
            ?? throw new KeyNotFoundException($"Payment with id {paymentId} not found.");

    private static PaymentDto ToDto(Entities.Payment p) => new(
        PaymentId:         p.PaymentId,
        AppointmentId:     p.AppointmentId,
        PatientId:         p.PatientId,
        ProviderId:        p.ProviderId,
        Amount:            p.Amount,
        Status:            p.Status,
        Mode:              p.Mode,
        TransactionId:     p.TransactionId,
        Currency:          p.Currency,
        RazorpayOrderId:   p.RazorpayOrderId,
        RazorpayPaymentId: p.RazorpayPaymentId,
        PaidAt:            p.PaidAt,
        Notes:             p.Notes);
}
