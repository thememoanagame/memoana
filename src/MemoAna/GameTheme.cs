using MudBlazor;
namespace MemoAna;
public static class GameTheme
{
    public static MudTheme Theme = new MudTheme()
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#693D22",         
            Secondary = "#C88451",       
            Tertiary = "#DBA977",        

            Background = "#FDF8EC",      
            Surface = "#F7EFE0",         
            AppbarBackground = "#693D22",
            AppbarText = "#FDF8EC",      
            DrawerBackground = "#FDF8EC",
            DrawerText = "#3A2213",

            TextPrimary = "#2A180E",     
            TextSecondary = "#5C3E2B",   

            Success = "#5B7541",         
            Error = "#A84438",           
            Warning = "#D49A36",         
            Info = "#487586",            

            LinesDefault = "#E0D1B8",
            Divider = "#E0D1B8",
            ActionDefault = "#693D22",
            ActionDisabled = "#C8B9A6"
        },

        PaletteDark = new PaletteDark()
        {
            Primary = "#C88451",         
            Secondary = "#DBA977",       
            Tertiary = "#693D22",        

            Background = "#1A100A",      
            Surface = "#261810",         
            AppbarBackground = "#1A100A",
            AppbarText = "#FDF8EC",
            DrawerBackground = "#1A100A",
            DrawerText = "#E6D7C3",

            TextPrimary = "#FDF8EC",     
            TextSecondary = "#C8B9A6",   

            Success = "#81A35F",         
            Error = "#CF6355",           
            Warning = "#EBB14D",         
            Info = "#6AA2B5",            

            LinesDefault = "#3D271A",
            Divider = "#3D271A",
            ActionDefault = "#C88451",
            ActionDisabled = "#4A3324"
        },

        Typography = new Typography()
        {
            Default = new DefaultTypography()
            {
                FontFamily = ["Georgia", "Merriweather", "serif"], 
                FontSize = "1rem",
                FontWeight = "400",
                LineHeight = "1.5",
                LetterSpacing = ".00938em"
            },
            H1 = new H1Typography() { FontFamily = ["Cinzel", "Georgia", "serif"], FontWeight = "700" },
            H2 = new H2Typography() { FontFamily = ["Cinzel", "Georgia", "serif"], FontWeight = "600" },
            H3 = new H3Typography() { FontFamily = ["Cinzel", "Georgia", "serif"], FontWeight = "500" },
            Button = new ButtonTypography()
            {
                FontFamily = ["Georgia", "serif"],
                FontWeight = "600",
                TextTransform = "none" 
            }
        },

        LayoutProperties = new LayoutProperties()
        {
            DefaultBorderRadius = "8px", 
            AppbarHeight = "72px" 
        }
    };
}