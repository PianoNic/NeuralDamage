using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Configurations;

public class ReactionConfiguration : IEntityTypeConfiguration<Reaction>
{
    public void Configure(EntityTypeBuilder<Reaction> builder)
    {
        builder.HasKey(r => r.Id);

        builder.HasIndex(r => new { r.MessageId, r.UserId, r.Emoji }).IsUnique().HasFilter("\"UserId\" IS NOT NULL");
        builder.HasIndex(r => new { r.MessageId, r.BotId, r.Emoji }).IsUnique().HasFilter("\"BotId\" IS NOT NULL");

        builder.Property(r => r.Emoji).HasMaxLength(32).IsRequired();

        builder.HasOne(r => r.Message).WithMany(m => m.Reactions).HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.User).WithMany(u => u.Reactions).HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Bot).WithMany(b => b.Reactions).HasForeignKey(r => r.BotId).OnDelete(DeleteBehavior.Restrict);
    }
}
