using EnterpriseAgent.Api.AI;
using EnterpriseAgent.Api.Agents;
using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Tools;
using EnterpriseAgent.Api.Rag;
using EnterpriseAgent.Api.Security;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.AddHttpClient<GeminiAIClient>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue<int>("Gemini:RequestTimeoutSeconds", 60));
});
builder.Services.AddScoped<IAIClient>(services => services.GetRequiredService<GeminiAIClient>());
builder.Services.AddSingleton<CustomerDataRepository>();
builder.Services.AddSingleton<ChatHistoryService>();
builder.Services.AddSingleton<AuthorizationService>();
builder.Services.AddSingleton<VerificationReviewRequestService>();
builder.Services.AddScoped<ICustomerTool, GetCustomerTool>();
builder.Services.AddScoped<ICustomerTool, GetVerificationStatusTool>();
builder.Services.AddScoped<ICustomerTool, GetOrderEligibilityTool>();
builder.Services.AddScoped<ICustomerTool, CreateVerificationReviewRequestTool>();
builder.Services.AddSingleton<DocumentLoader>();
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddScoped<EmbeddingService>();
builder.Services.AddSingleton<VectorStore>();
builder.Services.AddScoped<RagService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Enterprise Agent API",
        Version = "v1",
        Description = "Enterprise AI Agent demonstration API for the FDP session."
    });
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Enterprise Agent API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseCors();
app.MapControllers();

app.Logger.LogInformation("EnterpriseAgent.Api started successfully.");

app.Run();

public partial class Program;
