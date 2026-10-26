namespace MemoAna;

public partial class MainPage : ContentPage
{
    private bool _hasSynced;
    private readonly ILogger<MainPage> logger;
    private readonly Game.Abstract.Services.IAssetService seedService;
    public MainPage(Game.Abstract.Services.IAssetService seedService, ILogger<MainPage> logger)
    {
        InitializeComponent();
        this.logger = logger;
        this.seedService = seedService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_hasSynced)
            return;

        _hasSynced = true;

        try
        {
            await seedService.SyncAndSeedAssetsAsync();
        }
        catch (Exception e)
        {
            _hasSynced = false;
            logger.LogError(e, "Error seeding theme data: {Message}", e.Message);
        }
    }
}
