using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace MemoAna.Backend.Composition.Extensions;
/// <summary>
/// 
/// </summary>

public static class WebApplicationExtensions
{
    extension(WebApplication app)
    {
        /// <summary>
        /// Overload Extension Method to setup http pipeline and run the app. 
        /// </summary>
        public async Task RunMemoAnaAsync<T>() where T : Microsoft.AspNetCore.Components.IComponent
        {
            
            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                _ = app.UseExceptionHandler("/Error", createScopeForErrors: true);
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                _ = app.UseHsts();
            }
            else
            {
                _ = app.MapOpenApi("ma/{v1}.json").AllowAnonymous();
                _ = app.MapScalarApiReference("ma/scalar", async options =>
                {
                    _ = options.WithOpenApiRoutePattern("/ma/{documentName}.json");
                    _ = options.WithTitle($"MemoAna Backend: [{app.Environment.EnvironmentName}]");
                    options.HeadContent = @"
                    <!-- MudBlazor JavaScript, Fonts & base CSS  -->
                    <link href=""https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap"" rel=""stylesheet"" />
                    <link href=""_content/MudBlazor/MudBlazor.min.css"" rel=""stylesheet"" />
                    <script src=""_content/MudBlazor/MudBlazor.min.js""></script>
                    <!-- Sync CSS Class and Theme Script -->
                    <script>
                        document.addEventListener('DOMContentLoaded', () => {
                            // Adds the mud-application class at body to activate MudBlazor scopes.
                            document.body.classList.add('mud-application', 'mud-theme-primary');
                            // Ensures thats the Scalar theme attribute reflects the MudBlazor dark state
                            const updateScalarTheme = () => {
                                const isDark = document.body.classList.contains('mud-dark-theme');
                                document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
                            };
                            // Observes class mutations at body to alternate theme in real-time
                            const observer = new MutationObserver(updateScalarTheme);
                            observer.observe(document.body, { attributes: true, attributeFilter: ['class'] });
                            updateScalarTheme();
                        });
                    </script>
                ";
                    _ = options.WithCustomCss(@"
                    :root {
                        --scalar-background-1: var(--mud-palette-surface, #ffffff);
                        --scalar-background-2: var(--mud-palette-background, #f5f5f5);
                        --scalar-background-3: var(--mud-palette-background-gray, #e0e0e0);
                        --scalar-background-accent: var(--mud-palette-action-default-hover, rgba(0,0,0,0.04));
                        --scalar-color-1: var(--mud-palette-text-primary, #424242);
                        --scalar-color-2: var(--mud-palette-text-secondary, #616161);
                        --scalar-color-3: var(--mud-palette-text-disabled, #9e9e9e);
                        --scalar-color-accent: var(--mud-palette-primary, #594ae2);
                        --scalar-button-1: var(--mud-palette-primary, #594ae2);
                        --scalar-button-1-color: var(--mud-palette-primary-text, #ffffff);
                        --scalar-button-1-hover: var(--mud-palette-primary-darken, #3d2cc4);
                        --scalar-border-color: var(--mud-palette-lines-default, #e0e0e0);
                        --scalar-radius: var(--mud-default-borderradius, 4px);
                        --scalar-font: 'Roboto', sans-serif;
                        --scalar-font-code: 'Roboto Mono', monospace;
                    }
                    .mud-dark-theme, [data-theme='dark'] {
                        --scalar-background-1: var(--mud-palette-surface, #1e1e2d);
                        --scalar-background-2: var(--mud-palette-background, #151521);
                        --scalar-background-3: var(--mud-palette-background-gray, #27273a);
                        --scalar-color-1: var(--mud-palette-text-primary, #ffffff);
                        --scalar-color-2: var(--mud-palette-text-secondary, #a1a5b7);
                        --scalar-border-color: var(--mud-palette-lines-default, #2b2b40);
                    }
                    .scalar-api-reference {
                        font-family: var(--scalar-font);
                        background-color: var(--scalar-background-1);
                        color: var(--scalar-color-1);
                    }
                    .scalar-card, .section {
                        border-radius: var(--mud-default-borderradius, 4px) !important;
                        box-shadow: var(--mud-elevation-1, 0px 2px 1px -1px rgba(0,0,0,0.2)) !important;
                    }
                ");
                });
            }
            _ = app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            _ = app.UseHttpsRedirection();
            _ = app.UseAuthentication();
            _ = app.UseAuthorization();
            _ = app.UseAntiforgery();

            _ = app.MapStaticAssets();
            _ = app.MapControllers();
            _ = app.MapRazorComponents<T>()
                .AddInteractiveServerRenderMode();

            await app.RunAsync();
        }
    }   
}
