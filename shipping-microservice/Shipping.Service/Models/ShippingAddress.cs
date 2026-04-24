namespace Shipping.Service.Models;

public record ShippingAddress(
    string Recipient,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string PostalCode,
    string Country);
