#nullable enable annotations
#nullable disable warnings
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, CS1591, CS8981, SYSLIB0011, SYSLIB0014
namespace CoreJ2K.Util
{
    internal interface IFileInfo
    {
        #region PROPERTIES

        string Name { get; }

        string FullName { get; }

        bool Exists { get; }

        #endregion

        #region METHODS

        bool Delete();

        #endregion
    }
}