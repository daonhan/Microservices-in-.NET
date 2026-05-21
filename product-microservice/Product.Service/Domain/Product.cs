using Product.Service.Domain.Events;

namespace Product.Service.Domain;

internal class Product : Entity
{
    private Product() { }

    public Product(string name, decimal price, int productTypeId, string? description = null)
    {
        Name = name;
        Price = price;
        ProductTypeId = productTypeId;
        Description = description;
        Raise(new ProductCreatedDomainEvent(this));
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public decimal Price { get; private set; }

    public int ProductTypeId { get; private set; }

    public ProductType? ProductType { get; private set; }

    public void Rename(string name) => Name = name;

    public void ChangeDescription(string? description) => Description = description;

    public void ChangeType(int productTypeId) => ProductTypeId = productTypeId;

    public void ChangePrice(decimal newPrice)
    {
        if (newPrice == Price)
        {
            return;
        }

        Price = newPrice;
        Raise(new ProductPriceChangedDomainEvent(Id, newPrice));
    }
}
