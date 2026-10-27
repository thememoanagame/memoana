using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.Maui.Audio;
using Xunit;

namespace MemoAna.UnitTests.Services;

public sealed class AudioServiceTests
{
    private sealed class FakeAudioPlayer : IAudioPlayer
    {
        public double Duration => 100;
        public double CurrentPosition { get; set; }
        public double Volume { get; set; } = 1.0;
        public double Balance { get; set; }
        public bool IsPlaying { get; set; }
        public bool Loop { get; set; }
        public bool CanSeek => true;
        public double Speed { get; set; } = 1.0;
        public double MinimumSpeed => 0.5;
        public double MaximumSpeed => 2.0;
        public bool CanSetSpeed => true;

        public int PlayCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public event EventHandler? PlaybackEnded;
        public event EventHandler? Error;

        public void Play()
        {
            IsPlaying = true;
            PlayCount++;
        }

        public void Pause()
        {
            IsPlaying = false;
        }

        public void Stop()
        {
            IsPlaying = false;
            StopCount++;
        }

        public void Seek(double position)
        {
            CurrentPosition = position;
        }

        public void SetSource(Stream stream)
        {
        }

        public void TriggerPlaybackEnded()
        {
            IsPlaying = false;
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            IsPlaying = false;
            DisposeCount++;
        }
    }

    private sealed class FakeAudioManager : IAudioManager
    {
        public List<(Stream Stream, FakeAudioPlayer Player)> CreatedPlayers { get; } = [];

        public AudioPlayerOptions? DefaultPlayerOptions { get; set; }
        public AudioRecorderOptions? DefaultRecorderOptions { get; set; }
        public AudioStreamOptions? DefaultStreamerOptions { get; set; }

        public IAudioPlayer CreatePlayer(AudioPlayerOptions? options = null)
        {
            var player = new FakeAudioPlayer();
            CreatedPlayers.Add((new MemoryStream(), player));
            return player;
        }

        public IAudioPlayer CreatePlayer(Stream audioStream, AudioPlayerOptions? options = null)
        {
            var player = new FakeAudioPlayer();
            CreatedPlayers.Add((audioStream, player));
            return player;
        }

        public IAudioPlayer CreatePlayer(string fileName, AudioPlayerOptions? options = null)
        {
            var player = new FakeAudioPlayer();
            CreatedPlayers.Add((new MemoryStream(), player));
            return player;
        }

        public IAudioPlayer CreatePlayer(Stream audioStream) => CreatePlayer(audioStream, null);
        public IAudioPlayer CreatePlayer(string fileName) => CreatePlayer(fileName, null);

        public AsyncAudioPlayer CreateAsyncPlayer(Stream audioStream, AudioPlayerOptions? options = null) => throw new NotImplementedException();
        public AsyncAudioPlayer CreateAsyncPlayer(string fileName, AudioPlayerOptions? options = null) => throw new NotImplementedException();

        public IAudioRecorder CreateRecorder(AudioRecorderOptions? options = null) => throw new NotImplementedException();
        public IAudioStreamer CreateStreamer() => throw new NotImplementedException();
    }

    private static (AudioService service, FakeAudioManager manager, List<string> requestedFiles) CreateSut()
    {
        var manager = new FakeAudioManager();
        var requestedFiles = new List<string>();
        Task<Stream> FileOpener(string fileName)
        {
            requestedFiles.Add(fileName);
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        }

        var service = new AudioService(manager, NullLogger<AudioService>.Instance, null, FileOpener);
        return (service, manager, requestedFiles);
    }

    [Fact]
    public async Task PlayMainTitleAsync_StartsMenuMusicInLoop()
    {
        var (service, manager, files) = CreateSut();

        await service.PlayMainTitleAsync();

        Assert.Single(files);
        Assert.Equal("andorios-arcade_music7.mp3", files[0]);
        Assert.Single(manager.CreatedPlayers);
        var player = manager.CreatedPlayers[0].Player;
        Assert.True(player.Loop);
        Assert.True(player.IsPlaying);
        Assert.Equal(1, player.PlayCount);
        Assert.True(service.IsPlaying);
    }

    [Fact]
    public async Task PlayMainTitleAsync_WhenAlreadyPlayingMusic7_IsIdempotentAndDoesNotRestart()
    {
        var (service, manager, files) = CreateSut();

        await service.PlayMainTitleAsync();
        Assert.Single(files);
        Assert.Single(manager.CreatedPlayers);
        var player = manager.CreatedPlayers[0].Player;

        // Second call while music7 is already playing
        await service.PlayMainTitleAsync();

        // No new file opened, no new player created, player not stopped or restarted
        Assert.Single(files);
        Assert.Single(manager.CreatedPlayers);
        Assert.Equal(1, player.PlayCount);
        Assert.Equal(0, player.StopCount);
        Assert.Equal(0, player.DisposeCount);
    }

    [Fact]
    public async Task PlayMainGameAsync_StartsGameMusicInLoop()
    {
        var (service, manager, files) = CreateSut();

        await service.PlayMainGameAsync();

        Assert.Single(files);
        Assert.Equal("andorios-arcade_music3.mp3", files[0]);
        Assert.Single(manager.CreatedPlayers);
        var player = manager.CreatedPlayers[0].Player;
        Assert.True(player.Loop);
        Assert.True(player.IsPlaying);
        Assert.Equal(1, player.PlayCount);
    }

    [Fact]
    public async Task Transition_FromMenuToGame_DisposesOldPlayerAndStartsNewMusicInLoop()
    {
        var (service, manager, files) = CreateSut();

        await service.PlayMainTitleAsync();
        var menuPlayer = manager.CreatedPlayers[0].Player;
        Assert.True(menuPlayer.IsPlaying);

        await service.PlayMainGameAsync();

        // Menu player was stopped and disposed
        Assert.False(menuPlayer.IsPlaying);
        Assert.Equal(1, menuPlayer.StopCount);
        Assert.Equal(1, menuPlayer.DisposeCount);

        // New game player created and playing music3 in loop
        Assert.Equal(2, manager.CreatedPlayers.Count);
        var gamePlayer = manager.CreatedPlayers[1].Player;
        Assert.Equal("andorios-arcade_music3.mp3", files[1]);
        Assert.True(gamePlayer.Loop);
        Assert.True(gamePlayer.IsPlaying);
    }

    [Fact]
    public async Task PlayFlipAsync_PlaysSfxWithoutInterruptingBackgroundMusic()
    {
        var (service, manager, files) = CreateSut();

        await service.PlayMainGameAsync();
        var musicPlayer = manager.CreatedPlayers[0].Player;
        Assert.True(musicPlayer.IsPlaying);

        // Play card flip SFX
        await service.PlayFlipAsync();

        // Music player was NOT stopped or disposed
        Assert.True(musicPlayer.IsPlaying);
        Assert.Equal(0, musicPlayer.StopCount);
        Assert.Equal(0, musicPlayer.DisposeCount);

        // Flip SFX player was created and played
        Assert.Equal(2, manager.CreatedPlayers.Count);
        Assert.Equal("freesound_community-flipcard.mp3", files[1]);
        var sfxPlayer = manager.CreatedPlayers[1].Player;
        Assert.False(sfxPlayer.Loop);
        Assert.True(sfxPlayer.IsPlaying);
        Assert.Equal(1, sfxPlayer.PlayCount);
    }

    [Fact]
    public async Task PlayShuffleFlipAsync_RunsConcurrentlyWithMusic3_AndStopsCleanly()
    {
        var (service, manager, files) = CreateSut();

        // 1. Board loading begins: music3 starts in loop
        await service.PlayMainGameAsync();
        var musicPlayer = manager.CreatedPlayers[0].Player;

        // 2. Shuffle SFX starts concurrently
        await service.PlayShuffleFlipAsync();
        Assert.Equal(2, manager.CreatedPlayers.Count);
        Assert.Equal("freesound_community-shuffleandcardflip1.mp3", files[1]);
        var shufflePlayer = manager.CreatedPlayers[1].Player;

        // Both are active concurrently
        Assert.True(musicPlayer.IsPlaying);
        Assert.True(shufflePlayer.IsPlaying);
        Assert.False(shufflePlayer.Loop);

        // 3. Board loading finishes: StopShuffleAsync is called
        await service.StopShuffleAsync();

        // Shuffle player was stopped and disposed
        Assert.False(shufflePlayer.IsPlaying);
        Assert.Equal(1, shufflePlayer.StopCount);
        Assert.Equal(1, shufflePlayer.DisposeCount);

        // Background music is STILL playing!
        Assert.True(musicPlayer.IsPlaying);
        Assert.Equal(0, musicPlayer.StopCount);
        Assert.Equal(0, musicPlayer.DisposeCount);
    }

    [Fact]
    public async Task Sfx_DisposesPlayerAndStreamWhenPlaybackEnds()
    {
        var (service, manager, files) = CreateSut();

        await service.PlayFlipAsync();
        var sfxPlayer = manager.CreatedPlayers[0].Player;
        Assert.True(sfxPlayer.IsPlaying);

        // Simulate playback ending
        sfxPlayer.TriggerPlaybackEnded();

        // Player was disposed
        Assert.Equal(1, sfxPlayer.DisposeCount);
    }

    [Fact]
    public async Task ReturnToMenu_SwitchesFromGameMusic3ToMenuMusic7()
    {
        var (service, manager, files) = CreateSut();

        // Gameplay / statistics: music3
        await service.PlayMainGameAsync();
        var gamePlayer = manager.CreatedPlayers[0].Player;

        // User navigates back to menu / home: music7
        await service.PlayMainTitleAsync();

        Assert.False(gamePlayer.IsPlaying);
        Assert.Equal(1, gamePlayer.DisposeCount);

        var menuPlayer = manager.CreatedPlayers[1].Player;
        Assert.True(menuPlayer.IsPlaying);
        Assert.Equal("andorios-arcade_music7.mp3", files[1]);
    }

    [Fact]
    public async Task StopAsync_StopsAndDisposesAllActivePlayers()
    {
        var (service, manager, _) = CreateSut();

        await service.PlayMainGameAsync();
        await service.PlayShuffleFlipAsync();
        await service.PlayFlipAsync();

        var musicPlayer = manager.CreatedPlayers[0].Player;
        var shufflePlayer = manager.CreatedPlayers[1].Player;
        var sfxPlayer = manager.CreatedPlayers[2].Player;

        await service.StopAsync();

        Assert.False(musicPlayer.IsPlaying);
        Assert.Equal(1, musicPlayer.DisposeCount);
        Assert.False(shufflePlayer.IsPlaying);
        Assert.Equal(1, shufflePlayer.DisposeCount);
        Assert.False(sfxPlayer.IsPlaying);
        Assert.Equal(1, sfxPlayer.DisposeCount);
    }
}

