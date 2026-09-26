using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Stockpile.Infrastructure.Persistence.Configurations;

public sealed class ProcessedRequestConfiguration : IEntityTypeConfiguration<ProcessedRequest>
{
    public void Configure(EntityTypeBuilder<ProcessedRequest> builder)
    {
        builder.ToTable("processed_requests");

        // Named so UnitOfWork's replay detection, which matches "idempotency_key" in the
        // violated constraint, treats a lost race here exactly like one in stock_movements.
        builder.HasKey(r => r.IdempotencyKey).HasName("pk_processed_requests_idempotency_key");

        builder.Property(r => r.IdempotencyKey).HasMaxLength(100);
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.ProcessedAt).IsRequired();
    }
}
