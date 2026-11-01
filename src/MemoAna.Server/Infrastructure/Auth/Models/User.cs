using MemoAna.Common.Entities;
using Microsoft.AspNetCore.Identity;

namespace MemoAna.Server.Infrastructure.Auth.Models;

public class User(string username = "") : IdentityUser<string>(username), IEntityBase
{
}
