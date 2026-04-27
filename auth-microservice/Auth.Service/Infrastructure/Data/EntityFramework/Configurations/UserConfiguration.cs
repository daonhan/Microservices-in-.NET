using Auth.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Service.Infrastructure.Data.EntityFramework.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .IsRequired();

        builder.Property(u => u.Role)
            .IsRequired();

        builder.HasData(
            new User
            {
                Id = new Guid("d854813c-4a72-4afd-b431-878cba3ecf2a"),
                Username = "microservices@daonhan.com",
                PasswordHash = "AQAAAAIAAYagAAAAEDgcVTWsoKHvpybMHFtFOBxG0zYOvKUkB+xDTlq54OejnLzLBpFVNL0oIbrhJs7+hw==",
                Role = "Administrator"
            });
    }
}
