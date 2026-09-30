using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using NeuralDamage.Infrastructure.Extensions;

namespace NeuralDamage.Tests.Infrastructure;

public class RateLimitTests
{
    /// <summary>A host with the app's limits on one endpoint, and the caller named by a header.</summary>
    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddChatRateLimits(builder.Configuration);

        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", context.Request.Headers["X-User"].ToString())], "test"));
            return next(context);
        });
        app.UseRateLimiter();
        app.MapPost("/messages", () => Results.Accepted()).RequireRateLimiting(RateLimitExtensions.Messages);
        await app.StartAsync();
        return app;
    }

    private static Task<HttpResponseMessage> PostAs(HttpClient client, string user)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/messages");
        request.Headers.Add("X-User", user);
        return client.SendAsync(request);
    }

    [Test]
    public async Task PastTheLimit_TheSenderGets429_WithAMessage_AndOthersCarryOn()
    {
        await using var app = await StartAsync(new() { ["RateLimits:Messages:PermitLimit"] = "2", ["RateLimits:Messages:WindowSeconds"] = "60" });
        var client = app.GetTestClient();

        var first = await PostAs(client, "alice");
        var second = await PostAs(client, "alice");
        var third = await PostAs(client, "alice");
        var bob = await PostAs(client, "bob");

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(third.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(await third.Content.ReadAsStringAsync()).IsEqualTo("You're sending messages too fast. Wait a moment and try again.");
        await Assert.That(third.Headers.RetryAfter).IsNotNull();
        await Assert.That(bob.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task APermitLimitOfZero_TurnsTheLimitOff()
    {
        await using var app = await StartAsync(new() { ["RateLimits:Messages:PermitLimit"] = "0" });
        var client = app.GetTestClient();

        for (var i = 0; i < 50; i++)
            await Assert.That((await PostAs(client, "alice")).StatusCode).IsEqualTo(HttpStatusCode.Accepted);
    }
}
