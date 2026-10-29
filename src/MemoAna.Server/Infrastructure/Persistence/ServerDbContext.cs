using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
namespace MemoAna.Server.Infrastructure.Persistence;

public class ServerDbContext(DbContextOptions options) : IdentityDbContext<IdentityUser, IdentityRole, string>(options)
{
}