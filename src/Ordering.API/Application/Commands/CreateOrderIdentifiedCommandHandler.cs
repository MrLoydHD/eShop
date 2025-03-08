using eShop.Ordering.API.Application.Commands;
using eShop.Ordering.API.Extensions;
using Microsoft.Extensions.Logging;

namespace eShop.Ordering.API.Application.Commands
{
    /// <summary>
    /// Concrete implementation of IdentifiedCommandHandler for CreateOrderCommand
    /// </summary>
    public class CreateOrderIdentifiedCommandHandler : IdentifiedCommandHandler<CreateOrderCommand, bool>
    {
        public CreateOrderIdentifiedCommandHandler(
            IMediator mediator,
            IRequestManager requestManager,
            ILogger<IdentifiedCommandHandler<CreateOrderCommand, bool>> logger)
            : base(mediator, requestManager, logger)
        {
        }

        protected override bool CreateResultForDuplicateRequest()
        {
            return true; // Idempotent operation - return success for duplicate requests
        }
    }
}
