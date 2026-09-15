using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockpile.Domain.Entities;

namespace Stockpile.Infrastructure.Persistence.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", t =>
        {
            // A reservation movement must not move on-hand, and vice versa.
            t.HasCheckConstraint(
                "movement_deltas_not_both_zero",
                "on_hand_delta <> 0 OR reserved_delta <> 0");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type).HasConversion<int>().IsRequired();
        builder.Property(m => m.ReferenceType).HasMaxLength(50).IsRequired();
        builder.Property(m => m.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Reason).HasMaxLength(500);

        // Race-safe idempotency: the unique index is the guarantee, the pre-check is
        // only a fast path. Two concurrent retries both pass a pre-check; only one
        // survives this index (phase 02 task 5).
        builder.HasIndex(m => m.IdempotencyKey).IsUnique();

        // The movements endpoint is cursor-paginated by (occurred_at, id) descending.
        builder.HasIndex(m => new { m.ProductId, m.OccurredAt, m.Id });
        builder.HasIndex(m => new { m.WarehouseId, m.OccurredAt });
        builder.HasIndex(m => new { m.ReferenceType, m.ReferenceId });

        builder.Ignore(m => m.DomainEvents);

        builder.HasOne<Product>().WithMany()
            .HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}
