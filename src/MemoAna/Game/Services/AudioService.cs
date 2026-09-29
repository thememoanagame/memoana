using MemoAna.Common.Abstract.Repositories;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Entities;
using MemoAna.Game.Models;
using Plugin.Maui.Audio;

namespace MemoAna.ame.Services;

public sealed partial class AudioService(IAudioManager manager, IRepository<GameSettingsEntity> repository, ILogger<AudioService> logger) : IAudioService, IDisposable
{
    private readonly IAudioManager _manager = manager;
    private IAudioPlayer? _player;
    private Stream? _currentStream;

    public bool IsPlaying => _player?.IsPlaying ?? false;

    private async Task InitializeAsync(string fileName, bool loop = false)
    {
        logger.LogInformation("Initializing audio player for {FileName}", fileName);
        try
        {
            // 1. Previous player and stream cleanup to free the hardware channel
            CleanUpCurrentPlayer();

            // 2. Safelly open the stream
            _currentStream = await FileSystem.OpenAppPackageFileAsync(fileName);

            // 3. Player creation and loop activation if background audio
            _player = _manager.CreatePlayer(_currentStream);

            // 4. If is main title or main game, set continuous loop
            _player.Loop = loop;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioService] Erro ao carregar arquivo {fileName}: {ex.Message}");
            throw;
        }
    }
    public async Task PlayFlipAsync()
    {
        GameSettingsEntity? currentConfig = (await repository.ListAsync(x => true, null!, CancellationToken.None)).FirstOrDefault();
        if (currentConfig is GameSettingsEntity gs && gs.Options is GameOptions go && !go.IsSfxEnabled)
            return;

        await InitializeAsync("freesound_community-flipcard.mp3");
        _player?.Play();
    }

    public async Task PlayLoseAsync()
    {
        GameSettingsEntity? currentConfig = (await repository.ListAsync(x => true, null!, CancellationToken.None)).FirstOrDefault();
        if (currentConfig is GameSettingsEntity gs && gs.Options is GameOptions go && !go.IsMusicEnabled)
            return;

        await InitializeAsync("lose_effect.mp3");
        _player?.Play();
    }

    public async Task PlayMainGameAsync()
    {
        GameSettingsEntity? currentConfig = (await repository.ListAsync(x => true, null!, CancellationToken.None)).FirstOrDefault();
        if (currentConfig is GameSettingsEntity gs && gs.Options is GameOptions go && !go.IsMusicEnabled)
            return;

        await InitializeAsync("andorios-arcade_music3.mp3", true);
        _player?.Play();
    }

    public async Task PlayMainTitleAsync()
    {
        GameSettingsEntity? currentConfig = (await repository.ListAsync(x => true, null!, CancellationToken.None)).FirstOrDefault();
        if (currentConfig is GameSettingsEntity gs && gs.Options is GameOptions go && !go.IsMusicEnabled)
            return;

        await InitializeAsync("andorios-arcade_music7.mp3", true);
        _player?.Play();
    }

    public async Task PlayShuffleFlipAsync()
    {
        GameSettingsEntity? currentConfig = (await repository.ListAsync(x => true, null!, CancellationToken.None)).FirstOrDefault();
        if (currentConfig is GameSettingsEntity gs && gs.Options is GameOptions go && !go.IsSfxEnabled)
            return;

        await InitializeAsync("freesound_community-shuffleandcardflip1.mp3");
        _player?.Play();
    }

    public async Task PlayWinAsync()
    {
        GameSettingsEntity? currentConfig = (await repository.ListAsync(x => true, null!, CancellationToken.None)).FirstOrDefault();
        if (currentConfig is GameSettingsEntity gs && gs.Options is GameOptions go && !go.IsMusicEnabled)
            return;

        await InitializeAsync("win_effect.mp3");
        _player?.Play();
    }

    public async Task StopAsync()
    {
        if (_player != null && _player.IsPlaying)
        {
            _player.Stop();
        }
        CleanUpCurrentPlayer();
    }

    private void CleanUpCurrentPlayer()
    {
        if (_player != null)
        {
            _player.Stop();
            _player.Dispose();
            _player = null;
        }

        if (_currentStream != null)
        {
            _currentStream.Close();
            _currentStream.Dispose();
            _currentStream = null;
        }
    }

    public void Dispose()
    {
        CleanUpCurrentPlayer();
    }
}