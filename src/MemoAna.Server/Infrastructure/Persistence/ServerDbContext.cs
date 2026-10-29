using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
namespace MemoAna.Server.Infrastructure.Persistence;

public class ServerDbContext : IdentityDbContext<IdentityUser, IdentityRole, string>
{
    public ServerDbContext(DbContextOptions options) : base(options)
    {
    }
}