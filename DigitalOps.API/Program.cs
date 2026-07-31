using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DigitalOps")
    ?? throw new InvalidOperationException(
        "Connection string 'DigitalOps' is required. Configure ConnectionStrings__DigitalOps outside source control.");

builder.Services.AddProblemDetails();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddDbContext<DigitalOpsDbContext>(options => options
    .UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable("__digitalops_ef_migrations_history", "public"))
    .UseSnakeCaseNamingConvention());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

public partial class Program;
