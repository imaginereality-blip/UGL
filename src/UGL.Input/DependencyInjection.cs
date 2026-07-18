using Microsoft.Extensions.DependencyInjection;
using UGL.Core.Interfaces;

namespace UGL.Input;

public static class DependencyInjection
{
    public static IServiceCollection AddInputServices(this IServiceCollection services)
    {
        // XInput (gamepads)
        services.AddSingleton<XInputPollingService>();
        services.AddSingleton<IInputService>(sp => sp.GetRequiredService<XInputPollingService>());
        services.AddSingleton<KeyboardInputService>();

        // RawInput (lightguns, wheels, spinners, multi-device)
        services.AddSingleton<IPeripheralRegistry, PeripheralRegistry>();
        services.AddSingleton<IRawInputService, RawInputService>();

        return services;
    }
}
