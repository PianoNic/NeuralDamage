using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;

namespace NeuralDamage.Tests.Helpers;

public static class TestDbContext
{
    public static NeuralDamageDbContext Create()
    {
        var options = new DbContextOptionsBuilder<NeuralDamageDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new NeuralDamageDbContext(options);
    }
}
