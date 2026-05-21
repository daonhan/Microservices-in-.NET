namespace Product.Service.Domain.Events;

// Holds the aggregate, not a scalar Id: a new product's Id is database-generated
// and is not known until the aggregate has been persisted.
internal sealed record ProductCreatedDomainEvent(Product Product) : IDomainEvent;
