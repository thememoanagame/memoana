using MemoAna.Common.Enums;

namespace MemoAna.Common.Abstract.Services;

public interface IPlatformService
{ 
    bool IsWindows { get; }
    bool IsMacOS { get; }
    bool IsAndroid { get; }
    bool IsIOS { get; }
}
