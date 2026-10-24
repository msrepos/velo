using Microsoft.AspNetCore.Identity;

namespace Velo.Modules.Identity.Infrastructure.Persistence.Models;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool IsActive { get; set; } = true;
    public string? Name { get; set; }
}
