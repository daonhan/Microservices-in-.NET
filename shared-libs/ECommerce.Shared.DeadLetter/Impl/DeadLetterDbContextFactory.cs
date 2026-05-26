using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

public sealed class DeadLetterDbContextFactory : IDesignTimeDbContextFactory<DeadLetterDbContext>
{
    public DeadLetterDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DeadLetterDbContext>()
            .UseSqlServer("Server=localhost;Database=DeadLetterDesignTime;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new DeadLetterDbContext(options);
    }
}
