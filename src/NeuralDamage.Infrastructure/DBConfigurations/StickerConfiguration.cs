using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services.Stickers;

namespace NeuralDamage.Infrastructure.DBConfigurations;

public class StickerConfiguration : IEntityTypeConfiguration<Sticker>
{
    public void Configure(EntityTypeBuilder<Sticker> builder)
    {
        builder.HasKey(s => s.Id);
        // A deleted sticker frees its name for a new one.
        builder.HasIndex(s => s.Name).IsUnique().HasFilter("\"DeletedAt\" IS NULL");

        builder.Property(s => s.Name).HasMaxLength(StickerRules.MaxNameLength).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(StickerRules.MaxDescriptionLength);
        builder.Property(s => s.ContentType).HasMaxLength(64).IsRequired();

        builder.HasOne(s => s.UploaderUser).WithMany().HasForeignKey(s => s.UploaderUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
