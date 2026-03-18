using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Interfaces;

public interface INeuralDamageDbContext
{
    DbSet<User> Users { get; }
    DbSet<Bot> Bots { get; }
    DbSet<Chat> Chats { get; }
    DbSet<ChatMember> ChatMembers { get; }
    DbSet<Message> Messages { get; }
    DbSet<Reaction> Reactions { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
