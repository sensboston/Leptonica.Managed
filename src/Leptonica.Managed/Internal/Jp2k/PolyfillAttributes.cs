#nullable enable annotations
#nullable disable warnings
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, CS1591, CS8981, SYSLIB0011, SYSLIB0014
// Copyright (c) 2025 Sjofn LLC.
// Licensed under the BSD 3-Clause License.

// Inline shims for trimming/AOT annotation attributes missing from netstandard2.x.
// The real types ship in-box starting with .NET 5.
#if !NET5_0_OR_GREATER
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
    internal sealed class RequiresUnreferencedCodeAttribute : Attribute
    {
        public RequiresUnreferencedCodeAttribute(string message) => Message = message;
        public string Message { get; }
        public string? Url { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
    internal sealed class RequiresDynamicCodeAttribute : Attribute
    {
        public RequiresDynamicCodeAttribute(string message) => Message = message;
        public string Message { get; }
        public string? Url { get; set; }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    internal sealed class UnconditionalSuppressMessageAttribute : Attribute
    {
        public UnconditionalSuppressMessageAttribute(string category, string checkId) { }
        public string? Scope { get; set; }
        public string? Target { get; set; }
        public string? MessageId { get; set; }
        public string? Justification { get; set; }
    }
}
#endif
