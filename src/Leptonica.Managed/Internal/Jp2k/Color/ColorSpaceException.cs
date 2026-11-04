#nullable enable annotations
#nullable disable warnings
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, CS1591, CS8981, SYSLIB0011, SYSLIB0014
/// <summary>**************************************************************************
/// 
/// 
/// Copyright Eastman Kodak Company, 343 State Street, Rochester, NY 14650
/// $Date $
/// ***************************************************************************
/// </summary>
using System;
namespace CoreJ2K.Color
{

    /// <summary> This exception is thrown when the content of an
    /// image contains an incorrect colorspace box
    /// 
    /// </summary>
    /// <seealso cref="j2k.colorspace.ColorSpaceMapper" />
    /// <version> 	1.0
    /// </version>
    /// <author> 	Bruce A. Kern
    /// </author>

    internal class ColorSpaceException : Exception
    {

        /// <summary> Contruct with message</summary>
        /// <param name="msg">returned by getMessage()
        /// </param>
        public ColorSpaceException(string msg) : base(msg)
        {
        }


        /// <summary> Empty constructor</summary>
        public ColorSpaceException()
        {
        }
    }
}