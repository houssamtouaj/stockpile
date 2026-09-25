using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockpile.Domain.Entities;

namespace Stockpile.Infrastructure.Persistence.Configurations;

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Number).HasMaxLength(30).IsRequired();
        builder.HasIndex(o => o.Number).IsUnique();
        builder.Property(o => o.Status).HasConversion<int>().IsRequired();
        builder.Property(o => o.RowVersion)
            .HasColumnName("xmin").HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate().IsRowVersion();

        // Composition: lines have no independent lifetime.
        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(l => l.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // See SalesOrderConfiguration: required for the private backing field.
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(o => new { o.WarehouseId, o.Status });
        builder.HasIndex(o => o.SupplierId);
        builder.Ignore(o => o.DomainEvents);

        builder.HasOne<Supplier>().WithMany()
            .HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(o => o.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("purchase_order_lines", t =>
        {
            t.HasCheckConstraint("po_line_quantity_positive", "quantity_ordered > 0");
            t.HasCheckConstraint(
                "po_line_received_within_ordered",
                "quantity_received >= 0 AND quantity_received <= quantity_ordered");
            t.HasCheckConstraint("po_line_cost_not_negative", "unit_cost_cents >= 0");
        });

        builder.HasKey(l => l.Id);
        builder.HasIndex(l => new { l.PurchaseOrderId, l.ProductId }).IsUnique();

        builder.Ignore(l => l.QuantityOutstanding);
        builder.Ignore(l => l.IsFullyReceived);
        builder.Ignore(l => l.DomainEvents);

        builder.HasOne<Product>().WithMany()
            .HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
