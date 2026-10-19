using System.Reflection;
using MemoAna.Common.Abstract.Localization;
using Microsoft.Extensions.Localization;

namespace MemoAna.Common.Localization;

public class Localizer<T>(IStringLocalizerFactory factory) : ILocalizer<T> 
{
    private readonly IStringLocalizer _localizer =
        factory.Create("Strings", typeof(T).Assembly.FullName!);

    public string this[string key] => _localizer[key];
}
