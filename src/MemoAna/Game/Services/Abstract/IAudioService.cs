namespace MemoAna.Game.Services.Abstract;

public interface IAudioService  
{
    Task PlayFlipAsync();
    Task PlayLoseAsync();
    Task PlayMainGameAsync();
    Task PlayMainTitleAsync();
    Task PlayShuffleFlipAsync();
    Task PlayWinAsync();
    Task StopAsync();
    bool IsPlaying { get; }
}
