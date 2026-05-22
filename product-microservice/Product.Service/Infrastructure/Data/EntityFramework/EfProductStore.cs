using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Product.Service.Domain;
using Product.Service.Domain.Abstractions;
using Product.Service.Infrastructure.Outbox;

namespace Product.Service.Infrastructure.Data.EntityFramework;

internal sealed class EfProductStore : IProductStore
{
    private readonly ProductContext _context;
    private readonly DomainEventOutboxInterceptor _outboxInterceptor;

    public EfProductStore(ProductContext context, DomainEventOutboxInterceptor outboxInterceptor)
    {
        _context = context;
        _outboxInterceptor = outboxInterceptor;
    }

    public async Task<Domain.Product?> GetById(int id)
    {
        return await _context.Products
            .Include(p => p.ProductType)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public Task CreateProduct(Domain.Product product)
    {
        _context.Products.Add(product);
        return SaveAndPublishAsync();
    }

    public Task UpdateProduct(Domain.Product product) => SaveAndPublishAsync();

    private async Task SaveAndPublishAsync()
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            var domainEvents = _context.ChangeTracker.Entries<Entity>()
                .SelectMany(e => e.Entity.DequeueDomainEvents())
                .ToList();

            await _context.SaveChangesAsync(acceptAllChangesOnSuccess: false);

            await _outboxInterceptor.PublishAsync(domainEvents);

            _context.ChangeTracker.AcceptAllChanges();
            scope.Complete();
        });
    }
}
