using MemoAna.Common.Entities;
using Microsoft.AspNetCore.Identity;

namespace MemoAna.Server.Infrastructure.Auth.Models;

public class User : IdentityUser<string>, IEntityBase
{
    public User() : base()
    {
        Id = Guid.NewGuid().ToString();
    }

    public User(string userName) : base(userName)
    {
        Id = Guid.NewGuid().ToString();
    }

    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? SurName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? PlayerId { get; set; }
}
