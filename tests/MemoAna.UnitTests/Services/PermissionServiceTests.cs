using Microsoft.Maui.ApplicationModel;
using MemoAna.Common.Abstract.Services;
using MemoAna.Common.Enums;
using MemoAna.Common.Services;
using Xunit;

namespace MemoAna.UnitTests.Services;

public sealed class PermissionServiceTests
{
    [Fact]
    public async Task EnsureGrantedAsync_WhenAlreadyGranted_ReturnsGranted_AndDoesNotCallRequest()
    {
        var adapter = new FakePermissionPlatformAdapter
        {
            StatusToReturn = PermissionStatus.Granted,
            RequestStatusToReturn = PermissionStatus.Granted
        };
        var service = new PermissionService(adapter);

        PermissionStatus status = await service.EnsureGrantedAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionStatus.Granted, status);
        Assert.Equal(0, adapter.RequestCallCount);
        Assert.True(adapter.CheckCallCount >= 1);
    }

    [Fact]
    public async Task EnsureGrantedAsync_WhenInitiallyDenied_RequestsPermission()
    {
        var adapter = new FakePermissionPlatformAdapter
        {
            StatusToReturn = PermissionStatus.Denied,
            RequestStatusToReturn = PermissionStatus.Granted
        };
        var service = new PermissionService(adapter);

        PermissionStatus status = await service.EnsureGrantedAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionStatus.Granted, status);
        Assert.Equal(1, adapter.RequestCallCount);
    }

    [Fact]
    public async Task EnsureGrantedAsync_WhenDeniedAndDeniedOnRequest_ReturnsDenied()
    {
        var adapter = new FakePermissionPlatformAdapter
        {
            StatusToReturn = PermissionStatus.Denied,
            RequestStatusToReturn = PermissionStatus.Denied
        };
        var service = new PermissionService(adapter);

        PermissionStatus status = await service.EnsureGrantedAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionStatus.Denied, status);
        Assert.Equal(1, adapter.RequestCallCount);
    }

    [Fact]
    public async Task RevokedPermission_QueriesPlatformAgain_DoesNotTrustCachedValue()
    {
        var adapter = new FakePermissionPlatformAdapter
        {
            StatusToReturn = PermissionStatus.Granted
        };
        var service = new PermissionService(adapter);

        PermissionStatus firstResult = await service.EnsureGrantedAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);
        Assert.Equal(PermissionStatus.Granted, firstResult);

        // Simulate user revoking permission in device Settings
        adapter.StatusToReturn = PermissionStatus.Denied;
        adapter.RequestStatusToReturn = PermissionStatus.Denied;

        PermissionStatus secondResult = await service.EnsureGrantedAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);
        Assert.Equal(PermissionStatus.Denied, secondResult);
        Assert.True(adapter.CheckCallCount >= 2);
    }

    [Fact]
    public async Task ConcurrentRequests_SynchronizesAndAvoidsDuplicatePrompts()
    {
        var adapter = new FakePermissionPlatformAdapter
        {
            StatusToReturn = PermissionStatus.Denied,
            RequestStatusToReturn = PermissionStatus.Granted
        };

        var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        adapter.OnRequestAsync = async () =>
        {
            requestStarted.TrySetResult(true);
            await releaseRequest.Task;
            adapter.StatusToReturn = PermissionStatus.Granted;
        };

        var service = new PermissionService(adapter);

        Task<PermissionStatus> call1 = service.EnsureGrantedAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Task<PermissionStatus> call2 = service.EnsureGrantedAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);
        releaseRequest.TrySetResult(true);

        PermissionStatus[] results = await Task.WhenAll(call1, call2);

        Assert.Equal(PermissionStatus.Granted, results[0]);
        Assert.Equal(PermissionStatus.Granted, results[1]);
        Assert.Equal(1, adapter.RequestCallCount);
    }

    [Fact]
    public async Task PlatformMapping_Windows_ReturnsGrantedWithoutPrompt()
    {
        var adapter = new DefaultPermissionPlatformAdapter(platform: MauiPlatform.Windows);

        PermissionStatus check = await adapter.CheckStatusAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);
        PermissionStatus request = await adapter.RequestAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionStatus.Granted, check);
        Assert.Equal(PermissionStatus.Granted, request);
    }

    [Fact]
    public async Task PlatformMapping_Android_Api36OrLower_ReturnsGrantedWithoutPrompt()
    {
        var adapter = new DefaultPermissionPlatformAdapter(platform: MauiPlatform.Android, androidApiLevel: 36);

        PermissionStatus check = await adapter.CheckStatusAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);
        PermissionStatus request = await adapter.RequestAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionStatus.Granted, check);
        Assert.Equal(PermissionStatus.Granted, request);
    }

    [Fact]
    public async Task PlatformMapping_Android_Api37OrHigher_InvokesNativePermissionHandlers()
    {
        var adapter = new DefaultPermissionPlatformAdapter(
            platform: MauiPlatform.Android,
            androidApiLevel: 37,
            customCheckHandler: (perm, ct) => Task.FromResult(PermissionStatus.Denied),
            customRequestHandler: (perm, ct) => Task.FromResult(PermissionStatus.Granted));

        PermissionStatus check = await adapter.CheckStatusAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);
        PermissionStatus request = await adapter.RequestAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionStatus.Denied, check);
        Assert.Equal(PermissionStatus.Granted, request);
    }

    [Fact]
    public async Task PlatformMapping_MacCatalyst_ReturnsGranted()
    {
        var adapter = new DefaultPermissionPlatformAdapter(platform: MauiPlatform.MacOS);

        PermissionStatus check = await adapter.CheckStatusAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);
        PermissionStatus request = await adapter.RequestAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionStatus.Granted, check);
        Assert.Equal(PermissionStatus.Granted, request);
    }

    [Fact]
    public async Task PlatformMapping_IOS_ReturnsGranted()
    {
        var adapter = new DefaultPermissionPlatformAdapter(platform: MauiPlatform.IOS);

        PermissionStatus check = await adapter.CheckStatusAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);
        PermissionStatus request = await adapter.RequestAsync(AppPermission.LocalNetwork, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionStatus.Granted, check);
        Assert.Equal(PermissionStatus.Granted, request);
    }

    private sealed class FakePermissionPlatformAdapter : IPermissionPlatformAdapter
    {
        public PermissionStatus StatusToReturn { get; set; } = PermissionStatus.Granted;
        public PermissionStatus RequestStatusToReturn { get; set; } = PermissionStatus.Granted;
        public int CheckCallCount { get; private set; }
        public int RequestCallCount { get; private set; }
        public Func<Task>? OnRequestAsync { get; set; }

        public Task<PermissionStatus> CheckStatusAsync(AppPermission permission, CancellationToken cancellationToken = default)
        {
            CheckCallCount++;
            return Task.FromResult(StatusToReturn);
        }

        public async Task<PermissionStatus> RequestAsync(AppPermission permission, CancellationToken cancellationToken = default)
        {
            RequestCallCount++;
            if (OnRequestAsync != null)
            {
                await OnRequestAsync();
            }
            return RequestStatusToReturn;
        }
    }
}

