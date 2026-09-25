using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockpile.Domain.Entities;

namespace Stockpile.Infrastructure.Persistence.Configurations;

public sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.ToTable("stock_transfers", t =>
        {
            t.HasCheckConstraint(
                "transfer_three_distinct_warehouses",
                "from_warehouse_id <> to_warehouse_id "
                + "AND in_transit_warehouse_id <> from_warehouse_id "
                + "AND in_transit_warehouse_id <> to_warehouse_id");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Number).HasMaxLength(30).IsRequired();
        builder.HasIndex(t => t.Number).IsUnique();
        builder.Property(t => t.Status).HasConversion<int>().IsRequired();
        builder.Property(t => t.RowVersion)
            .HasColumnName("xmin").HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate().IsRowVersion();

        // Composition: lines have no independent lifetime.
        builder.HasMany(t => t.Lines)
            .WithOne()
            .HasForeignKey(l => l.StockTransferId)
            .OnDelete(DeleteBehavior.Cascade);

        // See SalesOrderConfiguration: required for the private backing field.
        builder.Navigation(t => t.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(t => new { t.FromWarehouseId, t.Status });
        builder.HasIndex(t => new { t.ToWarehouseId, t.Status });
        builder.Ignore(t => t.DomainEvents);

        // Three FKs to one table: each is its own relationship with its own constraint.
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(t => t.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(t => t.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(t => t.InTransitWarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> builder)
    {
        builder.ToTable("stock_transfer_lines", t =>
            t.HasCheckConstraint("transfer_line_quantity_positive", "quantity > 0"));

        builder.HasKey(l => l.Id);
        builder.HasIndex(l => new { l.StockTransferId, l.ProductId }).IsUnique();
        builder.Ignore(l => l.DomainEvents);

        builder.HasOne<Product>().WithMany()
            .HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
