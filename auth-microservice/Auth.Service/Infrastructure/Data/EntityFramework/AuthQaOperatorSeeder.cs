using Microsoft.EntityFrameworkCore;

namespace Auth.Service.Infrastructure.Data.EntityFramework;

public static class AuthQaOperatorSeeder
{
    public const string OperatorEmail = "operator@qa.test";
    public const string OperatorPassword = "oKNrqkO7iC#G";
    public const string OperatorRole = "Operator";
    public static readonly Guid OperatorId = new("d0000000-0000-0000-0000-000000000001");

    // Reuses the QA persona PBKDF2 hash (decodes to OperatorPassword via IPasswordHasher<User>).
    private const string OperatorPasswordHash =
        "AQAAAAIAAYagAAAAEDgcVTWsoKHvpybMHFtFOBxG0zYOvKUkB+xDTlq54OejnLzLBpFVNL0oIbrhJs7+hw==";

    public static void SeedQaOperatorUser(this IServiceProvider serviceProvider)
    {
        var authContext = serviceProvider.GetRequiredService<AuthContext>();
        authContext.Database.ExecuteSqlInterpolated(
            $@"IF NOT EXISTS (SELECT 1 FROM [Users] WHERE [Id] = {OperatorId})
               INSERT INTO [Users] ([Id], [Username], [PasswordHash], [Role])
               VALUES ({OperatorId}, {OperatorEmail}, {OperatorPasswordHash}, {OperatorRole});");
    }
}
