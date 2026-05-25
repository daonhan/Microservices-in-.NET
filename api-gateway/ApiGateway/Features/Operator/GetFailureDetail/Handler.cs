using ECommerce.Shared.Infrastructure.DeadLetter;

namespace ApiGateway.Features.Operator.GetFailureDetail;

internal sealed class GetFailureDetailHandler
{
    private readonly IDeadLetterStore _store;
    private readonly IConfiguration _configuration;

    public GetFailureDetailHandler(IDeadLetterStore store, IConfiguration configuration)
    {
        _store = store;
        _configuration = configuration;
    }

    public async Task<IResult> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var message = await _store.GetAsync(id, cancellationToken);
        if (message is null)
        {
            return Results.NotFound();
        }

        var traceUiBaseUrl = _configuration["Operator:TraceUiBaseUrl"];
        string? traceUrl = null;
        if (message.CorrelationId.HasValue && !string.IsNullOrWhiteSpace(traceUiBaseUrl))
        {
            traceUrl = traceUiBaseUrl.TrimEnd('/') + "/" + message.CorrelationId.Value;
        }

        return Results.Ok(new DeadLetterDetailResponse(message, traceUrl));
    }
}
