using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using EFCore.NamingConventions;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Azure.Storage.Blobs;
using ChessApp.Backend.Data;
using ChessApp.Backend.Services;
using ChessApp.Backend.Validators;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// =====================================================
// LOGGING
// =====================================================

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Information()
    .CreateLogger();

builder.Host.UseSerilog();

// =====================================================
// DATABASE
// =====================================================

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not configured");

// Enable NTS (NetTopologySuite) for geospatial queries
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        x => x.UseNetTopologySuite()
    ).UseSnakeCaseNamingConvention()
);

// =====================================================
// AUTHENTICATION
// =====================================================

var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["SecretKey"]
    ?? throw new InvalidOperationException("JWT SecretKey not configured");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // Support JWT in WebSocket queries
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Query.TryGetValue("access_token", out var token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

// =====================================================
// AUTHORIZATION
// =====================================================

builder.Services.AddAuthorization();

// =====================================================
// DEPENDENCY INJECTION
// =====================================================

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProposalService, ProposalService>();
builder.Services.AddScoped<IMatchService, MatchService>();
builder.Services.AddScoped<IBlockService, BlockService>();
builder.Services.AddScoped<IReviewService, ReviewService>();

// Azure Blob Storage
var blobConnectionString = builder.Configuration.GetConnectionString("AzureBlobStorage");

var containerName = builder.Configuration["Azure:BlobContainerName"] ?? "chess-app-photos";

builder.Services.AddScoped(_ =>
    new BlobContainerClient(
        new Uri($"https://{builder.Configuration["Azure:StorageAccount"]}.blob.core.windows.net/{containerName}"),
        new Azure.Storage.StorageSharedKeyCredential(
            builder.Configuration["Azure:StorageAccount"],
            builder.Configuration["Azure:StorageKey"])));

// =====================================================
// CORS
// =====================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMobileApps", corsBuilder =>
    {
        // Allow requests from mobile apps (localhost for development)
        corsBuilder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();

        // In production, specify exact origins:
        // .WithOrigins("https://app.example.com")
        // .AllowAnyMethod()
        // .AllowAnyHeader();
    });
});

// =====================================================
// CONTROLLERS & SIGNALR
// =====================================================

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ValidationActionFilter>();
});

// FluentValidation: registers every AbstractValidator<T> in this assembly
// (see Validators.cs) so ValidationActionFilter can resolve IValidator<T>
// for whatever DTO an action receives.
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddSignalR();

// =====================================================
// SWAGGER / OPENAPI
// =====================================================

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// =====================================================
// BUILD APPLICATION
// =====================================================

var app = builder.Build();

// =====================================================
// MIDDLEWARE
// =====================================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowMobileApps");

app.UseAuthentication();
app.UseAuthorization();

// =====================================================
// DATABASE MIGRATION & INITIALIZATION
// =====================================================

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    try
    {
        Log.Information("Running database migrations...");
        await dbContext.Database.MigrateAsync();
        Log.Information("Database migrations completed successfully");
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Database migration failed");
        throw;
    }
}

// =====================================================
// ROUTE MAPPING
// =====================================================

app.MapControllers();

// SignalR hub for real-time notifications
app.MapHub<NotificationHub>("/hub/notifications");

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithOpenApi();

// =====================================================
// RUN APPLICATION
// =====================================================

app.Run();
