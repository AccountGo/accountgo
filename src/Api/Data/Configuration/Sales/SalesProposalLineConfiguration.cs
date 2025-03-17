using Core.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configuration.Sales
{
    public class SalesProposalLineConfiguration : IEntityTypeConfiguration<SalesProposalLine>
    {
        public void Configure(EntityTypeBuilder<SalesProposalLine> builder)
        {
            builder.Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)");

            builder.Property(p => p.Discount)
                .HasColumnType("decimal(18, 2)");

            builder.Property(p => p.Quantity)
                .HasColumnType("decimal(18, 2)");
        }
    }
}
