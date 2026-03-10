using Microsoft.AspNetCore.Components;

namespace CoreSyncServer.Client.Components;

/// <summary>
/// Base class for pages using InteractiveWebAssembly render mode.
/// Handles the SSR initialization workaround where HttpClient and other
/// services are not available until after the first render.
/// Derived classes should override <see cref="OnInitializedInteractiveAsync"/>
/// instead of OnAfterRenderAsync.
/// </summary>
public abstract class InteractivePage : ComponentBase
{
    [Inject]
    protected HttpClient Http { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await OnInitializedInteractiveAsync();
            StateHasChanged();
        }
    }

    /// <summary>
    /// Called once after the first render when the interactive runtime is ready
    /// and injected services (HttpClient, etc.) are available.
    /// </summary>
    protected virtual Task OnInitializedInteractiveAsync() => Task.CompletedTask;
}
