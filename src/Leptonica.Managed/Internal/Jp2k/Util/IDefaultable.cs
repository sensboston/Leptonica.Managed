#nullable enable annotations
#nullable disable warnings
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, CS1591, CS8981, SYSLIB0011, SYSLIB0014
// Copyright (c) 2012-2016 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

namespace CoreJ2K.Util
{
    /// <summary>
    /// Interface for default classification of manager types.
    /// </summary>
    internal interface IDefaultable
    {
        /// <summary>
        /// Gets whether or not this type is classified as a default manager.
        /// </summary>
        bool IsDefault { get; }
    }
}