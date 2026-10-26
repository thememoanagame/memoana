using Microsoft.Maui.ApplicationModel;
using MemoAna.Common.Enums;

namespace MemoAna.Common.Exceptions;

/// <summary>
/// Exception thrown when an operation requires a permission that has not been granted.
/// </summary>
public sealed class PermissionDeniedException : InvalidOperationException
{
    public AppPermission Permission { get; }
    public PermissionStatus Status { get; }

    public PermissionDeniedException(AppPermission permission, PermissionStatus status)
        : base($"Permission '{permission}' was not granted. Platform status: {status}.")
    {
        Permission = permission;
        Status = status;
    }

    public PermissionDeniedException(AppPermission permission, PermissionStatus status, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Permission = permission;
        Status = status;
    }
}

