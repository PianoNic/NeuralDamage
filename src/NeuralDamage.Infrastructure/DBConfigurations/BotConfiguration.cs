using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.DBConfigurations;

public class BotConfiguration : IEntityTypeConfiguration<Bot>
{
    public void Configure(EntityTypeBuilder<Bot> builder)
    {
        builder.HasKey(b => b.Id);
        // No index on Name: names are unique within a chat, not across all bots, and the handlers
        // enforce that (BotNames). Nothing looks a bot up by name in the database.

        builder.Property(b => b.Name).HasMaxLength(128).IsRequired();
        builder.Property(b => b.ModelId).HasMaxLength(256).IsRequired();
        builder.Property(b => b.SystemPrompt).IsRequired();
        builder.Property(b => b.Personality);
        builder.Property(b => b.Temperature).HasDefaultValue(0.7);
        builder.Property(b => b.AvatarUrl).HasMaxLength(1024);
        builder.Property(b => b.Aliases).HasMaxLength(512);
        builder.Property(b => b.Looks).HasMaxLength(Bot.MaxLooksLength);
        builder.Property(b => b.IsActive).HasDefaultValue(true);
        // The sentinel keeps an explicit false from being swapped for the column default on insert.
        builder.Property(b => b.IsPublic).HasDefaultValue(true).HasSentinel(true);
        builder.Property(b => b.FeedEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(b => b.MaxPostsPerDay).HasDefaultValue(Bot.DefaultPostsPerDay).HasSentinel(Bot.DefaultPostsPerDay);
        builder.Property(b => b.MaxStoriesPerDay).HasDefaultValue(Bot.DefaultStoriesPerDay).HasSentinel(Bot.DefaultStoriesPerDay);
        builder.Property(b => b.MemoryEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(b => b.MemoryNotes).HasMaxLength(Bot.MaxMemoryNotesLength);

        builder.HasOne(b => b.CreatedBy).WithMany(u => u.CreatedBots).HasForeignKey(b => b.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Chat).WithMany().HasForeignKey(b => b.ChatId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class BotMemoryConfiguration : IEntityTypeConfiguration<BotMemory>
{
    public void Configure(EntityTypeBuilder<BotMemory> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Text).HasMaxLength(BotMemory.MaxTextLength).IsRequired();
        builder.Property(m => m.Scope).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.Source).HasConversion<string>().HasMaxLength(16);
        builder.HasOne(m => m.Bot).WithMany().HasForeignKey(m => m.BotId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.SubjectUser).WithMany().HasForeignKey(m => m.SubjectUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Chat>().WithMany().HasForeignKey(m => m.ChatId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.BotId, m.Scope });
        builder.HasIndex(m => m.SubjectUserId);
    }
}

public class BotMemoryCursorConfiguration : IEntityTypeConfiguration<BotMemoryCursor>
{
    public void Configure(EntityTypeBuilder<BotMemoryCursor> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasOne<Bot>().WithMany().HasForeignKey(c => c.BotId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Chat>().WithMany().HasForeignKey(c => c.ChatId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.BotId, c.ChatId }).IsUnique();
    }
}

public class BotReferenceConfiguration : IEntityTypeConfiguration<BotReference>
{
    public void Configure(EntityTypeBuilder<BotReference> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ContentType).HasMaxLength(64).IsRequired();
        builder.HasOne(r => r.Bot).WithMany(b => b.References).HasForeignKey(r => r.BotId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.BotId);
    }
}
