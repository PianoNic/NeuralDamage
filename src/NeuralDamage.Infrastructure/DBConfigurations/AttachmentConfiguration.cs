using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.DBConfigurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.ChatId);
        builder.HasIndex(a => a.MessageId);

        builder.Property(a => a.ContentType).HasMaxLength(64).IsRequired();

        builder.HasOne(a => a.Chat).WithMany().HasForeignKey(a => a.ChatId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.UploaderUser).WithMany().HasForeignKey(a => a.UploaderUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Message).WithMany(m => m.Attachments).HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}
