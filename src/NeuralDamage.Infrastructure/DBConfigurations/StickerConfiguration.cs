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
        builder.Property(s => s.Sha256).HasMaxLength(64);
        builder.HasIndex(s => s.Sha256);
        // The library is read newest first, a page at a time.
        builder.HasIndex(s => s.CreatedAt);

        builder.HasOne(s => s.UploaderUser).WithMany().HasForeignKey(s => s.UploaderUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
