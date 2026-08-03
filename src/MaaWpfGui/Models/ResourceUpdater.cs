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
using System.Net.Http;
using System.Threading.Tasks;
using MaaWpfGui.Constants;
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

        if (!await DownloadFullPackageAsync(MaaUrls.GithubResourceUpdate, "MaaResourceGithub.zip").ConfigureAwait(false))
        {
            Fail();
            return false;
        }

        OutputDownloadProgress(downloading: false, output: LocalizationHelper.GetString("GameResourceUpdatePreparing"));

        const string GithubZipFile = "MaaResourceGithub.zip";
        const string ExtractFolder = "MaaResourceGithub";

        // 解压到 MaaResource 文件夹
        try
        {
            if (Directory.Exists(ExtractFolder))
            {
                Directory.Delete(ExtractFolder, true);
            }

            ZipFile.ExtractToDirectory(GithubZipFile, ExtractFolder);
        }
        catch (Exception e)
        {
            _logger.Error("Failed to extract MaaResourceGithub.zip: " + e.Message);
            Fail();
            return false;
        }

        // 把 \MaaResource-main 中的 resource 文件夹复制到当前目录
        try
        {
            string basePath = Path.Combine(ExtractFolder, "MaaResource-main");
            foreach (var folder in new[] { "resource" })
            {
                DirectoryMerge(
                    Path.Combine(basePath, folder),
                    Path.Combine(PathsHelper.BaseDir, folder));
            }
        }
        catch (Exception e)
        {
            _logger.Error("Failed to copy folders: " + e.Message);
            Fail();
            return false;
        }

        // 删除 MaaResource 文件夹 和 MaaResource.zip
        try
        {
            Directory.Delete(ExtractFolder, true);
            File.Delete(GithubZipFile);
        }
        catch (Exception e)
        {
            _logger.Error("Failed to delete MaaResource files: " + e.Message);
        }

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
        await Instances.AsstProxy.LoadResourceWhenIdleAsync();
        DataHelper.Reload();
        SettingsViewModel.VersionUpdateSettings.ResourceInfoUpdate();
        ToastNotification.ShowDirect(LocalizationHelper.GetString("GameResourceUpdated"));
        _isReloading = false;
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
