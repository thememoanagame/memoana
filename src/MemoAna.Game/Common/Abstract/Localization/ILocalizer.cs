namespace MemoAna.Common.Abstract.Localization;

public interface ILocalizer<T>
{
    string this[string key] { get; }
}
