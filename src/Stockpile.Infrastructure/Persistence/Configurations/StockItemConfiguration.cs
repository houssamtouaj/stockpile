using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockpile.Domain.Entities;

namespace Stockpile.Infrastructure.Persistence.Configurations;

public sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items", t =>
        {
            // Layer 1 of the concurrency design (§6): a backstop that should never fire.
            // If it does, there is a logic bug, not a concurrency event.
            t.HasCheckConstraint(
                "stock_never_negative",
                "quantity_on_hand >= 0 AND quantity_reserved >= 0 "
                + "AND quantity_reserved <= quantity_on_hand");
        });

        builder.HasKey(s => s.Id);

        builder.HasIndex(s => new { s.ProductId, s.WarehouseId }).IsUnique();
        builder.HasIndex(s => s.WarehouseId);

        builder.Property(s => s.BinLocation).HasMaxLength(50);

        // Derived; never a column. Reconstructed as on_hand - reserved in SQL.
        builder.Ignore(s => s.QuantityAvailable);
        builder.Ignore(s => s.DomainEvents);

        builder.HasOne<Product>().WithMany()
            .HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        // NO concurrency token here, on purpose (§5, §6). Do not add one.
    }
}
