// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

// Required polyfill so C# 9+ init-only setters compile on .NET Framework 4.8.1
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
