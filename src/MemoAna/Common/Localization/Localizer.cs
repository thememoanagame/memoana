using MemoAna.Common.Abstract.Localization;
using MemoAna.Resources.Localization;
using Microsoft.Extensions.Localization;

namespace MemoAna.Common.Localization;

public class Localizer(IStringLocalizerFactory factory) : ILocalizer 
{
    private readonly IStringLocalizer _localizer =
        factory.Create("Strings", typeof(Strings).Assembly.FullName!);

    public string this[string key] => _localizer[key];
}
