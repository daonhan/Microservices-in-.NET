using ECommerce.Shared.Infrastructure.RabbitMq;
using Order.Service.Endpoints;
using Order.Service.Infrastructure.Data.EntityFramework;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRabbitMqEventBus(builder.Configuration)
    .AddRabbitMqEventPublisher();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MigrateDatabase();
}

app.UseSwagger();
app.UseSwaggerUI();

app.RegisterEndpoints();

app.UseHttpsRedirection();

app.Run();

public partial class Program { }
