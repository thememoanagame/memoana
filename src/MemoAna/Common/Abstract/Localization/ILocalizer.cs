namespace MemoAna.Common.Abstract.Localization;

public interface ILocalizer
{
    string this[string key] { get; }
}
