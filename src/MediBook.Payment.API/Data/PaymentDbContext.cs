using MediBook.Payment.API.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediBook.Payment.API.Data;

public sealed class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    public DbSet<Entities.Payment> Payments => Set<Entities.Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Entities.Payment>(entity =>
        {
            entity.ToTable("payments");

            entity.HasKey(p => p.PaymentId);

            entity.Property(p => p.PaymentId)
                  .HasColumnName("payment_id")
                  .UseIdentityAlwaysColumn();

            entity.Property(p => p.AppointmentId)
                  .HasColumnName("appointment_id")
                  .IsRequired();

            entity.Property(p => p.PatientId)
                  .HasColumnName("patient_id")
                  .HasColumnType("uuid")
                  .IsRequired();

            entity.Property(p => p.ProviderId)
                  .HasColumnName("provider_id")
                  .HasColumnType("uuid")
                  .IsRequired();

            entity.Property(p => p.Amount)
                  .HasColumnName("amount")
                  .HasColumnType("numeric(12,2)")
                  .IsRequired();

            entity.Property(p => p.Status)
                  .HasColumnName("status")
                  .HasMaxLength(20)
                  .HasDefaultValue(Entities.Payment.StatusPending)
                  .IsRequired();

            entity.Property(p => p.Mode)
                  .HasColumnName("mode")
                  .HasMaxLength(20)
                  .IsRequired();

            entity.Property(p => p.TransactionId)
                  .HasColumnName("transaction_id")
                  .HasMaxLength(200)
                  .HasDefaultValue(string.Empty);

            entity.Property(p => p.Currency)
                  .HasColumnName("currency")
                  .HasMaxLength(10)
                  .HasDefaultValue("INR")
                  .IsRequired();

            entity.Property(p => p.RazorpayOrderId)
                  .HasColumnName("razorpay_order_id")
                  .HasMaxLength(200)
                  .HasDefaultValue(string.Empty);

            entity.Property(p => p.RazorpayPaymentId)
                  .HasColumnName("razorpay_payment_id")
                  .HasMaxLength(200)
                  .HasDefaultValue(string.Empty);

            entity.Property(p => p.PaidAt)
                  .HasColumnName("paid_at")
                  .HasColumnType("timestamp with time zone");

            entity.Property(p => p.Notes)
                  .HasColumnName("notes")
                  .HasMaxLength(1000)
                  .HasDefaultValue(string.Empty);

            // ── Indexes ───────────────────────────────────────────────────────

            // One payment record per appointment (unique constraint)
            entity.HasIndex(p => p.AppointmentId)
                  .IsUnique()
                  .HasDatabaseName("ix_payments_appointment_id");

            entity.HasIndex(p => p.PatientId)
                  .HasDatabaseName("ix_payments_patient_id");

            entity.HasIndex(p => p.ProviderId)
                  .HasDatabaseName("ix_payments_provider_id");

            entity.HasIndex(p => p.Status)
                  .HasDatabaseName("ix_payments_status");

            entity.HasIndex(p => p.TransactionId)
                  .HasDatabaseName("ix_payments_transaction_id");

            entity.HasIndex(p => p.PaidAt)
                  .HasDatabaseName("ix_payments_paid_at");
        });
    }
}
