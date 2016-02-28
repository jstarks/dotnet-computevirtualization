﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;

namespace Microsoft.Windows.ComputeVirtualization
{
    public class ContainerStorage
    {
        private class StorageFunctions
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct DriverInfo
            {
                public int Type;
                public IntPtr Path;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct LayerDescriptor
            {
                public Guid Id;
                public int Flags;
                public IntPtr Path;
            }

            [DllImport("vmcompute.dll", PreserveSig = false, ExactSpelling = true)]
            public static extern void CreateLayer(ref DriverInfo info, string id, string parentId);

            [DllImport("vmcompute.dll", PreserveSig = false, ExactSpelling = true)]
            public static extern void DestroyLayer(ref DriverInfo info, string id);

            [DllImport("vmcompute.dll", PreserveSig = false, ExactSpelling = true)]
            public static extern void ActivateLayer(ref DriverInfo info, string id);

            [DllImport("vmcompute.dll", PreserveSig = false, ExactSpelling = true)]
            public static extern void DeactivateLayer(ref DriverInfo info, string id);

            [DllImport("vmcompute.dll", PreserveSig = false, ExactSpelling = true)]
            public static extern void CreateSandboxLayer(ref DriverInfo info, string id, string parentId, IntPtr unused1, int unused2);

            [DllImport("vmcompute.dll", PreserveSig = false, ExactSpelling = true)]
            public static extern void PrepareLayer(ref DriverInfo info, string id, [MarshalAs(UnmanagedType.LPArray)] LayerDescriptor[] layers, int layerCount);

            [DllImport("vmcompute.dll", PreserveSig = false, ExactSpelling = true)]
            public static extern void UnprepareLayer(ref DriverInfo info, string id);

            [DllImport("vmcompute.dll", PreserveSig = false, ExactSpelling = true)]
            public static extern void ExportLayer(ref DriverInfo info, string id, string exportPath, [MarshalAs(UnmanagedType.LPArray)] LayerDescriptor[] layers, int layerCount);

            [DllImport("vmcompute.dll", PreserveSig = false, ExactSpelling = true)]
            public static extern void ImportLayer(ref DriverInfo info, string id, string importPath, [MarshalAs(UnmanagedType.LPArray)] LayerDescriptor[] layers, int layerCount);

            [DllImport("vmcompute.dll", PreserveSig = false, ExactSpelling = true)]
            public static extern void GetLayerMountPath(ref DriverInfo info, string id, ref UIntPtr length, StringBuilder path);
        }

        private class DriverInfoHelper : IDisposable
        {
            public StorageFunctions.DriverInfo Data;

            public DriverInfoHelper()
            {                
                Data.Type = 1;
                Data.Path = Marshal.StringToCoTaskMemUni("");
                Marshal.WriteInt16(Data.Path, 0);
            }

            public void Dispose()
            {
                Marshal.FreeCoTaskMem(Data.Path);
            }
        }

        private class LayerHelper : IDisposable
        {
            public StorageFunctions.LayerDescriptor[] Data;

            public LayerHelper(IList<Layer> layers)
            {
                Data = new StorageFunctions.LayerDescriptor[layers.Count];
                for (int i = 0; i < layers.Count; i++)
                {
                    Data[i].Id = layers[i].Id;
                    Data[i].Path = Marshal.StringToCoTaskMemUni(layers[i].Path);
                }
            }

            public void Dispose()
            {
                foreach (var layer in Data)
                {
                    Marshal.FreeCoTaskMem(layer.Path);
                }
            }
        }

        /// <summary>
        /// Creates a new storage sandbox at the given directory path.
        /// </summary>
        /// <param name="path">A path to the new sandbox.</param>
        public static void CreateSandbox(string path)
        {
            using (var info = new DriverInfoHelper())
            {
                StorageFunctions.CreateSandboxLayer(ref info.Data, Path.GetFullPath(path), null, new IntPtr(0), 0);
            }
        }

        /// <summary>
        /// Represents a mounted sandbox. When Dispose() is called, the layer will be unmounted.
        /// </summary>
        public class MountedSandbox : IDisposable
        {
            private string _path;
            private string _mountPath;

            /// <summary>
            /// Returns a path that can be used to access the sandbox's files. This path may not work with all versions of .NET because
            /// it is not an ordinary Win32 path.
            /// </summary>
            public string MountPath { get { return _mountPath; } }

            internal MountedSandbox(string path, IList<Layer> layers)
            {
                using (var info = new DriverInfoHelper())
                using (var descriptors = new LayerHelper(layers))
                {
                    StorageFunctions.ActivateLayer(ref info.Data, path);
                    try
                    {
                        StorageFunctions.PrepareLayer(ref info.Data, path, descriptors.Data, descriptors.Data.Length);
                        var mountPath = new StringBuilder(260);
                        var length = new UIntPtr((uint)mountPath.Capacity);
                        StorageFunctions.GetLayerMountPath(ref info.Data, path, ref length, mountPath);
                        _mountPath = mountPath.ToString();
                    }
                    catch (Exception)
                    {
                        StorageFunctions.DeactivateLayer(ref info.Data, path);
                        throw;
                    }
                }

                _path = path;
            }

            public void Dispose()
            {
                using (var info = new DriverInfoHelper())
                {
                    StorageFunctions.DeactivateLayer(ref info.Data, _path);
                }
            }
        }

        /// <summary>
        /// Mounts a sandbox.
        /// </summary>
        /// <param name="path">The path to the sandbox.</param>
        /// <param name="layers">A list of parent layers.</param>
        /// <returns></returns>
        public static MountedSandbox MountSandbox(string path, IList<Layer> layers)
        {
            return new MountedSandbox(Path.GetFullPath(path), layers);
        }

        /// <summary>
        /// Destroys a layer or sandbox.
        /// </summary>
        /// <param name="path">The path to the layer or sandbox.</param>
        public static void DestroyLayer(string path)
        {
            using (var info = new DriverInfoHelper())
            {
                StorageFunctions.DestroyLayer(ref info.Data, Path.GetFullPath(path));
            }
        }

        /// <summary>
        /// Exports the contents a layer or sandbox to the specified directory. Only the files and
        /// registry keys that were changed are exported.
        /// </summary>
        /// <param name="path">The path to the layer or sandbox.</param>
        /// <param name="exportPath">The path of the exported files. This directory will be created and must not already exist.</param>
        /// <param name="layers">A list of parent layers.</param>
        public static void ExportLayer(string path, string exportPath, IList<Layer> layers)
        {
            path = Path.GetFullPath(path);
            if (Directory.Exists(exportPath))
            {
                throw new IOException(string.Format("Directory {0} already exists.", exportPath));
            }

            Directory.CreateDirectory(exportPath);
            try
            {
                using (var info = new DriverInfoHelper())
                using (var descriptors = new LayerHelper(layers))
                {
                    StorageFunctions.ActivateLayer(ref info.Data, path);
                    try
                    {
                        StorageFunctions.ExportLayer(ref info.Data, path, exportPath, descriptors.Data, descriptors.Data.Length);
                    }
                    finally
                    {
                        StorageFunctions.DeactivateLayer(ref info.Data, path);
                    }
                }
            }
            catch (Exception)
            {
                Directory.Delete(exportPath, true);
                throw;
            }
        }

        /// <summary>
        /// Creates a new layer from the exported contents of another layer.
        /// </summary>
        /// <param name="path">The path of the new layer.</param>
        /// <param name="importPath">The path of the exported files.</param>
        /// <param name="layers">A list of parent layers.</param>
        public static void ImportLayer(string path, string importPath, IList<Layer> layers)
        {
            using (var info = new DriverInfoHelper())
            using (var descriptors = new LayerHelper(layers))
            {
                StorageFunctions.ImportLayer(ref info.Data, Path.GetFullPath(path), importPath, descriptors.Data, descriptors.Data.Length);
            }
        }
    }
}
