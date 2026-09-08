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
