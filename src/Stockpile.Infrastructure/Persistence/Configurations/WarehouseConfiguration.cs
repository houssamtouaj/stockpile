using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockpile.Domain.Entities;

namespace Stockpile.Infrastructure.Persistence.Configurations;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(w => w.Code).IsUnique();
        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.Address).HasMaxLength(500);
        builder.Property(w => w.Kind).HasConversion<int>().IsRequired();

        builder.Ignore(w => w.DomainEvents);
    }
}
