#if NET481
namespace System.Runtime.CompilerServices
{
    using System;
    using System.ComponentModel;

    /// <summary>
    /// Polyfill for .NET Framework 4.8.1 compatibility with init-only properties.
    /// This attribute is automatically defined in .NET 5.0+.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class IsExternalInit : Attribute
    {
    }
}
#endif
