using Microsoft.Extensions.Hosting;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

public interface IDeadLetterCapture : IHostedService
{
}
