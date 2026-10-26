using Editor.Models;
using Editor.Services.Abstract;
using System.Collections.ObjectModel;

namespace Editor;

public partial class MainPage : ContentPage
{
	private readonly IThemeService _themeService;
	private string _currentTheme = string.Empty;
	public ObservableCollection<CardItem> CurrentCards { get; set; } = [];

	public MainPage(IThemeService themeService)
	{
		InitializeComponent();
		_themeService = themeService;
		BindingContext = this;
	}

	private async void OnSelectRepoClicked(object sender, EventArgs e)
	{
		// NOTA: Para cenários de produção no MAUI, utilize o FolderPicker do CommunityToolkit.Maui.
		// Aqui simulamos a inserção do caminho raiz via Prompt para manter o código enxuto.
		string rootPath = await DisplayPromptAsync("Repositório", "Cole o caminho absoluto do repositório clonado:");

		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return;

		LblCurrentRepo.Text = rootPath;
		var themes = await _themeService.LoadRepositoryAsync(rootPath);
		CvThemes.ItemsSource = themes;
	}

	private async void OnThemeSelected(object sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection[0] is string themeName)
		{
			_currentTheme = themeName;
			var cards = await _themeService.LoadThemeCardsAsync(themeName);

			CurrentCards.Clear();
			foreach (var card in cards)
				CurrentCards.Add(card);
		}
	}

	private async void OnSaveThemeClicked(object sender, EventArgs e)
	{
		if (string.IsNullOrEmpty(_currentTheme) || !CurrentCards.Any()) return;

		await _themeService.SaveLocalizationAsync(_currentTheme, [.. CurrentCards]);
		await DisplayAlertAsync("Sucesso", "Textos salvos no localization.json e IDs mapeados no registry.", "OK");
	}

	private async void OnBuildApiClicked(object sender, EventArgs e)
	{
		try
		{
			await _themeService.BuildStaticApiAsync();
			await DisplayAlertAsync("Build Concluído", "Pasta /public gerada com sucesso. Pronto para commit!", "OK");
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Erro", ex.Message, "OK");
		}
	}
}