using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Configurations;

public class BotConfiguration : IEntityTypeConfiguration<Bot>
{
    public void Configure(EntityTypeBuilder<Bot> builder)
    {
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => b.Name).IsUnique();

        builder.Property(b => b.Name).HasMaxLength(128).IsRequired();
        builder.Property(b => b.ModelId).HasMaxLength(256).IsRequired();
        builder.Property(b => b.SystemPrompt).IsRequired();
        builder.Property(b => b.Personality);
        builder.Property(b => b.Temperature).HasDefaultValue(0.7);
        builder.Property(b => b.AvatarUrl).HasMaxLength(1024);
        builder.Property(b => b.Aliases).HasMaxLength(512);
        builder.Property(b => b.IsActive).HasDefaultValue(true);

        builder.HasOne(b => b.CreatedBy).WithMany(u => u.CreatedBots).HasForeignKey(b => b.CreatedById).OnDelete(DeleteBehavior.Restrict);
    }
}
