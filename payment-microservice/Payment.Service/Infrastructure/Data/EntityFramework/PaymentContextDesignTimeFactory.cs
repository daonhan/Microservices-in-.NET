using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal class PaymentContextDesignTimeFactory : IDesignTimeDbContextFactory<PaymentContext>
{
    public PaymentContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PaymentContext>();
        optionsBuilder.UseSqlServer("Server=localhost,1433;Database=Payment;User Id=sa;Password=micR0S3rvice$;TrustServerCertificate=True");

        return new PaymentContext(optionsBuilder.Options);
    }
}
