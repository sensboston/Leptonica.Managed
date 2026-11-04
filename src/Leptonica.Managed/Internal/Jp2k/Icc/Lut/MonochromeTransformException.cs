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
namespace CoreJ2K.Icc.Lut
{

    /// <summary> Exception thrown by MonochromeTransformTosRGB.
    /// 
    /// </summary>
    /// <seealso cref="j2k.icc.lut.MonochromeTransformTosRGB" />
    /// <version> 	1.0
    /// </version>
    /// <author> 	Bruce A. Kern
    /// </author>

    internal class MonochromeTransformException : Exception
    {

        /// <summary> Contruct with message</summary>
        /// <param name="msg">returned by getMessage()
        /// </param>
        internal MonochromeTransformException(string msg) : base(msg)
        {
        }

        /// <summary> Empty constructor</summary>
        internal MonochromeTransformException()
        {
        }
    }
}