#if WINDOWS
using Microsoft.UI.Windowing;
#endif

namespace MemoAna;
public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly MainPage page;
    public App(MainPage page)
    {
        InitializeComponent();
        this.page = page;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var w =  new Window(page) { Title = "MemoAna" };
        // Configuração exclusiva para a plataforma Windows
#if WINDOWS
        w.Created += (sender, e) =>
        {
            // Obtém a janela nativa do WinUI3 vinculada à janela do MAUI
            var nativeWindow = w.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (nativeWindow != null)
            {
                // Obtém o identificador da janela (WindowId)
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
            
                // Obtém a AppWindow nativa do ecossistema Windows App SDK
                var appWindow = AppWindow.GetFromWindowId(windowId);

                // Altera o tipo de exibição diretamente para Tela Cheia (oculta barras e menu)
                appWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
        };
#endif
        return w;
    }
}
