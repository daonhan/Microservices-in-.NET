using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using ECommerce.Shared.Qa;
using Product.Service.Endpoints;
using Product.Service.Features.CreateProduct;
using Product.Service.Features.GetProduct;
using Product.Service.Features.ListProducts;
using Product.Service.Features.UpdateProduct;
using Product.Service.Infrastructure.Data.EntityFramework;
using Product.Service.Infrastructure.Outbox;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddProductOutbox();

builder.Services.AddCreateProductSlice()
    .AddGetProductSlice()
    .AddListProductsSlice()
    .AddUpdateProductSlice();

builder.Services.AddPlatformEventBus(builder.Configuration)
    .AddPlatformEventPublisher(builder.Configuration);

builder.AddPlatformObservability("Product",
    customTracing: t => t.WithSqlInstrumentation());

builder.Services.AddPlatformHealthChecks()
    .AddSqlServerProbe(builder.Configuration.GetConnectionString("Default") ?? "")
    .AddRabbitMqProbe(builder.Configuration["RabbitMq:HostName"] ?? "localhost");

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRequireServicePolicy();

builder.AddPlatformOpenApi("product");

var app = builder.Build();

app.UsePrometheusExporter();
app.MapPlatformHealthChecks();
app.UsePlatformOpenApi();

if (QaSeedingExtensions.IsQaSeedingEnabled(app.Environment, app.Configuration))
{
    app.MigrateDatabase();
    app.ApplyOutboxMigrations();
}

app.SeedQaData();

app.MapCreateProduct();
app.MapGetProduct();
app.MapListProducts();
app.MapUpdateProduct();
app.RegisterInternalOutboxEndpoints();

app.UseHttpsRedirection();

app.UseJwtAuthentication();

app.Run();

public partial class Program { }
