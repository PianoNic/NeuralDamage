using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.DBConfigurations;

public class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.CreatedAt);
        builder.HasIndex(p => new { p.AuthorBotId, p.CreatedAt });

        builder.Property(p => p.Content).HasMaxLength(Post.MaxContentLength).IsRequired();

        // A person's or bot's posts go with them; deleting either is rare and already restricted elsewhere.
        builder.HasOne(p => p.AuthorUser).WithMany().HasForeignKey(p => p.AuthorUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.AuthorBot).WithMany().HasForeignKey(p => p.AuthorBotId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.Sticker).WithMany().HasForeignKey(p => p.StickerId).OnDelete(DeleteBehavior.Restrict);
        // The post stays when its chat is deleted; it only loses the link.
        builder.HasOne(p => p.SourceChat).WithMany().HasForeignKey(p => p.SourceChatId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class PostCommentConfiguration : IEntityTypeConfiguration<PostComment>
{
    public void Configure(EntityTypeBuilder<PostComment> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.PostId, c.CreatedAt });
        builder.HasIndex(c => new { c.AuthorBotId, c.CreatedAt });

        builder.Property(c => c.Content).HasMaxLength(PostComment.MaxContentLength).IsRequired();

        builder.HasOne(c => c.Post).WithMany(p => p.Comments).HasForeignKey(c => c.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.Parent).WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.AuthorUser).WithMany().HasForeignKey(c => c.AuthorUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.AuthorBot).WithMany().HasForeignKey(c => c.AuthorBotId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.Sticker).WithMany().HasForeignKey(c => c.StickerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class FeedLikeConfiguration : IEntityTypeConfiguration<FeedLike>
{
    public void Configure(EntityTypeBuilder<FeedLike> builder)
    {
        builder.HasKey(l => l.Id);

        // One like per liker and thing liked; a post's own likes have no comment.
        builder.HasIndex(l => new { l.PostId, l.CommentId, l.UserId }).IsUnique().HasFilter("\"UserId\" IS NOT NULL").AreNullsDistinct(false);
        builder.HasIndex(l => new { l.PostId, l.CommentId, l.BotId }).IsUnique().HasFilter("\"BotId\" IS NOT NULL").AreNullsDistinct(false);
        builder.HasIndex(l => l.CommentId);

        builder.HasOne(l => l.Post).WithMany(p => p.Likes).HasForeignKey(l => l.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(l => l.Comment).WithMany(c => c.Likes).HasForeignKey(l => l.CommentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Bot>().WithMany().HasForeignKey(l => l.BotId).OnDelete(DeleteBehavior.Cascade);
    }
}
