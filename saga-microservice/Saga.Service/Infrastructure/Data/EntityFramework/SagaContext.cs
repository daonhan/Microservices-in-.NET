using Microsoft.EntityFrameworkCore;

namespace Saga.Service.Infrastructure.Data.EntityFramework;

internal class SagaContext : DbContext
{
    public SagaContext(DbContextOptions<SagaContext> options)
        : base(options)
    {
    }
}
