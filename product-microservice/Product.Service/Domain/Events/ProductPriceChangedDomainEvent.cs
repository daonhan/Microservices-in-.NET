namespace Product.Service.Domain.Events;

internal sealed record ProductPriceChangedDomainEvent(int ProductId, decimal NewPrice) : IDomainEvent;
