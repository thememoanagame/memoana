using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using MemoAna.Common.Abstract.Services;
using MemoAna.Common.Enums;

namespace MemoAna.Common.Services;

/// <summary>
/// Cross-platform permission service managing application functional permissions.
/// Queries the underlying platform on demand and avoids redundant requests.
/// </summary>
public sealed class PermissionService(
    IPermissionPlatformAdapter? platformAdapter = null,
    ILogger<PermissionService>? logger = null) : IPermissionService
{
    private readonly IPermissionPlatformAdapter platformAdapter = platformAdapter ?? new DefaultPermissionPlatformAdapter();
    private readonly SemaphoreSlim requestGate = new(1, 1);

    public async Task<PermissionStatus> CheckStatusAsync(AppPermission permission, CancellationToken cancellationToken = default)
    {
        logger?.LogDebug("Checking permission status for {Permission}", permission);
        return await platformAdapter.CheckStatusAsync(permission, cancellationToken);
    }

    public async Task<PermissionStatus> RequestAsync(AppPermission permission, CancellationToken cancellationToken = default)
    {
        await requestGate.WaitAsync(cancellationToken);
        try
        {
            logger?.LogInformation("Requesting permission for {Permission}", permission);
            return await platformAdapter.RequestAsync(permission, cancellationToken);
        }
        finally
        {
            requestGate.Release();
        }
    }

    public async Task<PermissionStatus> EnsureGrantedAsync(AppPermission permission, CancellationToken cancellationToken = default)
    {
        // 1. Check current status without prompting.
        PermissionStatus status = await CheckStatusAsync(permission, cancellationToken);
        if (status == PermissionStatus.Granted)
        {
            logger?.LogDebug("Permission {Permission} is already granted.", permission);
            return PermissionStatus.Granted;
        }

        // 2. Synchronize request so concurrent calls do not prompt the OS repeatedly.
        await requestGate.WaitAsync(cancellationToken);
        try
        {
            // Double-check status after acquiring gate in case a previous concurrent caller obtained it.
            status = await platformAdapter.CheckStatusAsync(permission, cancellationToken);
            if (status == PermissionStatus.Granted)
            {
                logger?.LogDebug("Permission {Permission} was granted by a concurrent request.", permission);
                return PermissionStatus.Granted;
            }

            logger?.LogInformation("Permission {Permission} not granted (status: {Status}). Requesting from platform.", permission, status);
            return await platformAdapter.RequestAsync(permission, cancellationToken);
        }
        finally
        {
            requestGate.Release();
        }
    }

    public async Task<bool> IsGrantedAsync(AppPermission permission, CancellationToken cancellationToken = default)
    {
        PermissionStatus status = await CheckStatusAsync(permission, cancellationToken);
        return status == PermissionStatus.Granted;
    }
}

