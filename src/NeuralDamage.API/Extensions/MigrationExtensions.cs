using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;

namespace NeuralDamage.API.Extensions
{
    public static class MigrationExtensions
    {
        public static WebApplication ApplyMigrations(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            db.Database.Migrate();
            return app;
        }
    }
}
