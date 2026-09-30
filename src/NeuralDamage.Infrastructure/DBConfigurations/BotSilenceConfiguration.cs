using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.DBConfigurations;

public class BotSilenceConfiguration : IEntityTypeConfiguration<BotSilence>
{
    public void Configure(EntityTypeBuilder<BotSilence> builder)
    {
        builder.HasKey(s => s.Id);

        // One stop per chat and one mute per bot and chat: a null BotId counts as a value here.
        builder.HasIndex(s => new { s.ChatId, s.BotId }).IsUnique().AreNullsDistinct(false);

        builder.HasOne<Chat>().WithMany().HasForeignKey(s => s.ChatId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Bot>().WithMany().HasForeignKey(s => s.BotId).OnDelete(DeleteBehavior.Cascade);
    }
}
