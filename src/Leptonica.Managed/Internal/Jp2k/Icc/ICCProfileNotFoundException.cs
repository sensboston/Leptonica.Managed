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

namespace CoreJ2K.Icc
{

    /// <summary> This exception is thrown when an image contains no icc profile.
    /// is incorrect.
    /// 
    /// </summary>
    /// <seealso cref="j2k.icc.ICCProfile" />
    /// <version> 	1.0
    /// </version>
    /// <author> 	Bruce A. Kern
    /// </author>

    internal class ICCProfileNotFoundException : ICCProfileException
    {

        /// <summary> Contruct with message</summary>
        /// <param name="msg">returned by getMessage()
        /// </param>
        internal ICCProfileNotFoundException(string msg) : base(msg)
        {
        }


        /// <summary> Empty constructor</summary>
        internal ICCProfileNotFoundException() : base("no icc profile in image")
        {
        }
    }
}