using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.DBConfigurations;

public class ChatConfiguration : IEntityTypeConfiguration<Chat>
{
    public void Configure(EntityTypeBuilder<Chat> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(256).IsRequired();
        // HistoryOptions.SummaryMaxChars is the cap; this only bounds the column.
        builder.Property(c => c.Summary).HasMaxLength(Services.ChatCompactor.MaxStoredChars);
        builder.Property(c => c.DirectKey).HasMaxLength(80);
        builder.Property(c => c.AvatarUrl).HasMaxLength(256);
        builder.HasIndex(c => c.DirectKey).IsUnique().HasFilter("\"DirectKey\" IS NOT NULL");
        builder.Ignore(c => c.IsDirect);

        builder.HasOne(c => c.CreatedBy).WithMany(u => u.CreatedChats).HasForeignKey(c => c.CreatedById).OnDelete(DeleteBehavior.Restrict);
    }
}
