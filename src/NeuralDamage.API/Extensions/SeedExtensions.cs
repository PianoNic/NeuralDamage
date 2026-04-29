using NeuralDamage.Infrastructure;

namespace NeuralDamage.API.Extensions
{
    public static class SeedExtensions
    {
        public static async Task<WebApplication> ApplySeedsAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();

            // No seeders yet — placeholder to match Polyglot layout.
            await Task.CompletedTask;

            return app;
        }
    }
}
