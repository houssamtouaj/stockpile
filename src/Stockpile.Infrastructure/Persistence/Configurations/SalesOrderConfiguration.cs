using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockpile.Domain.Entities;

namespace Stockpile.Infrastructure.Persistence.Configurations;

public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("sales_orders");
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
            .HasForeignKey(l => l.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Lines is an IReadOnlyList over a private backing field. Without field access EF
        // cannot materialise the collection, and the failure surfaces much later as an
        // empty Lines on a loaded order.
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(o => new { o.WarehouseId, o.Status });   // the order board query
        builder.Ignore(o => o.DomainEvents);

        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(o => o.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.ToTable("sales_order_lines", t =>
        {
            t.HasCheckConstraint("so_line_quantity_positive", "quantity_ordered > 0");
            t.HasCheckConstraint(
                "so_line_picked_within_ordered",
                "quantity_picked >= 0 AND quantity_picked <= quantity_ordered");
        });

        builder.HasKey(l => l.Id);

        // The aggregate refuses a second line for the same product; this is the backstop.
        builder.HasIndex(l => new { l.SalesOrderId, l.ProductId }).IsUnique();

        builder.Ignore(l => l.IsFullyPicked);
        builder.Ignore(l => l.DomainEvents);

        builder.HasOne<Product>().WithMany()
            .HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
