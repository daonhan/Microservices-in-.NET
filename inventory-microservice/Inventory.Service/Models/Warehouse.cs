namespace Inventory.Service.Models;

internal class Warehouse
{
    public int Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }
}
