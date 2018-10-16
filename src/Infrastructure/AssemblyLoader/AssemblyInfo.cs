using System;
using System.Reflection;

namespace Infrastructure.AssemblyLoader
{
    public abstract class AssemblyInfo : IAssemblyInfo
    {
        #region Private member variables
        private readonly string _name;
        private readonly Version _version;
        private readonly Assembly _assembly;
        #endregion

        #region  Public properties
        public string Name => this._name;
        public Version Version => this._version;
        public Assembly Assembly => this._assembly;
        #endregion

        #region Ctor
        public AssemblyInfo(string name, Version version, Assembly assembly = null)
        {
            this._name = name;
            this._version = version;
            this._assembly = assembly;
        }
        #endregion
    }
}