using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.HasIndex(u => u.ExternalId).IsUnique();
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.ExternalId).IsRequired().HasMaxLength(256);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.Property(u => u.DisplayName).HasMaxLength(256);
        builder.Property(u => u.AvatarUrl).HasMaxLength(1024);
    }
}
