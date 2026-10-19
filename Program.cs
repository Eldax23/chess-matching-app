using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using EFCore.NamingConventions;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Serilog;
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

// Lichess account linking (OAuth PKCE)
builder.Services.Configure<LichessOptions>(builder.Configuration.GetSection("Lichess"));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<ILichessClient, LichessClient>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LichessOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
});
builder.Services.AddScoped<ILichessIntegrationService, LichessIntegrationService>();

// chess.com account linking by username (public API, unverified)
builder.Services.Configure<ChessComOptions>(builder.Configuration.GetSection("ChessCom"));
builder.Services.AddHttpClient<IChessComClient, ChessComClient>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ChessComOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
});
builder.Services.AddScoped<IChessComIntegrationService, ChessComIntegrationService>();

// Periodic rating refresh for all linked accounts
builder.Services.AddHostedService<LinkedAccountSyncWorker>();

// Photo storage: local disk in Development (free, no signup needed),
// an S3-compatible provider (Cloudflare R2, Backblaze B2, AWS S3, ...)
// in every other environment - see Storage:S3 in appsettings.json.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IPhotoStorageService, LocalPhotoStorageService>();
}
else
{
    var s3Section = builder.Configuration.GetSection("Storage:S3");
    var s3ServiceUrl = s3Section["ServiceUrl"]
        ?? throw new InvalidOperationException("Storage:S3:ServiceUrl not configured");
    var s3AccessKey = s3Section["AccessKey"]
        ?? throw new InvalidOperationException("Storage:S3:AccessKey not configured");
    var s3SecretKey = s3Section["SecretKey"]
        ?? throw new InvalidOperationException("Storage:S3:SecretKey not configured");

    builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
        new BasicAWSCredentials(s3AccessKey, s3SecretKey),
        new AmazonS3Config
        {
            ServiceURL = s3ServiceUrl,
            ForcePathStyle = true
        }));

    builder.Services.AddSingleton<IPhotoStorageService, S3PhotoStorageService>();
}

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

    // Serves photos saved by LocalPhotoStorageService back out at /uploads/...
    var uploadsPath = Path.Combine(app.Environment.ContentRootPath,
        app.Configuration["Storage:LocalUploadsPath"] ?? "uploads");
    Directory.CreateDirectory(uploadsPath);

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadsPath),
        RequestPath = "/uploads"
    });
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
