using System.Collections.Generic;
using System.Reflection;

namespace Frame.HotUpdate
{
    public interface IHotUpdateService
    {
        IReadOnlyList<string> LoadedAotMetadataAssemblies { get; }

        IReadOnlyList<Assembly> LoadedAssemblies { get; }

        HotUpdateLoadResult LoadAotMetadata(string name, byte[] dllBytes, HotUpdateAotMetadataMode mode = HotUpdateAotMetadataMode.SuperSet);

        HotUpdateLoadResult LoadAotMetadataAsset(string path, HotUpdateAotMetadataMode mode = HotUpdateAotMetadataMode.SuperSet);

        HotUpdateLoadResult LoadAssembly(string name, byte[] dllBytes);

        HotUpdateLoadResult LoadAssemblyAsset(string path);

        bool TryGetAssembly(string assemblyName, out Assembly assembly);

        object InvokeStatic(string assemblyName, string typeName, string methodName, object[] args = null);
    }
}
