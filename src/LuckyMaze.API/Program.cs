using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Toamaisutaa.Abstractions;
using LuckyMaze.API.Extensions;
using LuckyMaze.Infrastructure;
using LuckyMaze.Infrastructure.Services;
using LuckyMaze.Application.Services;
using LuckyMaze.API.Services;
using LuckyMaze.API.Hubs;
using LuckyMaze.Domain;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSwaggerGen(options =>
{
    // Toamaisutaa issues plain bearer tokens - from an identity provider's OIDC flow when one is
    // configured, or from POST /auth/login when it isn't - so one HTTP bearer scheme covers both.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the access_token from POST /auth/login.",
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});

builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });

builder.Services.AddDbContext<LuckyMazeDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("LuckyMazeDatabase"),
        npgsqlOptions => npgsqlOptions
            .EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null
            )
    ));

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddSignalR();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IGameSettingsService, GameSettingsService>();
builder.Services.AddSingleton<IMazeGenerator, MazeGenerator>();
builder.Services.AddSingleton<IAiSolver, AiSolver>();
builder.Services.AddSingleton<IMazeHardwareService, MazeHardwareService>();
builder.Services.AddSingleton<IGameNotificationService, GameNotificationService>();
builder.Services.AddSingleton<GameManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GameManager>());

// Validates OIDC access tokens against Oidc:Authority when one is configured, and Toamaisutaa's own
// locally issued tokens either way - one handler, one scheme, neither downstream code nor an
// endpoint can tell which kind of token it is holding.
builder.Services.AddToamaisutaaBearer(builder.Configuration).AddUserSync();

// Authenticated by default, plus the "Toamaisutaa.Admin" policy from Oidc:AdminRole.
builder.Services.AddToamaisutaaAuthorization(builder.Configuration);

// Roles for a locally issued token come from our own User.Role rather than an identity provider's
// claim. Registered before AddToamaisutaaPasswordLogin, which only fills the slot if it is empty.
builder.Services.AddScoped<IUserRoleProvider, LuckyMazeUserRoleProvider>();

builder.Services.AddToamaisutaaProvisioning();
builder.Services.AddToamaisutaaEntityFrameworkStores<LuckyMazeDbContext>();
builder.Services.AddToamaisutaaCurrentUser();

// Local username/password sign-in and open self-registration - no identity provider required.
builder.Services.AddToamaisutaaPasswordLogin(builder.Configuration);
builder.Services.AddSingleton<IPasswordResetNotifier, LoggingPasswordResetNotifier>();
builder.Services.AddToamaisutaaTokenCleanup();

var app = builder.Build();

app.ApplyMigrations();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("DefaultCorsPolicy");
app.UseAuthentication();
app.UseAuthorization();

// The SPA's runtime OIDC config is served by AppController (already at GET /api/app), backed by
// the same Oidc:* configuration Toamaisutaa itself binds - so MapToamaisutaaConfiguration is
// deliberately not mapped here, to avoid two handlers on the same route.

// POST /auth/login, /auth/refresh, /auth/logout, /auth/register, /auth/password,
// /auth/password/forgot, /auth/password/reset.
app.MapToamaisutaaPasswordEndpoints();

app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

app.Run();
