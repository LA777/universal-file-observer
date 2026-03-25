using Microsoft.Extensions.Options;
using Ufo.FunctionalTests.LabelController;

namespace Ufo.FunctionalTests.Extensions;

internal static class OptionsMonitorStubExtensions
{
    /// <summary>Creates an <see cref="IOptionsMonitor{T}"/> wrapping <paramref name="value"/>.</summary>
    public static IOptionsMonitor<T> ToOptionsMonitor<T>(this T value) where T : class =>
        new OptionsMonitorStub<T>(value);
}
