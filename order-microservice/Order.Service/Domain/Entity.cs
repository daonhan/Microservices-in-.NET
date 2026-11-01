using System.ComponentModel.DataAnnotations.Schema;
using Order.Service.Domain.Events;

namespace Order.Service.Domain;

internal abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    [NotMapped]
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void RemoveDomainEvents(IEnumerable<IDomainEvent> events)
    {
        foreach (var domainEvent in events)
        {
            // Remove only the captured instance, preserving events raised later.
            var index = _domainEvents.FindIndex(candidate => ReferenceEquals(candidate, domainEvent));
            if (index >= 0)
            {
                _domainEvents.RemoveAt(index);
            }
        }
    }

    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }
}
