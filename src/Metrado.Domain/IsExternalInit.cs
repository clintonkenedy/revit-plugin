#if !NET
// Polyfill required by decision D1. C# emits a modreq on IsExternalInit for
// every `init` accessor and every positional record, and netstandard2.0 does
// not ship that type. Declaring it here lets the netstandard2.0 leg compile
// the same immutable records as the net10.0 leg, with no package reference.
// It is compiled out on net10.0, where the framework provides the type.

namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
#endif
