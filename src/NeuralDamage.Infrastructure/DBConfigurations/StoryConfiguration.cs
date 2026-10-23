using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.DBConfigurations;

public class StoryConfiguration : IEntityTypeConfiguration<Story>
{
    public void Configure(EntityTypeBuilder<Story> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.ExpiresAt);
        builder.HasIndex(s => new { s.AuthorBotId, s.CreatedAt });
        builder.Property(s => s.Spec).IsRequired();

        builder.HasOne(s => s.AuthorUser).WithMany().HasForeignKey(s => s.AuthorUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.AuthorBot).WithMany().HasForeignKey(s => s.AuthorBotId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StoryViewConfiguration : IEntityTypeConfiguration<StoryView>
{
    public void Configure(EntityTypeBuilder<StoryView> builder)
    {
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => new { v.StoryId, v.UserId }).IsUnique();
        builder.HasOne(v => v.Story).WithMany(s => s.Views).HasForeignKey(v => v.StoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StoryImageConfiguration : IEntityTypeConfiguration<StoryImage>
{
    public void Configure(EntityTypeBuilder<StoryImage> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.ContentType).HasMaxLength(64).IsRequired();
        builder.HasIndex(i => i.CreatedAt);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.UploaderUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
