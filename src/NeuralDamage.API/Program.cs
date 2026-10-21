using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using NeuralDamage.API.Extensions;
using NeuralDamage.API.Middleware;
using NeuralDamage.API.Services;
using NeuralDamage.Application.Behaviors;
using NeuralDamage.Application.Validators;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.BackgroundServices;
using NeuralDamage.Infrastructure.Extensions;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Stickers;
using NeuralDamage.Infrastructure.Services.BotPictures;
using NeuralDamage.Infrastructure.Services.Feed;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

// Validation messages and formatting stay English whatever the host's locale.
ValidationCulture.Pin();

// Chat text is full of emoji, which a console left on its default code page logs as "??".
Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Database
builder.Services.AddDbContext<NeuralDamageDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton(AppAdmins.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<IConnectionTracker, ConnectionTracker>();
builder.Services.AddSingleton<IChatBotState, ChatBotState>();
builder.Services.AddScoped<IChatNotificationService, ChatNotificationService>();
builder.Services.AddSingleton(ModelPolicy.FromConfiguration(builder.Configuration));
builder.Services.AddScoped<IOpenRouterService, OpenRouterAgentService>();
builder.Services.AddSingleton(BotRankingOptions.FromConfiguration(builder.Configuration));
builder.Services.AddHttpClient<IDecisionsClient, DecisionsClient>((sp, client) =>
    client.Timeout = sp.GetRequiredService<BotRankingOptions>().Timeout);
builder.Services.Configure<BotBehaviorOptions>(builder.Configuration.GetSection(BotBehaviorOptions.SectionName));
builder.Services.Configure<HistoryOptions>(builder.Configuration.GetSection(HistoryOptions.SectionName));
builder.Services.AddSingleton<IChatCompactor, ChatCompactor>();
builder.Services.AddScoped<IBotDecisionEngine, BotDecisionEngine>();
builder.Services.AddSingleton(sp => BotClock.FromConfiguration(builder.Configuration, sp.GetRequiredService<ILogger<BotClock>>()));
builder.Services.AddSingleton<BotResponseQueue>();
builder.Services.AddSingleton<IBotResponseQueue>(sp => sp.GetRequiredService<BotResponseQueue>());
builder.Services.AddSingleton<IBotResponseOrchestrator, BotResponseOrchestrator>();
builder.Services.AddHostedService<BotResponseBackgroundService>();

// The feed: bots post on their own (FeedScheduler) and answer posts and comments (FeedConversation).
builder.Services.Configure<FeedOptions>(builder.Configuration.GetSection(FeedOptions.SectionName));
builder.Services.AddScoped<IFeedNotifier, FeedNotifier>();
builder.Services.AddScoped<FeedPostWriter>(sp => new FeedPostWriter(
    sp.GetRequiredService<NeuralDamageDbContext>(),
    sp.GetRequiredService<IOpenRouterService>(),
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<FeedPostWriter>>(),
    sp.GetRequiredService<IOptions<FeedOptions>>(),
    sp.GetRequiredService<BotClock>()));
builder.Services.AddSingleton<FeedQueue>();
builder.Services.AddSingleton<IFeedQueue>(sp => sp.GetRequiredService<FeedQueue>());
builder.Services.AddSingleton(sp => new FeedConversation(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<ILogger<FeedConversation>>(),
    sp.GetRequiredService<IOptions<BotBehaviorOptions>>(),
    sp.GetRequiredService<IOptions<FeedOptions>>(),
    sp.GetRequiredService<BotClock>()));
builder.Services.AddHostedService<FeedBackgroundService>();
builder.Services.AddSingleton(sp => new FeedScheduler(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<ILogger<FeedScheduler>>(),
    sp.GetRequiredService<IOptions<FeedOptions>>(),
    sp.GetRequiredService<BotClock>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<FeedScheduler>());

// Images
var attachmentOptions = AttachmentOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(attachmentOptions);
builder.Services.AddSingleton<IAttachmentStorage>(new FileSystemAttachmentStorage(
    Path.GetFullPath(attachmentOptions.Path, builder.Environment.ContentRootPath)));
builder.Services.AddSingleton<ImageDescriber>();
builder.Services.AddSingleton<IImageDescriber>(sp => sp.GetRequiredService<ImageDescriber>());
builder.Services.AddSingleton(sp => new StickerDescribeQueue(
    sp.GetRequiredService<ImageDescriber>().DescribeStickerAsync,
    sp.GetRequiredService<ILogger<StickerDescribeQueue>>()));
builder.Services.AddSingleton<IStickerDescribeQueue>(sp => sp.GetRequiredService<StickerDescribeQueue>());
builder.Services.AddHostedService<StickerDescribeService>();
builder.Services.AddHostedService<UnsentAttachmentSweeper>();

// Picture suggestions for new bots, from Wikimedia. Redirects are followed by hand, only to allowed hosts.
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IBotPictureFinder, BotPictureFinder>(client => client.Timeout = TimeSpan.FromSeconds(6))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

// Rate limits
builder.Services.AddChatRateLimits(builder.Configuration);

// Mediator & Validation
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.PipelineBehaviors = [typeof(ValidationBehavior<,>)];
});
builder.Services.AddValidatorsFromAssemblyContaining<SendMessageValidator>(ServiceLifetime.Scoped);

// Authentication (Toamaisutaa)
builder.Services.AddToamaisutaaBearer(builder.Configuration);
builder.Services.AddToamaisutaaAuthorization(builder.Configuration);
builder.Services.AddToamaisutaaCurrentUser();
builder.Services.AddUserSync();
// SignalR
builder.Services.AddSignalR();

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
        // The frontend's models are generated from this document, so a DTO
        // property that cannot be null in C# has to say so here - otherwise
        // every generated field comes out optional and the client has to
        // guard values that are always present.
        options.SupportNonNullableReferenceTypes();
        options.NonNullableReferenceTypesAsRequired();

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

// Database
app.ApplyMigrations();
await app.ApplySeedsAsync();

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

app.UseRouting();
if (app.Environment.IsDevelopment())
    app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapHub<NeuralDamage.API.Hubs.ChatHub>("/hubs/chat");
app.MapHub<NeuralDamage.API.Hubs.UserHub>("/hubs/user");

// The SPA shell has to load before the user can sign in, so it bypasses the
// fallback authorization policy that guards everything else.
if (!app.Environment.IsDevelopment())
    app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
