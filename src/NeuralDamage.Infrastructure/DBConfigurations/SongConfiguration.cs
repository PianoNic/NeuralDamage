using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.DBConfigurations;

public class SongConfiguration : IEntityTypeConfiguration<Song>
{
    public void Configure(EntityTypeBuilder<Song> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.DeezerId).IsUnique();
        builder.Property(s => s.Title).HasMaxLength(Song.MaxTitleLength).IsRequired();
        builder.Property(s => s.Artist).HasMaxLength(Song.MaxTitleLength).IsRequired();
        builder.Property(s => s.Album).HasMaxLength(Song.MaxTitleLength);
        builder.Property(s => s.CoverUrl).HasMaxLength(1000);
        builder.Property(s => s.FullContentType).HasMaxLength(64);
        builder.Ignore(s => s.HasFull);
    }
}
