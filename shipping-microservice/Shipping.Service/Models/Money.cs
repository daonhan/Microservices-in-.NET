namespace Shipping.Service.Models;

public record Money(decimal Amount, string Currency)
{
    public static Money Usd(decimal amount) => new(amount, "USD");
}
