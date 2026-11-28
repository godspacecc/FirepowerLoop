using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SharedBoot
{
    /// <summary>供启动界面使用的下载量、速度和预计剩余时间。</summary>
    internal readonly struct BootDownloadProgress
    {
        internal BootDownloadProgress(ulong bytes, ulong total, ulong speed)
        {
            Bytes = bytes;
            Total = total;
            Speed = speed;
        }

        internal ulong Bytes { get; }
        internal ulong Total { get; }
        internal ulong Speed { get; }
        internal float Fraction => Total == 0 ? 0f : Normalize((float)((double)Bytes / Total));
        internal double? RemainingSeconds => Speed == 0 || Bytes >= Total ? (double?)null : (Total - Bytes) / (double)Speed;

        internal static float Normalize(float value) =>
            float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
    }

    /// <summary>将安装包下载到请求独占的临时文件，校验通过后才提交到缓存。</summary>
    internal static class BootInstallerTransfer
    {
        internal static string TemporaryPath(string path) => path + ".part-" + Guid.NewGuid().ToString("N");

        internal static async UniTask DownloadAsync(string url, string temporary, long size,
            Action<BootDownloadProgress> report, CancellationToken cancellation, TimeSpan? totalTimeout = null)
        {
            cancellation.ThrowIfCancellationRequested();
            if (size <= 0)
                throw new InvalidDataException("安装包清单的下载大小无效。");

            Directory.CreateDirectory(Path.GetDirectoryName(temporary));
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
            request.redirectLimit = 0;
            request.downloadHandler = new DownloadHandlerFile(temporary) { removeFileOnAbort = true };
            var operation = request.SendWebRequest();
            double started = Time.realtimeSinceStartupAsDouble;
            double deadline = started + (totalTimeout?.TotalSeconds ?? double.PositiveInfinity);
            double sampleTime = started;
            double lastProgress = started;
            ulong observed = 0;
            ulong sampled = 0;
            ulong speed = 0;
            ulong total = (ulong)size;
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                double now = Time.realtimeSinceStartupAsDouble;
                ulong bytes = request.downloadedBytes;
                if (bytes > total)
                    throw new InvalidDataException("下载大小超出安装包清单声明。");
                if (bytes != observed)
                {
                    observed = bytes;
                    lastProgress = now;
                }

                if (now - sampleTime >= 0.5d)
                {
                    speed = bytes >= sampled ? (ulong)((bytes - sampled) / (now - sampleTime)) : 0;
                    sampled = bytes;
                    sampleTime = now;
                }

                report(new BootDownloadProgress(bytes, total, speed));
                if (operation.isDone)
                    break;

                if (now - lastProgress >= 30d || now >= deadline)
                    throw new TimeoutException("等待安装包下载超时。");
                await UniTask.Yield(PlayerLoopTiming.Update, cancellation);
            }

            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"安装包下载失败：{request.responseCode} {request.error}");
        }

        internal static bool Publish(string temporary, string path, long size, string digest,
            CancellationToken cancellation)
        {
            if (!BootInstallerIntegrity.Matches(temporary, size, digest))
                return false;
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path))
                {
                    if (!BootInstallerIntegrity.Matches(path, size, digest))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        File.Replace(temporary, path, null);
                    }
                }
                else
                {
                    File.Move(temporary, path);
                }
                return true;
            }
            catch (IOException) when (BootInstallerIntegrity.Matches(path, size, digest)) { return true; }
        }

        internal static void CleanTemporary(string temporary)
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
