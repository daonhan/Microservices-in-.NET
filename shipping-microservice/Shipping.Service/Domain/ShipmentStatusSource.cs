namespace Shipping.Service.Domain;

public enum ShipmentStatusSource
{
    System = 0,
    Admin = 1,
    CarrierPoll = 2,
    CarrierWebhook = 3,
}
