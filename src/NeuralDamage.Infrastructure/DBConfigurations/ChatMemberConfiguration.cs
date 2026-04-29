using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.DBConfigurations;

public class ChatMemberConfiguration : IEntityTypeConfiguration<ChatMember>
{
    public void Configure(EntityTypeBuilder<ChatMember> builder)
    {
        builder.HasKey(cm => cm.Id);

        builder.HasIndex(cm => new { cm.ChatId, cm.UserId }).IsUnique().HasFilter("\"UserId\" IS NOT NULL");
        builder.HasIndex(cm => new { cm.ChatId, cm.BotId }).IsUnique().HasFilter("\"BotId\" IS NOT NULL");
        builder.ToTable(t => t.HasCheckConstraint("CK_ChatMember_UserOrBot", "(\"UserId\" IS NOT NULL AND \"BotId\" IS NULL) OR (\"UserId\" IS NULL AND \"BotId\" IS NOT NULL)"));

        builder.Property(cm => cm.Role).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(cm => cm.Chat).WithMany(c => c.Members).HasForeignKey(cm => cm.ChatId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(cm => cm.User).WithMany(u => u.ChatMembers).HasForeignKey(cm => cm.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(cm => cm.Bot).WithMany(b => b.ChatMembers).HasForeignKey(cm => cm.BotId).OnDelete(DeleteBehavior.Restrict);
    }
}
