using ECommerce.Shared.Infrastructure.DeadLetter.Models;

namespace ApiGateway.Features.Operator.GetFailureDetail;

public sealed record DeadLetterDetailResponse(DeadLetterMessage Message, string? TraceUrl);
