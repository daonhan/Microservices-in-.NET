using Microsoft.AspNetCore.Mvc;
using Shipping.Service.ApiModels;
using Shipping.Service.Infrastructure.Data;

namespace Shipping.Service.Endpoints;

public static class ShippingApiEndpoints
{
    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/by-order/{orderId:guid}", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            Guid orderId) =>
        {
            var shipments = await shipmentStore.GetByOrder(orderId);

            if (shipments.Count == 0)
            {
                return TypedResults.NotFound($"No shipments found for order {orderId}");
            }

            var response = shipments
                .Select(s => new ShipmentResponse(
                    s.Id,
                    s.OrderId,
                    s.CustomerId,
                    s.WarehouseId,
                    s.Status.ToString(),
                    s.CreatedAt,
                    s.Lines.Select(l => new ShipmentLineDto(l.ProductId, l.Quantity)).ToList()))
                .ToList();

            return TypedResults.Ok(response);
        }).RequireAuthorization("Administrator");
    }
}
