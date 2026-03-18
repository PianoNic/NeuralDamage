using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Interfaces;

public interface INeuralDamageDbContext
{
    DbSet<User> Users { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
