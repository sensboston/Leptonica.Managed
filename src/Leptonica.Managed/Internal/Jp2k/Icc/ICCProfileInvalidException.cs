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

    /// <summary> This exception is thrown when the content of an an icc profile 
    /// is in someway incorrect.
    /// 
    /// </summary>
    /// <seealso cref="j2k.icc.ICCProfile" />
    /// <version> 	1.0
    /// </version>
    /// <author> 	Bruce A. Kern
    /// </author>

    internal class ICCProfileInvalidException : ICCProfileException
    {

        /// <summary> Contruct with message</summary>
        /// <param name="msg">returned by getMessage()
        /// </param>
        internal ICCProfileInvalidException(string msg) : base(msg)
        {
        }


        /// <summary> Empty constructor</summary>
        internal ICCProfileInvalidException() : base("icc profile is invalid")
        {
        }
    }
}