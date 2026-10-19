using Microsoft.Maui.ApplicationModel;
using MemoAna.Common.Enums;

namespace MemoAna.Common.Abstract.Services;

/// <summary>
/// Platform adapter interface responsible for dispatching permission checks and requests to the underlying OS.
/// </summary>
public interface IPermissionPlatformAdapter
{
    /// <summary>
    /// Queries the underlying platform for the current status of the requested permission.
    /// </summary>
    Task<PermissionStatus> CheckStatusAsync(AppPermission permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a permission request to the underlying platform.
    /// </summary>
    Task<PermissionStatus> RequestAsync(AppPermission permission, CancellationToken cancellationToken = default);
}

