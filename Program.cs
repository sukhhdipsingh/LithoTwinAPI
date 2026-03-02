using LithoTwinAPI.Data;
using LithoTwinAPI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase("LithoTwinDB"));
builder.Services.AddScoped<IManufacturingService, ManufacturingService>();
builder.Services.AddHostedService<ThermalDriftService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "LithoTwin API",
        Version = "v1",
        Description = "Telemetry, exposure simulation, and wafer routing for EUV lithography tools"
    });
});

// allow local frontend dev
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();