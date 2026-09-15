using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockpile.Domain.Entities;
using Stockpile.Domain.ValueObjects;

namespace Stockpile.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku)
            .HasConversion(
                sku => sku.Value,
                raw => Sku.Create(raw).Value)      // already validated on the way in
            .HasMaxLength(Sku.MaxLength)
            .IsRequired();

        builder.HasIndex(p => p.Sku).IsUnique();

        builder.Property(p => p.Name).HasMaxLength(Product.MaxNameLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Category).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Barcode).HasMaxLength(64);

        // Fast scanner lookup (§8 GET /api/products/by-barcode/{barcode}).
        builder.HasIndex(p => p.Barcode).HasFilter("barcode IS NOT NULL");
        builder.HasIndex(p => p.Category);

        // Map the CLR property onto Postgres's system xmin column. All four calls are
        // load-bearing: without ValueGeneratedOnAddOrUpdate + the explicit xid type,
        // EF treats RowVersion as an ordinary property and the migration emits a real
        // `xmin` column in CreateTable — which then shadows the system column and the
        // token never changes. Verify this in task 9 step 7.
        builder.Property(p => p.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.Ignore(p => p.DomainEvents);
    }
}
