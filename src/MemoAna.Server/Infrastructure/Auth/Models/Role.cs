using Microsoft.AspNetCore.Identity;

namespace MemoAna.Server.Infrastructure.Auth.Models;

public class Role(string name = "") : IdentityRole<string>(name)
{
}
