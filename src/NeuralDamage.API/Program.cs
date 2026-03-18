using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using NeuralDamage.API.Middleware;
using NeuralDamage.API.Services;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Database
builder.Services.AddDbContext<NeuralDamageDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<INeuralDamageDbContext>(sp => sp.GetRequiredService<NeuralDamageDbContext>());

// Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IUserSyncService, UserSyncService>();

// Mediator & Validation
builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });

// Authentication (JWT/OIDC)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Oidc:Authority"];
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Oidc:RequireHttpsMetadata", true);
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "role";
        options.TokenValidationParameters.ValidateAudience = false;
    });
builder.Services.AddAuthorization(options => options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// API
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHttpClient();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

if (builder.Environment.IsDevelopment())
{
    var oidcAuthority = builder.Configuration["Oidc:Authority"] ?? "";
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.AddSecurityDefinition("OAuth2", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{oidcAuthority}/authorize"),
                    TokenUrl = new Uri("/api/oidc/token", UriKind.Relative),
                    Scopes = new Dictionary<string, string>
                    {
                        { "openid", "OpenID" },
                        { "profile", "Profile" },
                        { "email", "Email" }
                    }
                }
            }
        });
        options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("OAuth2", doc)] = ["openid", "profile", "email"]
        });
    });
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
}

var app = builder.Build();

// Database migration
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
    dbContext.Database.Migrate();
}

// Middleware pipeline
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "NeuralDamage API V1");
        c.OAuthClientId(builder.Configuration["Oidc:ClientId"]);
        c.OAuthAppName("NeuralDamage");
        c.OAuthScopeSeparator(" ");
        c.OAuthUsePkce();
    });

    // Token proxy to bypass CORS on the OIDC provider's token endpoint
    app.MapPost("/api/oidc/token", async (HttpContext ctx, IHttpClientFactory httpClientFactory, IConfiguration config) =>
    {
        var form = await ctx.Request.ReadFormAsync();
        var client = httpClientFactory.CreateClient();
        var content = new FormUrlEncodedContent(form.ToDictionary(k => k.Key, v => v.Value.ToString()));
        var response = await client.PostAsync($"{config["Oidc:Authority"]}/api/oidc/token", content);
        ctx.Response.StatusCode = (int)response.StatusCode;
        ctx.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        await response.Content.CopyToAsync(ctx.Response.Body);
    }).AllowAnonymous().WithTags("Internal (Dev Only)");
}

app.UseStaticFiles();

if (!app.Environment.IsDevelopment())
{
    // Don't use HTTPS redirection in production when running behind a reverse proxy
    // The proxy handles HTTPS termination
    // app.UseHttpsRedirection();
    app.UseSpaStaticFiles();
}

// Ensure frontend routes work
app.UseRouting();
if (app.Environment.IsDevelopment())
    app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (!app.Environment.IsDevelopment())
{
    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = "wwwroot";
    });
}

app.Run();