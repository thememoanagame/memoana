using System.Collections.Concurrent;
using MemoAna.Common.Abstract.Repositories;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Entities;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;

namespace MemoAna.Game.Services;

public sealed partial class AudioService : IAudioService, IDisposable
{
    private const string MenuMusicTrack = "andorios-arcade_music7.mp3";
    private const string GameMusicTrack = "andorios-arcade_music3.mp3";
    private const string FlipSfxTrack = "freesound_community-flipcard.mp3";
    private const string ShuffleSfxTrack = "freesound_community-shuffleandcardflip1.mp3";
    private const string WinSfxTrack = "win_effect.mp3";
    private const string LoseSfxTrack = "lose_effect.mp3";

    private readonly IAudioManager _manager;
    private readonly ILogger<AudioService> _logger;
    private readonly Func<string, Task<Stream>> _fileOpener;

    private readonly SemaphoreSlim _musicLock = new(1, 1);
    private readonly SemaphoreSlim _shuffleLock = new(1, 1);

    private IAudioPlayer? _musicPlayer;
    private Stream? _musicStream;
    private string? _currentMusicTrack;

    private IAudioPlayer? _shufflePlayer;
    private Stream? _shuffleStream;

    private readonly ConcurrentDictionary<IAudioPlayer, Stream> _activeSfx = new();
    private bool _disposed;

    public AudioService(
        IAudioManager manager,
        ILogger<AudioService> logger,
        IRepository<GameSettingsEntity>? repository = null,
        Func<string, Task<Stream>>? fileOpener = null)
    {
        _manager = manager;
        _logger = logger;
        _fileOpener = fileOpener ?? (fileName => FileSystem.OpenAppPackageFileAsync(fileName));
    }

    public bool IsPlaying => _musicPlayer?.IsPlaying ?? false;

    public Task PlayMainTitleAsync() => PlayBackgroundMusicAsync(MenuMusicTrack);

    public Task PlayMainGameAsync() => PlayBackgroundMusicAsync(GameMusicTrack);

    private async Task PlayBackgroundMusicAsync(string trackName)
    {
        if (_disposed)
            return;

        await _musicLock.WaitAsync();
        try
        {
            if (_disposed)
                return;

            if (string.Equals(_currentMusicTrack, trackName, StringComparison.OrdinalIgnoreCase) && _musicPlayer != null)
            {
                if (!_musicPlayer.IsPlaying)
                {
                    _musicPlayer.Play();
                }
                _logger.LogDebug("Background track {Track} is already active; maintaining playback", trackName);
                return;
            }

            _logger.LogInformation("Transitioning background music to {Track}", trackName);
            StopAndDisposeMusicPlayer();

            _musicStream = await _fileOpener(trackName);
            _musicPlayer = _manager.CreatePlayer(_musicStream);
            _musicPlayer.Loop = true;
            _currentMusicTrack = trackName;
            _musicPlayer.Play();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play background music track {Track}", trackName);
            StopAndDisposeMusicPlayer();
        }
        finally
        {
            _musicLock.Release();
        }
    }

    public async Task PlayShuffleFlipAsync()
    {
        if (_disposed)
            return;

        await _shuffleLock.WaitAsync();
        try
        {
            if (_disposed)
                return;

            StopAndDisposeShufflePlayer();

            _logger.LogInformation("Starting board loading shuffle SFX: {Track}", ShuffleSfxTrack);
            _shuffleStream = await _fileOpener(ShuffleSfxTrack);
            _shufflePlayer = _manager.CreatePlayer(_shuffleStream);
            _shufflePlayer.Loop = false;
            _shufflePlayer.PlaybackEnded += OnShufflePlaybackEnded;
            _shufflePlayer.Play();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play shuffle SFX");
            StopAndDisposeShufflePlayer();
        }
        finally
        {
            _shuffleLock.Release();
        }
    }

    public async Task StopShuffleAsync()
    {
        await _shuffleLock.WaitAsync();
        try
        {
            StopAndDisposeShufflePlayer();
        }
        finally
        {
            _shuffleLock.Release();
        }
    }

    private void OnShufflePlaybackEnded(object? sender, System.EventArgs e)
    {
        _ = StopShuffleAsync();
    }

    public Task PlayFlipAsync() => PlayOneShotSfxAsync(FlipSfxTrack);

    public Task PlayWinAsync() => PlayOneShotSfxAsync(WinSfxTrack);

    public Task PlayLoseAsync() => PlayOneShotSfxAsync(LoseSfxTrack);

    private async Task PlayOneShotSfxAsync(string fileName)
    {
        if (_disposed)
            return;

        try
        {
            Stream stream = await _fileOpener(fileName);
            IAudioPlayer player = _manager.CreatePlayer(stream);
            player.Loop = false;

            _activeSfx[player] = stream;

            player.PlaybackEnded += (sender, args) =>
            {
                CleanupSfxInstance(player);
            };

            player.Play();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play SFX {FileName}", fileName);
        }
    }

    private void CleanupSfxInstance(IAudioPlayer player)
    {
        if (_activeSfx.TryRemove(player, out Stream? stream))
        {
            try
            {
                if (player.IsPlaying)
                {
                    player.Stop();
                }
                player.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing SFX player");
            }

            try
            {
                stream?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing SFX stream");
            }
        }
    }

    public async Task StopAsync()
    {
        await _musicLock.WaitAsync();
        try
        {
            StopAndDisposeMusicPlayer();
        }
        finally
        {
            _musicLock.Release();
        }

        await _shuffleLock.WaitAsync();
        try
        {
            StopAndDisposeShufflePlayer();
        }
        finally
        {
            _shuffleLock.Release();
        }

        StopAndDisposeAllSfx();
    }

    private void StopAndDisposeMusicPlayer()
    {
        if (_musicPlayer != null)
        {
            try
            {
                if (_musicPlayer.IsPlaying)
                    _musicPlayer.Stop();
                _musicPlayer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing background music player");
            }
            _musicPlayer = null;
        }

        if (_musicStream != null)
        {
            try
            {
                _musicStream.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing background music stream");
            }
            _musicStream = null;
        }

        _currentMusicTrack = null;
    }

    private void StopAndDisposeShufflePlayer()
    {
        if (_shufflePlayer != null)
        {
            try
            {
                _shufflePlayer.PlaybackEnded -= OnShufflePlaybackEnded;
                if (_shufflePlayer.IsPlaying)
                    _shufflePlayer.Stop();
                _shufflePlayer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing shuffle player");
            }
            _shufflePlayer = null;
        }

        if (_shuffleStream != null)
        {
            try
            {
                _shuffleStream.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing shuffle stream");
            }
            _shuffleStream = null;
        }
    }

    private void StopAndDisposeAllSfx()
    {
        foreach (var player in _activeSfx.Keys)
        {
            CleanupSfxInstance(player);
        }
        _activeSfx.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        StopAndDisposeMusicPlayer();
        StopAndDisposeShufflePlayer();
        StopAndDisposeAllSfx();

        _musicLock.Dispose();
        _shuffleLock.Dispose();
    }
}
