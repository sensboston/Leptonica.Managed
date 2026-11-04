#nullable enable annotations
#nullable disable warnings
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, CS1591, CS8981, SYSLIB0011, SYSLIB0014
// Copyright (c) 2007-2016 CSJ2K contributors.
// Licensed under the BSD 3-Clause License.

namespace CoreJ2K.Util
{
    internal class DotnetFileInfoCreator : IFileInfoCreator
    {
        #region FIELDS

        private static readonly IFileInfoCreator Instance = new DotnetFileInfoCreator();

        #endregion

        #region METHODS

        public static void Register()
        {
            FileInfoFactory.Register(Instance);
        }

        public IFileInfo Create(string fileName)
        {
            return new DotnetFileInfo(fileName);
        }

        #endregion
    }
}
