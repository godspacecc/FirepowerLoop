using System;
using System.Collections.Generic;
using System.IO;
using Luban;
using UnityEngine;
using YooAsset;

namespace Game
{
    /// <summary>
    /// 从 YooAsset DefaultPackage 同步加载 Luban 配置表并缓存字节副本，释放资源 Handle 后仍可读取配置。
    /// </summary>
    public static class DataLoader
    {
        private static Tables _tables;
        private static readonly Dictionary<string, ByteBuf> _cache = new Dictionary<string, ByteBuf>();

        public static bool Init()
        {
            try
            {
                LoadTables();
                return true;
            }
            catch (Exception exception)
            {
                Log.Error($"[DataLoader] 配置初始化失败：{exception}");
                return false;
            }
        }

        /// <summary>
        /// 获取配置表
        /// </summary>
        public static Tables Tables
        {
            get
            {
                if (_tables == null)
                {
                    LoadTables();
                }
                return _tables;
            }
        }

        /// <summary>
        /// 加载配置表
        /// </summary>
        public static void LoadTables()
        {
            _tables = new Tables(LoadByteBuf);
        }

        /// <summary>
        /// 加载字节缓冲区 - 始终从 DefaultPackage 同步加载
        /// </summary>
        private static ByteBuf LoadByteBuf(string fileName)
        {
            // 检查缓存
            if (_cache.TryGetValue(fileName, out var cachedBuf))
            {
                return cachedBuf;
            }

            // 获取 DefaultPackage
            var package = YooAssets.GetPackage("DefaultPackage");
            if (package == null)
            {
                throw new InvalidOperationException(
                    "[DataLoader] DefaultPackage 未初始化，请确保 YooAsset 已正确初始化");
            }

            // 同步加载资源
            string address = $"{fileName}.bytes";
            var handle = package.LoadAssetSync<TextAsset>(address);

            if (handle.Status != EOperationStatus.Succeed)
            {
                handle.Release();
                throw new FileNotFoundException(
                    $"[DataLoader] 配置文件不存在: {address}");
            }

            try
            {
                var textAsset = handle.AssetObject as TextAsset;
                if (textAsset == null)
                {
                    throw new InvalidOperationException(
                        $"[DataLoader] 资源类型错误，期望 TextAsset: {address}");
                }

                // 克隆字节（Handle 释放后仍可使用）
                byte[] bytes = (byte[])textAsset.bytes.Clone();
                var buf = new ByteBuf(bytes);

                // 缓存
                _cache[fileName] = buf;

                return buf;
            }
            finally
            {
                handle.Release();
            }
        }

        /// <summary>
        /// 释放配置表
        /// </summary>
        public static void UnloadTables()
        {
            _tables = null;
            _cache.Clear();
        }
    }
}
