using TimesGateMonitor.Core.Interfaces;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

public sealed class HardwareDashboardRenderer : IHardwareDashboardRenderer
{
    private readonly IReadOnlyList<IHardwareScreenRenderer> _screenRenderers;

    public HardwareDashboardRenderer(IEnumerable<IHardwareScreenRenderer> screenRenderers)
    {
        _screenRenderers = screenRenderers.OrderBy(renderer => renderer.ScreenIndex).ToArray();

        if (_screenRenderers.Count != DisplayFrame.ScreenCount ||
            _screenRenderers.Select(renderer => renderer.ScreenIndex).Distinct().Count() != DisplayFrame.ScreenCount)
        {
            throw new InvalidOperationException("Exactly one renderer for each Times Gate screen 1-5 is required.");
        }
    }

    public IReadOnlyList<DisplayFrame> Render(
        HardwareSnapshot snapshot,
        DashboardRenderOptions options) =>
        _screenRenderers.Select(renderer => renderer.Render(snapshot, options)).ToArray();
}
