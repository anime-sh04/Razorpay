using MediBook.Payment.API.DTOs;
using MediBook.Payment.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBook.Payment.API.Controllers;

/// <summary>
/// Payment-Service REST API.
/// Base route: /api/v1/payments
/// </summary>
[ApiController]
[Route("api/v1/payments")]
// [Authorize]
public sealed class PaymentController : ControllerBase
{
    private readonly IPaymentService _payService;

    public PaymentController(IPaymentService payService) => _payService = payService;

    // ── POST /api/v1/payments/process ─────────────────────────────────────────
    /// <summary>
    /// Initiate a payment for an appointment.
    /// For online modes (Card/UPI/Wallet) returns a Razorpay order the frontend uses to render checkout.
    /// For Cash, records a Pending entry directly.
    /// </summary>
    [HttpPost("process")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Process(
        [FromBody] ProcessPaymentRequest request,
        CancellationToken ct)
    {
        var (payment, razorpayOrder) = await _payService.ProcessPaymentAsync(request, ct);

        var result = razorpayOrder is null
            ? (object)payment
            : new { payment, razorpayOrder };

        return CreatedAtAction(nameof(GetByAppointment),
            new { appointmentId = payment.AppointmentId }, result);
    }

    // ── POST /api/v1/payments/confirm ─────────────────────────────────────────
    /// <summary>
    /// Verify Razorpay signature and mark payment as Paid.
    /// Called by the frontend after the Razorpay checkout widget completes.
    /// </summary>
    [HttpPost("confirm")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmPaymentRequest request,
        CancellationToken ct)
    {
        var dto = await _payService.ConfirmPaymentAsync(request, ct);
        return Ok(dto);
    }

    // ── GET /api/v1/payments/appointment/{appointmentId} ─────────────────────
    /// <summary>Returns the payment linked to a specific appointment.</summary>
    [HttpGet("appointment/{appointmentId:int}")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByAppointment(int appointmentId, CancellationToken ct)
    {
        var dto = await _payService.GetPaymentByAppointmentAsync(appointmentId, ct);
        if (dto is null)
            return NotFound(new ApiErrorResponse($"No payment found for appointmentId {appointmentId}."));
        return Ok(dto);
    }

    // ── GET /api/v1/payments/patient/{patientId} ─────────────────────────────
    /// <summary>Returns all payments made by a patient.</summary>
    [HttpGet("patient/{patientId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByPatient(Guid patientId, CancellationToken ct)
    {
        var list = await _payService.GetPaymentsByPatientAsync(patientId, ct);
        return Ok(list);
    }

    // ── GET /api/v1/payments/history ─────────────────────────────────────────
    /// <summary>Returns all payment transactions across the platform. Admin only.</summary>
    [HttpGet("history")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(CancellationToken ct)
    {
        var list = await _payService.GetPaymentHistoryAsync(ct);
        return Ok(list);
    }

    // ── GET /api/v1/payments/{paymentId}/status ───────────────────────────────
    /// <summary>Returns just the status string for a payment.</summary>
    [HttpGet("{paymentId:int}/status")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(int paymentId, CancellationToken ct)
    {
        var status = await _payService.GetPaymentStatusAsync(paymentId, ct);
        return Ok(new { paymentId, status });
    }

    // ── PUT /api/v1/payments/{paymentId}/status ───────────────────────────────
    /// <summary>Admin override to update a payment's status.</summary>
    [HttpPut("{paymentId:int}/status")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        int paymentId,
        [FromBody] UpdateStatusRequest request,
        CancellationToken ct)
    {
        var dto = await _payService.UpdatePaymentStatusAsync(paymentId, request.Status, ct);
        return Ok(dto);
    }

    // ── GET /api/v1/payments/{paymentId}/invoice ──────────────────────────────
    /// <summary>Returns invoice details for a completed (Paid) payment.</summary>
    [HttpGet("{paymentId:int}/invoice")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateInvoice(int paymentId, CancellationToken ct)
    {
        var invoice = await _payService.GenerateInvoiceAsync(paymentId, ct);
        return Ok(invoice);
    }

    // ── GET /api/v1/payments/revenue/{providerId} ─────────────────────────────
    /// <summary>Returns total paid revenue for a provider.</summary>
    [HttpGet("revenue/{providerId:guid}")]
    [Authorize(Roles = "Provider,Admin")]
    [ProducesResponseType(typeof(TotalRevenueDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTotalRevenue(Guid providerId, CancellationToken ct)
    {
        var dto = await _payService.GetTotalRevenueAsync(providerId, ct);
        return Ok(dto);
    }
}

/// <summary>Request body for status override endpoint.</summary>
public sealed record UpdateStatusRequest(string Status);
