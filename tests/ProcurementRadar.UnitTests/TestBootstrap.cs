using System.Runtime.CompilerServices;
using System.Text;

namespace ProcurementRadar.UnitTests;

internal static class TestBootstrap
{
    /// <summary>
    /// Makes windows-1254 and iso-8859-9 available to every test in the
    /// assembly.
    /// </summary>
    /// <remarks>
    /// In production only BodyDecoder needs them and its static constructor does
    /// this. Tests that build such a body without going through BodyDecoder were
    /// relying on some other test class having been constructed first, which xUnit
    /// makes no promises about: they passed, until adding an unrelated test class
    /// changed the order.
    /// </remarks>
    [ModuleInitializer]
    internal static void Initialize() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
