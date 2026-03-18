using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.ChatId, m.CreatedAt });

        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.Mentions).HasMaxLength(2048);

        builder.HasOne(m => m.Chat).WithMany(c => c.Messages).HasForeignKey(m => m.ChatId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.SenderUser).WithMany(u => u.Messages).HasForeignKey(m => m.SenderUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.SenderBot).WithMany(b => b.Messages).HasForeignKey(m => m.SenderBotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.ReplyTo).WithMany().HasForeignKey(m => m.ReplyToId).OnDelete(DeleteBehavior.SetNull);
    }
}
