using System.Reflection;
using eShop.Ordering.API.Application.Commands;
using eShop.Ordering.API.Application.Behaviors;
using eShop.Ordering.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Ordering.API.Extensions
{
    public static class ApplicationServiceExtensions
    {
        public static WebApplicationBuilder AddApplicationServices(this WebApplicationBuilder builder)
        {
            // Register MediatR
            builder.Services.AddMediatR(cfg => {
                cfg.RegisterServicesFromAssembly(typeof(CreateOrderCommandHandler).Assembly);
                
                // Register behaviors
                cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
                cfg.AddOpenBehavior(typeof(ValidatorBehavior<,>));
                cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            });
            
            // Register domain event handlers
            builder.Services.AddScoped<IOrderRepository, OrderRepository>();
            
            // Register infrastructure services
            builder.Services.AddScoped<IRequestManager, RequestManager>();
            
            // Register command handlers
            builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, bool>, CreateOrderCommandHandler>();
            
            // Register command handlers for identified commands
            builder.Services.AddScoped<IRequestHandler<IdentifiedCommand<CreateOrderCommand, bool>, bool>, 
                CreateOrderIdentifiedCommandHandler>();
            
            // Other application services
            builder.Services.AddScoped<IOrderQueries, OrderQueries>();
            
            return builder;
        }
    }
}
