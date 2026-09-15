using MhM.UI.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MhM.UI.Data;

public static class AdminSeed
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<ApplicationIdentityUser>>();
        var db = services.GetRequiredService<MhMDbContext>();

        if (!await roles.RoleExistsAsync(PlatformRoles.Admin))
        {
            var roleResult = await roles.CreateAsync(new IdentityRole(PlatformRoles.Admin));
            if (!roleResult.Succeeded) throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(x => x.Description)));
        }

        var identity = await users.FindByNameAsync("admin");
        if (identity is null)
        {
            identity = new ApplicationIdentityUser
            {
                UserName = "admin",
                Email = "admin@mhm.local",
                EmailConfirmed = true,
                FirstName = "Admin",
                IsActive = true,
                LockoutEnabled = true
            };
            var createResult = await users.CreateAsync(identity);
            if (!createResult.Succeeded) throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(x => x.Description)));

            // Development seed only. Intentionally bypasses production password policy
            // to provide the explicitly requested local credentials admin/admin.
            identity.PasswordHash = users.PasswordHasher.HashPassword(identity, "admin");
            identity.SecurityStamp = Guid.NewGuid().ToString();
            var updateResult = await users.UpdateAsync(identity);
            if (!updateResult.Succeeded) throw new InvalidOperationException(string.Join("; ", updateResult.Errors.Select(x => x.Description)));
        }

        if (!await users.IsInRoleAsync(identity, PlatformRoles.Admin))
        {
            var addRoleResult = await users.AddToRoleAsync(identity, PlatformRoles.Admin);
            if (!addRoleResult.Succeeded) throw new InvalidOperationException(string.Join("; ", addRoleResult.Errors.Select(x => x.Description)));
        }

        if (!await db.AppUsers.AnyAsync(x => x.IdentityUserId == identity.Id))
        {
            db.AppUsers.Add(new AppUser
            {
                IdentityUserId = identity.Id,
                DisplayName = "admin",
                Username = "admin",
                NormalizedUsername = "ADMIN",
                Description = "Plattformadministration",
                Email = identity.Email!,
                PostalCode = "00000",
                City = "Administration",
                Role = UserRole.Privatperson,
                IsVerified = true,
                Verifications = VerificationLevel.Email | VerificationLevel.Identity
            });
            await db.SaveChangesAsync();
        }
    }
}
