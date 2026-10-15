using System;
using UnityEngine;

namespace Boot
{
    /// <summary>系统安装交接结果；成功仅代表已打开安装或授权界面，不代表 APK 已安装。</summary>
    [Serializable]
    public sealed class ApkInstallResult
    {
        public int schemaVersion;
        /// <summary>2 等待交接，0 安装页已打开，1 授权页已打开，-1 失败。</summary>
        public int result = -1;
        public string errorCode;
        public string stage;
        public string details;

        public static ApkInstallResult Failure(string code, string stage, string details) =>
            new ApkInstallResult { schemaVersion = 1, result = -1, errorCode = code, stage = stage, details = details };

        /// <summary>非法桥接数据按失败处理，不能把缺省字段误认成安装页已打开。</summary>
        public static ApkInstallResult Parse(string json)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(json) && json.Length <= 65536)
                {
                    var value = JsonUtility.FromJson<ApkInstallResult>(json);
                    if (value != null && value.schemaVersion == 1 && !string.IsNullOrWhiteSpace(value.stage) &&
                        value.result >= -1 && value.result <= 2) return value;
                }
            }
            catch (ArgumentException) { }
            return Failure("INVALID_BRIDGE_RESULT", "bridge", "安装插件未返回有效结果。");
        }

        public string PlayerMessage => errorCode switch
        {
            "SIGNATURE_MISMATCH" => "安装包签名与当前游戏不一致，请联系开发者重新发布。",
            "PACKAGE_MISMATCH" => "安装包不属于当前游戏，请联系开发者确认包名。",
            "VERSION_MISMATCH" => "安装包版本与发布信息不一致，请重新下载或联系开发者。",
            "APK_UNREADABLE" => "系统无法解析安装包，请重新下载或联系开发者。",
            "INVALID_FILE" => "安装包文件不存在或路径无效，请重新下载。",
            "INSTALL_TIMEOUT" => "打开系统安装界面超时，你可以重试或稍后更新。",
            "ACTIVITY_NOT_FOUND" => "系统没有可用的安装或授权界面，请检查设备设置。",
            "SECURITY_EXCEPTION" => "系统拒绝打开安装界面，请检查安装权限。",
            _ => "无法打开安装更新，请复制错误详情交给开发者排查，也可以稍后更新。"
        };

        /// <summary>复制包身份、版本、文件校验信息与原生错误；最长 16 KiB，便于手机粘贴排障。</summary>
        public string ToCopyDetails(ApkUpdateManifest manifest)
        {
            string text = $"安装更新失败\n阶段：{stage}\n错误码：{errorCode}\n结果：{result}\n" +
                $"包名：{Application.identifier}\n当前版本：{Application.version}\n目标版本：{manifest.versionName}\n" +
                $"目标版本码：{manifest.versionCode}\n文件：{manifest.apkFileName}\n大小：{manifest.sizeBytes} bytes\nSHA-256：{manifest.sha256}\n{details}";
            return text.Length <= 16384 ? text : text.Substring(0, 16300) + "\n[详情过长，已截断]";
        }
    }
}
