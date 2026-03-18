using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure;

public class NeuralDamageDbContext(DbContextOptions<NeuralDamageDbContext> options) : DbContext(options), INeuralDamageDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Bot> Bots => Set<Bot>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatMember> ChatMembers => Set<ChatMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Reaction> Reactions => Set<Reaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(typeof(NeuralDamageDbContext).Assembly);

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;

        return base.SaveChangesAsync(cancellationToken);
    }

    public class NeuralDamageDbContextFactory : IDesignTimeDbContextFactory<NeuralDamageDbContext>
    {
        public NeuralDamageDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<NeuralDamageDbContext>();
            optionsBuilder.UseNpgsql();
            return new NeuralDamageDbContext(optionsBuilder.Options);
        }
    }
}
