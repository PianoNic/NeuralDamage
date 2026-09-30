using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.DBConfigurations;

public class BotConfiguration : IEntityTypeConfiguration<Bot>
{
    public void Configure(EntityTypeBuilder<Bot> builder)
    {
        builder.HasKey(b => b.Id);
        // No index on Name: names are unique within a chat, not across all bots, and the handlers
        // enforce that (BotNames). Nothing looks a bot up by name in the database.

        builder.Property(b => b.Name).HasMaxLength(128).IsRequired();
        builder.Property(b => b.ModelId).HasMaxLength(256).IsRequired();
        builder.Property(b => b.SystemPrompt).IsRequired();
        builder.Property(b => b.Personality);
        builder.Property(b => b.Temperature).HasDefaultValue(0.7);
        builder.Property(b => b.AvatarUrl).HasMaxLength(1024);
        builder.Property(b => b.Aliases).HasMaxLength(512);
        builder.Property(b => b.IsActive).HasDefaultValue(true);
        // The sentinel keeps an explicit false from being swapped for the column default on insert.
        builder.Property(b => b.IsPublic).HasDefaultValue(true).HasSentinel(true);

        builder.HasOne(b => b.CreatedBy).WithMany(u => u.CreatedBots).HasForeignKey(b => b.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Chat).WithMany().HasForeignKey(b => b.ChatId).OnDelete(DeleteBehavior.SetNull);
    }
}
