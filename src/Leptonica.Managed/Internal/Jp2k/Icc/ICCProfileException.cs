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
namespace CoreJ2K.Icc
{

    /// <summary> This exception is thrown when the content of a profile
    /// is incorrect.
    /// 
    /// </summary>
    /// <seealso cref="j2k.icc.ICCProfile" />
    /// <version> 	1.0
    /// </version>
    /// <author> 	Bruce A. Kern
    /// </author>
    internal class ICCProfileException : Exception
    {

        /// <summary>  Contruct with message</summary>
        /// <param name="msg">returned by getMessage()
        /// </param>
        public ICCProfileException(string msg) : base(msg)
        {
        }


        /// <summary> Empty constructor</summary>
        public ICCProfileException()
        {
        }
    }
}