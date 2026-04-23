# MediBook — Payment Service

**Namespace:** `MediBook.Payment`  
**Framework:** .NET 8 / ASP.NET Core 8 Web API  
**Database:** PostgreSQL (via Npgsql EF Core 8)  
**Payment Gateway:** Razorpay .NET SDK v3

---

## Overview

The Payment Service handles the full payment lifecycle for MediBook appointments. It creates Razorpay orders for online payments, verifies gateway signatures after frontend checkout, records payment status transitions, generates invoices for completed payments, and exposes provider revenue summaries.

**No automated refund flow is included by design.** Refunds are an admin-triggered status update (`PUT /api/v1/payments/{id}/status` with `"Refunded"`).

---

## Architecture

Follows the same layered microservices pattern as all other MediBook services:

```
Entity (POCO)  →  IRepository / Repository  →  IService / Service  →  [ApiController]
```

| Layer | Class |
|---|---|
| Entity | `Payment` |
| Repository Interface | `IPaymentRepository` |
| Repository Impl | `PaymentRepository` |
| Service Interface | `IPaymentService` |
| Service Impl | `PaymentService` |
| Controller | `PaymentController` |

---

## Payment Flow

### Online Payment (Card / UPI / Wallet)

```
Client                    Payment Service              Razorpay
  │                            │                           │
  ├─ POST /payments/process ──►│                           │
  │                            ├─ Create Payment (Pending) │
  │                            ├─ Create Razorpay Order ──►│
  │                            │◄── orderId, amount ───────│
  │◄── PaymentDto +            │                           │
  │    RazorpayOrderResponse ──│                           │
  │                            │                           │
  ├─ [Razorpay Checkout Widget renders in browser]         │
  │                            │                           │
  ├─ POST /payments/confirm ──►│                           │
  │   (orderId, paymentId,     │                           │
  │    signature, txnId)       ├─ Verify HMAC-SHA256 sig   │
  │                            ├─ payment.MarkPaid(...)    │
  │◄── PaymentDto (Paid) ──────│                           │
```

### Cash Payment

```
Client                    Payment Service
  │                            │
  ├─ POST /payments/process ──►│
  │   (mode: "Cash")           ├─ Create Payment (Pending)
  │◄── PaymentDto (Pending) ───│
  │                            │
  │  [Admin/Provider manually  │
  │   confirms cash at clinic] │
  │                            │
  ├─ PUT /payments/{id}/status►│  (Admin only)
  │   (status: "Paid")         ├─ payment.SetStatus("Paid")
  │◄── PaymentDto (Paid) ──────│
```

---

## API Endpoints

All routes are under `/api/v1/payments`. JWT Bearer token required on all endpoints.

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/process` | Any | Initiate payment — creates Razorpay order for online, records Pending for Cash |
| `POST` | `/confirm` | Any | Verify Razorpay signature and mark Paid |
| `GET`  | `/appointment/{appointmentId}` | Any | Get payment for an appointment |
| `GET`  | `/patient/{patientId}` | Any | Get all payments for a patient |
| `GET`  | `/history` | Admin | Get all payments platform-wide |
| `GET`  | `/{paymentId}/status` | Any | Get payment status string |
| `PUT`  | `/{paymentId}/status` | Admin | Override payment status |
| `GET`  | `/{paymentId}/invoice` | Any | Generate invoice for a Paid payment |
| `GET`  | `/revenue/{providerId}` | Provider, Admin | Total paid revenue for a provider |

---

## Configuration

### `appsettings.json`

```json
{
  "ConnectionStrings": {
    "PaymentDb": "Host=...;Database=medibook_payment;Username=...;Password=..."
  },
  "JwtSettings": {
    "SecretKey": "MUST_MATCH_AUTH_SERVICE_SECRET_32_CHARS_MIN",
    "Issuer":    "MediBook.Auth",
    "Audience":  "MediBook.Client"
  },
  "Razorpay": {
    "KeyId":     "rzp_test_XXXXXXXXXXXX",
    "KeySecret": "your_razorpay_secret"
  }
}
```

> **Important:** `JwtSettings.SecretKey`, `Issuer`, and `Audience` must match exactly what the `auth-service` uses to issue tokens.

### Razorpay Keys

1. Log in to [Razorpay Dashboard](https://dashboard.razorpay.com)
2. Go to **Settings → API Keys → Generate Test Key**
3. Copy `Key Id` → `Razorpay:KeyId`
4. Copy `Key Secret` → `Razorpay:KeySecret`

For production use live keys and store secrets in environment variables or a secrets manager — never commit them to source control.

---

## Running Locally

```bash
# From payment-service/
dotnet restore src/MediBook.Payment.API/MediBook.Payment.API.csproj
dotnet run --project src/MediBook.Payment.API

# Swagger UI available at:
# http://localhost:5005
```

Migrations are applied automatically on startup via `db.Database.Migrate()`.

---

## Running with Docker

```bash
# From payment-service/
docker build -t medibook-payment .
docker run -p 5005:8080 \
  -e ConnectionStrings__PaymentDb="Host=host.docker.internal;..." \
  -e JwtSettings__SecretKey="..." \
  -e Razorpay__KeyId="rzp_test_..." \
  -e Razorpay__KeySecret="..." \
  medibook-payment
```

---

## Project Structure

```
payment-service/
├── Dockerfile
├── MediBook.Payment.sln
└── src/
    └── MediBook.Payment.API/
        ├── Controllers/
        │   └── PaymentController.cs        ← 9 REST endpoints
        ├── Data/
        │   └── PaymentDbContext.cs          ← EF Core fluent config
        ├── DTOs/
        │   └── PaymentDtos.cs               ← Request / response records
        ├── Entities/
        │   └── Payment.cs                   ← Domain aggregate + state machine
        ├── Extensions/
        │   └── ServiceCollectionExtensions.cs ← DI wiring
        ├── Helpers/
        │   └── Settings.cs                  ← JwtSettings + RazorpaySettings
        ├── Middleware/
        │   └── GlobalExceptionMiddleware.cs
        ├── Migrations/                       ← EF Core migrations
        ├── Properties/
        │   └── launchSettings.json
        ├── Repositories/
        │   ├── IPaymentRepository.cs
        │   └── PaymentRepository.cs
        ├── Services/
        │   ├── IPaymentService.cs
        │   └── PaymentService.cs            ← Razorpay integration + HMAC verification
        ├── Validators/
        │   └── PaymentValidators.cs         ← FluentValidation
        ├── Program.cs
        ├── appsettings.json
        └── MediBook.Payment.API.csproj
```

---

## Integrating with Appointment Service

The `appointment-service` currently uses a `PaymentClientStub`. To wire it up to this service:

1. In `appointment-service/appsettings.json` add:
   ```json
   "ServiceClients": {
     "PaymentServiceBaseUrl": "http://localhost:5005"
   }
   ```

2. Replace `PaymentClientStub` in `ServiceCollectionExtensions.cs` with a real typed `HttpClient` that calls `POST /api/v1/payments/process` on cancellation.

---

## Payment Status Reference

| Status | Meaning |
|--------|---------|
| `Pending` | Payment initiated, awaiting capture (online) or manual confirmation (cash) |
| `Paid` | Successfully captured via Razorpay or confirmed by admin |
| `Failed` | Gateway error or timeout during capture |
| `Refunded` | Admin has marked payment as refunded after cancellation |

## Payment Mode Reference

| Mode | Flow |
|------|------|
| `Card` | Razorpay order → frontend checkout → confirm endpoint |
| `UPI` | Razorpay order → frontend checkout → confirm endpoint |
| `Wallet` | Razorpay order → frontend checkout → confirm endpoint |
| `Cash` | Pending record created; admin updates to Paid at clinic |
"# Razorpay" 
