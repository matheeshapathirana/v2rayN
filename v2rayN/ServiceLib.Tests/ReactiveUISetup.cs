using ReactiveUI.Builder;
using ReactiveUI.Primitives.Concurrency;

namespace ServiceLib.Tests;

/// <summary>
/// View models use WhenAnyValue, which requires ReactiveUI to be initialized once per process
/// (the apps do this in their startup code).
/// </summary>
public static class ReactiveUISetup
{
    [Before(HookType.Assembly)]
    public static void InitializeReactiveUI()
    {
        _ = RxAppBuilder.CreateReactiveUIBuilder()
            .WithMainThreadScheduler(Sequencer.Immediate)
            .WithCoreServices()
            .BuildApp();
    }
}
