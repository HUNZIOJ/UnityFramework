using System;
using System.Collections.Generic;
using System.Reflection;
using Frame.Assets;
using Frame.Core;
using HybridCLR;
using UnityEngine;

namespace Frame.HotUpdate
{
    public sealed class HybridCLRHotUpdateService : GameModuleBase, IHotUpdateService
    {
        private readonly List<string> loadedAotMetadataAssemblies = new List<string>();
        private readonly List<Assembly> loadedAssemblies = new List<Assembly>();
        private readonly Dictionary<string, Assembly> loadedAssemblyByName = new Dictionary<string, Assembly>(StringComparer.Ordinal);

        public IReadOnlyList<string> LoadedAotMetadataAssemblies
        {
            get { return loadedAotMetadataAssemblies; }
        }

        public IReadOnlyList<Assembly> LoadedAssemblies
        {
            get { return loadedAssemblies; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<IHotUpdateService>(this);
            Context.Services.Register(this);
        }

        protected override void OnShutdown()
        {
            loadedAotMetadataAssemblies.Clear();
            loadedAssemblies.Clear();
            loadedAssemblyByName.Clear();
        }

        public HotUpdateLoadResult LoadAotMetadata(string name, byte[] dllBytes, HotUpdateAotMetadataMode mode = HotUpdateAotMetadataMode.SuperSet)
        {
            name = NormalizeName(name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return HotUpdateLoadResult.Failed(name, "AOT metadata name is required.");
            }

            if (dllBytes == null || dllBytes.Length == 0)
            {
                return HotUpdateLoadResult.Failed(name, "AOT metadata bytes are empty.");
            }

            if (loadedAotMetadataAssemblies.Contains(name))
            {
                return HotUpdateLoadResult.Succeeded(name);
            }

            LoadImageErrorCode errorCode;
            try
            {
                errorCode = RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, ConvertMode(mode));
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                return HotUpdateLoadResult.Failed(name, exception.Message);
            }

            if (errorCode != LoadImageErrorCode.OK)
            {
                return HotUpdateLoadResult.Failed(name, "Failed to load AOT metadata: " + name, errorCode.ToString());
            }

            loadedAotMetadataAssemblies.Add(name);
            FrameLog.Info("Loaded HybridCLR AOT metadata: " + name);
            return HotUpdateLoadResult.Succeeded(name);
        }

        public HotUpdateLoadResult LoadAotMetadataAsset(string path, HotUpdateAotMetadataMode mode = HotUpdateAotMetadataMode.SuperSet)
        {
            if (!TryLoadTextAsset(path, out TextAsset asset, out string error))
            {
                return HotUpdateLoadResult.Failed(path, error);
            }

            try
            {
                return LoadAotMetadata(GetNameFromPath(path), asset.bytes, mode);
            }
            finally
            {
                ReleaseAsset(path);
            }
        }

        public HotUpdateLoadResult LoadAssembly(string name, byte[] dllBytes)
        {
            name = NormalizeName(name);
            if (dllBytes == null || dllBytes.Length == 0)
            {
                return HotUpdateLoadResult.Failed(name, "Assembly bytes are empty.");
            }

            if (!string.IsNullOrWhiteSpace(name) && loadedAssemblyByName.TryGetValue(name, out Assembly existing))
            {
                return HotUpdateLoadResult.Succeeded(name, existing);
            }

            Assembly assembly;
            try
            {
                assembly = Assembly.Load(dllBytes);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                return HotUpdateLoadResult.Failed(name, exception.Message);
            }

            string assemblyName = assembly.GetName().Name;
            loadedAssemblies.Add(assembly);
            loadedAssemblyByName[assemblyName] = assembly;
            if (!string.IsNullOrWhiteSpace(name))
            {
                loadedAssemblyByName[name] = assembly;
            }

            FrameLog.Info("Loaded hot update assembly: " + assemblyName);
            return HotUpdateLoadResult.Succeeded(assemblyName, assembly);
        }

        public HotUpdateLoadResult LoadAssemblyAsset(string path)
        {
            if (!TryLoadTextAsset(path, out TextAsset asset, out string error))
            {
                return HotUpdateLoadResult.Failed(path, error);
            }

            try
            {
                return LoadAssembly(GetNameFromPath(path), asset.bytes);
            }
            finally
            {
                ReleaseAsset(path);
            }
        }

        public bool TryGetAssembly(string assemblyName, out Assembly assembly)
        {
            assemblyName = NormalizeName(assemblyName);
            if (!string.IsNullOrWhiteSpace(assemblyName) && loadedAssemblyByName.TryGetValue(assemblyName, out assembly))
            {
                return true;
            }

            assembly = null;
            return false;
        }

        public object InvokeStatic(string assemblyName, string typeName, string methodName, object[] args = null)
        {
            if (!TryGetAssembly(assemblyName, out Assembly assembly))
            {
                throw new FrameException("Hot update assembly is not loaded: " + assemblyName);
            }

            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new ArgumentException("Type name is required.", "typeName");
            }

            if (string.IsNullOrWhiteSpace(methodName))
            {
                throw new ArgumentException("Method name is required.", "methodName");
            }

            Type type = assembly.GetType(typeName, true);
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                throw new FrameException("Static method not found: " + typeName + "." + methodName);
            }

            return method.Invoke(null, args);
        }

        private bool TryLoadTextAsset(string path, out TextAsset asset, out string error)
        {
            asset = null;
            error = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Asset path is required.";
                return false;
            }

            IAssetService assets;
            if (Context == null || !Context.Services.TryResolve(out assets))
            {
                error = "IAssetService is not registered.";
                return false;
            }

            AssetHandle<TextAsset> handle = assets.Load<TextAsset>(path);
            if (!handle.IsValid)
            {
                error = "TextAsset not found: " + path;
                return false;
            }

            asset = handle.Asset;
            return true;
        }

        private void ReleaseAsset(string path)
        {
            IAssetService assets;
            if (Context != null && Context.Services.TryResolve(out assets))
            {
                assets.Release(path);
            }
        }

        private static HomologousImageMode ConvertMode(HotUpdateAotMetadataMode mode)
        {
            return mode == HotUpdateAotMetadataMode.Consistent ? HomologousImageMode.Consistent : HomologousImageMode.SuperSet;
        }

        private static string NormalizeName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        }

        private static string GetNameFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = path.Replace('\\', '/').Trim();
            int slash = normalized.LastIndexOf('/');
            return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        }
    }
}
