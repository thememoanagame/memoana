namespace MemoAna.Common.Abstract.ExceptionHandler;

public interface IExceptionHandler
{
    Task HandleAsync(Exception exception);
}
