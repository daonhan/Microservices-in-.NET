namespace Shipping.Service.ApiModels;

public record FailShipmentRequest(string Reason);

public record ReturnShipmentRequest(string Reason);
