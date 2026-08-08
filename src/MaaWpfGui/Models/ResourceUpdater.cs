// <copyright file="ResourceUpdater.cs" company="MaaAssistantArknights">
// Part of the MaaWpfGui project, maintained by the MaaAssistantArknights team (Maa Team)
// Copyright (C) 2021-2025 MaaAssistantArknights Contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License v3.0 only as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY
// </copyright>

#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MaaWpfGui.Constants;
using MaaWpfGui.Extensions;
using MaaWpfGui.Helper;
using MaaWpfGui.ViewModels.UI;
using MaaWpfGui.ViewModels.UserControl.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using static MaaWpfGui.ViewModels.Dialogs.VersionUpdateDialogViewModel;

namespace MaaWpfGui.Models;

public static class ResourceUpdater
{
    private static readonly ILogger _logger = Log.ForContext("SourceContext", "ResourceUpdater");

    public static async Task<bool> UpdateFromGithubAsync()
    {
        ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceUpdating"));

        const string GithubZipFileName = "MaaResourceGithub.zip";
        string githubZipFile = Path.Combine(PathsHelper.BaseDir, GithubZipFileName);
        string extractFolder = Path.Combine(PathsHelper.BaseDir, "MaaResourceGithub");

        if (!await DownloadFullPackageAsync(MaaUrls.GithubResourceUpdate, githubZipFile).ConfigureAwait(false))
        {
            Fail();
            return false;
        }

        OutputDownloadProgress(downloading: false, output: LocalizationHelper.GetString("GameResourceUpdatePreparing"));

        if (!ExtractAndMergeResourcePackage(githubZipFile, extractFolder))
        {
            Fail();
            return false;
        }

        SafeDeleteFile(githubZipFile);

        SettingsViewModel.VersionUpdateSettings.NewResourceFoundInfo = string.Empty;
        OutputDownloadProgress(
            downloading: false,
            output: LocalizationHelper.GetString("GameResourceUpdated"),
            toolTip: LocalizationHelper.GetString("ResourceUpdateTip"));
        return true;

        static void Fail()
        {
            string msg = LocalizationHelper.GetString("GameResourceFailed");
            ToastNotification.ShowDirect(msg);
            OutputDownloadProgress(downloading: false, output: msg);
        }
    }

    /// <summary>
    /// 从 GitHub（经加速代理）检查资源更新。
    /// </summary>
    public static async Task<(CheckUpdateRetT Ret, string? ReleaseNote)> CheckFromGithubAsync()
    {
        var currentVersionDateTime = VersionUpdateSettingsUserControlModel
            .GetResourceVersionByClientType(SettingsViewModel.GameSettings.ClientType)
            .DateTime;

        _logger.Information(
            "Check resource update: client={Client}, currentResourceTime={Current:O}, url={Url}",
            SettingsViewModel.GameSettings.ClientType,
            currentVersionDateTime,
            MaaUrls.GithubResourceVersionJson);

        HttpResponseMessage? response = null;
        try
        {
            response = await Instances.HttpService.GetAsync(new(MaaUrls.GithubResourceVersionJson), uriPartial: UriPartial.Path);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to send GET request to {Uri}", MaaUrls.GithubResourceVersionJson);
        }

        if (response is null)
        {
            _logger.Error("Failed to check resource version from GitHub");
            ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceFailed"));
            return (CheckUpdateRetT.NetworkError, null);
        }

        var jsonStr = await response.Content.ReadAsStringAsync();
        _logger.Information("{jsonStr}", jsonStr);
        JObject? data = null;
        try
        {
            data = (JObject?)JsonConvert.DeserializeObject(jsonStr);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to deserialize json.");
        }

        if (data is null)
        {
            ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceFailed"));
            return (CheckUpdateRetT.UnknownError, null);
        }

        var lastUpdated = data["last_updated"]?.ToString();
        if (!DateTimeOffset.TryParseExact(
                lastUpdated,
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var versionTime))
        {
            ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceFailed"));
            return (CheckUpdateRetT.UnknownError, null);
        }

        if (currentVersionDateTime >= versionTime)
        {
            return (CheckUpdateRetT.AlreadyLatest, null);
        }

        var releaseNote = LocalizationHelper.FormatVersion(lastUpdated, versionTime);
        _logger.Information("New resource version found: {DateTime:yyyy-MM-dd+HH:mm:ss.fff}", versionTime);

        SettingsViewModel.VersionUpdateSettings.NewResourceFoundInfo = LocalizationHelper.GetStringFormat("ResourceUpdateAvailableTip", releaseNote);

        return (CheckUpdateRetT.OK, releaseNote);
    }

    /// <summary>
    /// 检查并下载资源更新。
    /// </summary>
    public static async Task<CheckUpdateRetT> CheckAndDownloadResourceUpdate()
    {
        try
        {
            SettingsViewModel.VersionUpdateSettings.IsCheckingForUpdates = true;

            var (ret, _) = await CheckFromGithubAsync();
            if (ret != CheckUpdateRetT.OK)
            {
                return ret;
            }

            if (await UpdateFromGithubAsync())
            {
                return CheckUpdateRetT.OnlyGameResourceUpdated;
            }

            return ret;
        }
        finally
        {
            SettingsViewModel.VersionUpdateSettings.IsCheckingForUpdates = false;
        }
    }

    public static async Task ResourceUpdateAndReloadAsync()
    {
        if (SettingsViewModel.VersionUpdateSettings.IsCheckingForUpdates)
        {
            return;
        }

        var ret = await CheckAndDownloadResourceUpdate();
        if (ret == CheckUpdateRetT.OnlyGameResourceUpdated)
        {
            _ = ResourceReloadWhenIdleAsync();
        }
    }

    public static void ResourceReload()
    {
        Instances.AsstProxy.LoadResource();
        DataHelper.Reload();
        SettingsViewModel.VersionUpdateSettings.ResourceInfoUpdate();
        ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceUpdated"));
    }

    private static bool _isReloading = false;

    public static async Task ResourceReloadWhenIdleAsync()
    {
        if (_isReloading)
        {
            _logger.Information("Resource is already reloading, skip this request.");
            return;
        }

        _isReloading = true;
        try
        {
            await Instances.AsstProxy.LoadResourceWhenIdleAsync();
            DataHelper.Reload();
            SettingsViewModel.VersionUpdateSettings.ResourceInfoUpdate();
            ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceUpdated"));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to reload resource after import");
            ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceFailed"));
        }
        finally
        {
            _isReloading = false;
        }
    }

    /// <summary>
    /// 本地资源包导入结果。
    /// </summary>
    public enum LocalResourcePackageImportStatus
    {
        /// <summary>不是资源更新包。</summary>
        NotResourcePackage,

        /// <summary>资源版本低于或等于本地版本，已拒绝。</summary>
        VersionTooOld,

        /// <summary>导入成功。</summary>
        Imported,

        /// <summary>导入失败。</summary>
        Failed,
    }

    /// <summary>
    /// 检测压缩包是否为资源更新包，并尝试读取其版本时间戳。
    /// 只认 <c>MaaResource-main/resource/version.json</c> 这一层固定前缀结构，
    /// 避免误匹配完整包根目录的 <c>resource/version.json</c> 或 global 子目录中的同名文件。
    /// </summary>
    /// <param name="packagePath">压缩包路径。</param>
    /// <param name="versionDateTime">包内资源版本时间戳（last_updated）；无法读取则为 MinValue，且方法返回 false。</param>
    /// <returns>匹配 <c>MaaResource-main/resource/version.json</c> 且能解析出版本时间戳则返回 true。</returns>
    public static bool IsResourcePackage(string packagePath, out DateTimeOffset versionDateTime)
    {
        versionDateTime = DateTimeOffset.MinValue;
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var versionEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals("MaaResource-main/resource/version.json", StringComparison.OrdinalIgnoreCase));
            if (versionEntry == null)
            {
                return false;
            }

            // 必须能解析出版本时间戳才算有效资源包，避免绕过版本校验
            return TryReadLastUpdated(versionEntry, out versionDateTime);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to inspect zip for resource package detection: {PackagePath}", packagePath);
            return false;
        }
    }

    /// <summary>
    /// 导入本地资源更新包。校验版本（小于等于本地则拒绝）、解压合并到 resource 目录并重载。
    /// </summary>
    /// <param name="packagePath">压缩包路径。</param>
    /// <param name="packageDateTime">由 <see cref="IsResourcePackage"/> 预检测得到的时间戳，避免重复扫描 zip。</param>
    /// <returns>导入结果状态。</returns>
    public static async Task<LocalResourcePackageImportStatus> ImportLocalResourcePackageAsync(
        string packagePath,
        DateTimeOffset packageDateTime)
    {
        if (SettingsViewModel.VersionUpdateSettings.IsCheckingForUpdates)
        {
            ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceFailed"));
            return LocalResourcePackageImportStatus.Failed;
        }

        SettingsViewModel.VersionUpdateSettings.IsCheckingForUpdates = true;
        try
        {
            var localDateTime = SettingsViewModel.VersionUpdateSettings.ResourceDateTime;

            // 版本校验：无效时间戳或包内版本小于等于本地版本则拒绝（不执行解压）
            if (packageDateTime == DateTimeOffset.MinValue || packageDateTime <= localDateTime)
            {
                _logger.Information(
                    "Resource package rejected: package version {PackageVersion} (UTC) / {PackageVersionLocal} (local) is not newer than local {LocalVersion} (UTC) / {LocalVersionLocal} (local)",
                    packageDateTime,
                    packageDateTime.ToLocalTime(),
                    localDateTime,
                    localDateTime.ToLocalTime());
                ToastNotification.ShowDirect(LocalizationHelper.GetStringFormat(
                    "LocalResourcePackageTooOld",
                    packageDateTime.ToLocalTimeString(),
                    localDateTime.ToLocalTimeString()));
                return LocalResourcePackageImportStatus.VersionTooOld;
            }

            ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceUpdating"));

            // 解压 + 合并到本地 resource 目录（后台线程）
            string extractFolder = Path.Combine(PathsHelper.BaseDir, "MaaResourceImport");
            bool extractSuccess = await Task.Run(() =>
                ExtractAndMergeResourcePackage(packagePath, extractFolder)).ConfigureAwait(false);

            if (!extractSuccess)
            {
                ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceFailed"));
                return LocalResourcePackageImportStatus.Failed;
            }

            SettingsViewModel.VersionUpdateSettings.NewResourceFoundInfo = string.Empty;
            return LocalResourcePackageImportStatus.Imported;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to import local resource package: {PackagePath}", packagePath);
            ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceFailed"));
            return LocalResourcePackageImportStatus.Failed;
        }
        finally
        {
            SettingsViewModel.VersionUpdateSettings.IsCheckingForUpdates = false;
        }
    }

    /// <summary>
    /// 导入本地资源更新包并重载资源。重载采用 fire-and-forget，避免长时间占用 <see cref="VersionUpdateSettings.IsCheckingForUpdates"/>。
    /// </summary>
    /// <param name="packagePath">压缩包路径。</param>
    /// <param name="packageDateTime">由 <see cref="IsResourcePackage"/> 预检测得到的时间戳。</param>
    /// <returns>导入结果状态（重载是否完成不包含在内）。</returns>
    public static async Task<LocalResourcePackageImportStatus> ImportLocalResourcePackageAndReloadAsync(
        string packagePath,
        DateTimeOffset packageDateTime)
    {
        var status = await ImportLocalResourcePackageAsync(packagePath, packageDateTime).ConfigureAwait(false);
        if (status == LocalResourcePackageImportStatus.Imported)
        {
            // 先释放 IsCheckingForUpdates，再异步重载（可能等待任务队列空闲，耗时较长）
            _ = ResourceReloadWhenIdleAsync();
        }

        return status;
    }

    /// <summary>
    /// 解压资源包并合并到本地 resource 目录（GitHub 更新 / 拖入导入共用）。
    /// 包内须含 <c>MaaResource-main/resource/</c> 目录。
    /// </summary>
    /// <param name="zipPath">压缩包路径。</param>
    /// <param name="extractFolder">临时解压目录（应为绝对路径）。</param>
    /// <returns>成功返回 true。</returns>
    private static bool ExtractAndMergeResourcePackage(string zipPath, string extractFolder)
    {
        // 解压
        try
        {
            if (Directory.Exists(extractFolder))
            {
                Directory.Delete(extractFolder, true);
            }

            ZipFile.ExtractToDirectory(zipPath, extractFolder);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to extract resource package {ZipPath}", zipPath);
            SafeDeleteDirectory(extractFolder);
            return false;
        }

        // 查找 resource 目录（固定 MaaResource-main/resource/）
        string? resourceDir = FindResourceDirectory(extractFolder);
        if (resourceDir == null)
        {
            _logger.Warning("No resource/ directory found in package: {ZipPath}", zipPath);
            SafeDeleteDirectory(extractFolder);
            return false;
        }

        // 合并到本地 resource 目录
        try
        {
            DirectoryMerge(resourceDir, PathsHelper.ResourceDir);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to merge resource package from {ZipPath}", zipPath);
            SafeDeleteDirectory(extractFolder);
            return false;
        }

        SafeDeleteDirectory(extractFolder);
        return true;
    }

    /// <summary>
    /// 在解压目录中查找 resource 文件夹（<c>MaaResource-main/resource/</c>）。
    /// </summary>
    /// <param name="extractFolder">解压目录路径。</param>
    /// <returns>resource 目录完整路径，未找到则返回 null。</returns>
    private static string? FindResourceDirectory(string extractFolder)
    {
        // 固定结构：MaaResource-main/resource/
        string path = Path.Combine(extractFolder, "MaaResource-main", "resource");
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>
    /// 从 zip entry 中读取 version.json 的 last_updated 时间戳。
    /// </summary>
    /// <param name="entry">version.json 的 zip entry。</param>
    /// <param name="dateTime">解析出的时间戳。</param>
    /// <returns>解析成功返回 true。</returns>
    private static bool TryReadLastUpdated(ZipArchiveEntry entry, out DateTimeOffset dateTime)
    {
        dateTime = DateTimeOffset.MinValue;
        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var json = JsonConvert.DeserializeObject<JObject>(reader.ReadToEnd());
            var lastUpdated = json?["last_updated"]?.ToString();
            if (string.IsNullOrEmpty(lastUpdated))
            {
                return false;
            }

            dateTime = DateTimeOffset.ParseExact(
                lastUpdated,
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to parse resource version.json from zip entry: {Entry}", entry.FullName);
            return false;
        }
    }

    private static void SafeDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception e)
        {
            _logger.Error("Failed to delete {FilePath}: {Message}", filePath, e.Message);
        }
    }

    private static void SafeDeleteDirectory(string dirPath)
    {
        try
        {
            if (Directory.Exists(dirPath))
            {
                Directory.Delete(dirPath, true);
            }
        }
        catch (Exception e)
        {
            _logger.Error("Failed to cleanup directory {DirPath}: {Message}", dirPath, e.Message);
        }
    }

    private static async Task<bool> DownloadFullPackageAsync(string url, string saveTo)
    {
        try
        {
            return await Instances.HttpService.DownloadFileAsync(new(url), saveTo, "application/zip");
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to send GET request to {Uri}", url);
            OutputDownloadProgress(downloading: false, output: LocalizationHelper.GetString("GameResourceFailed"));
            return false;
        }
    }

    private static void DirectoryMerge(string sourceDirName, string destDirName)
    {
        DirectoryInfo dir = new DirectoryInfo(sourceDirName);
        DirectoryInfo[] dirs = dir.GetDirectories();

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourceDirName);
        }

        if (!Directory.Exists(destDirName))
        {
            Directory.CreateDirectory(destDirName);
        }

        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            if (file.Name == ".gitignore")
            {
                continue;
            }

            string tempPath = Path.Combine(destDirName, file.Name);
            file.CopyTo(tempPath, true); // 覆盖现有文件
        }

        foreach (DirectoryInfo subDir in dirs)
        {
            string tempPath = Path.Combine(destDirName, subDir.Name);
            DirectoryMerge(subDir.FullName, tempPath);
        }
    }
}
