using Microsoft.Maui.ApplicationModel;
using MemoAna.Common.Enums;

namespace MemoAna.Common.Abstract.Services;

/// <summary>
/// Service abstraction for querying and requesting application permissions across platforms.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Checks the current status of the specified application permission without prompting the user.
    /// </summary>
    Task<PermissionStatus> CheckStatusAsync(AppPermission permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests the specified application permission from the platform/user.
    /// </summary>
    Task<PermissionStatus> RequestAsync(AppPermission permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures that the specified application permission is granted.
    /// If currently not granted, requests it from the platform.
    /// </summary>
    Task<PermissionStatus> EnsureGrantedAsync(AppPermission permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the specified application permission is currently granted.
    /// </summary>
    Task<bool> IsGrantedAsync(AppPermission permission, CancellationToken cancellationToken = default);
}

