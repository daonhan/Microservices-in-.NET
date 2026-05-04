using ECommerce.Shared.Authentication;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using ECommerce.Shared.Infrastructure.RabbitMq;

namespace ApiGateway.Operator;

public static class OperatorModule
{
    public const string OperatorPathPrefix = "/operator";

    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.Services.AddRabbitMqEventBus(builder.Configuration);
        builder.Services.AddDeadLetter(builder.Configuration);
        builder.Services.AddRequireOperatorPolicy();
    }

    public static void MapEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/operator/api/failures")
            .RequireAuthorization(AuthorizationPolicies.RequireOperatorPolicy);

        group.MapGet("/", async (
            IDeadLetterStore store,
            string? service,
            string? eventType,
            DeadLetterStatus? status,
            DateTime? from,
            DateTime? to,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default) =>
        {
            var filter = new DeadLetterFilter(service, eventType, status, from, to, page, pageSize);
            var result = await store.ListAsync(filter, cancellationToken);
            return Results.Ok(result);
        });
    }
}
