// <copyright file="VersionUpdateDialogViewModel.cs" company="MaaAssistantArknights">
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
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using MaaWpfGui.Configuration.Factory;
using MaaWpfGui.Constants;
using MaaWpfGui.Helper;
using MaaWpfGui.Main;
using MaaWpfGui.Models;
using MaaWpfGui.Services;
using MaaWpfGui.States;
using MaaWpfGui.ViewModels.UI;
using MaaWpfGui.ViewModels.UserControl.Settings;
using Newtonsoft.Json.Linq;
using Semver;
using Serilog;
using Stylet;

namespace MaaWpfGui.ViewModels.Dialogs;

/// <summary>
/// The view model of version update.
/// </summary>
public class VersionUpdateDialogViewModel : Screen
{
    private readonly RunningState _runningState;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionUpdateDialogViewModel"/> class.
    /// </summary>
    public VersionUpdateDialogViewModel()
    {
        _runningState = RunningState.Instance;
    }

    private static readonly ILogger _logger = Log.ForContext<VersionUpdateDialogViewModel>();

    private static string AddContributorLink(string text)
    {
        /*
        //        "@ " -> "@ "
        //       "`@`" -> "`@`"
        //   "@MistEO" -> "[@MistEO](https://github.com/MistEO)"
        // "[@MistEO]" -> "[@MistEO]"
        */
        return Regex.Replace(text, @"([^\[`]|^)@([^\s]+)", "$1[@$2](https://github.com/$2)");
    }

    private readonly string _curVersion = FakeUpdateHelper.IsEnabled
        ? FakeUpdateHelper.CurrentVersion
        : Marshal.PtrToStringAnsi(MaaService.AsstGetVersion()) ?? "0.0.1";

    private string _latestVersion = string.Empty;

    /// <summary>
    /// Gets or sets the update tag.
    /// </summary>
    public string UpdateTag
    {
        get; set {
            SetAndNotify(ref field, value);
            ConfigFactory.Root.Update.Name = value;
        }
    } = FakeUpdateHelper.IsEnabled ? FakeUpdateHelper.TargetVersion : ConfigFactory.Root.Update.Name;

    private static string LoadUpdateBody()
    {
        var body = MarkdownDataHelper.Get("CHANGELOG");
        if (!string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        return string.Empty;
    }

    private string _updateInfo = LoadUpdateBody();

    // private static readonly MarkdownPipeline s_markdownPipeline = new MarkdownPipelineBuilder().UseXamlSupportedExtensions().Build();

    /// <summary>
    /// Gets or sets the update info.
    /// </summary>
    public string UpdateInfo
    {
        get {
            try
            {
                return AddContributorLink(_updateInfo);
            }
            catch
            {
                return _updateInfo;
            }
        }

        set {
            SetAndNotify(ref _updateInfo, value);
            MarkdownDataHelper.Set("CHANGELOG", value);
        }
    }

    /// <summary>
    /// Gets or sets the update URL.
    /// </summary>
    public string UpdateUrl { get; set => SetAndNotify(ref field, value); } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether it is the first boot after updating.
    /// </summary>
    public bool IsFirstBootAfterUpdate
    {
        get; set {
            SetAndNotify(ref field, value);
            ConfigFactory.Root.Update.IsFirstBoot = value;
        }
    } = ConfigFactory.Root.Update.IsFirstBoot;

    /// <summary>
    /// Gets or sets the name of the update package.
    /// </summary>
    public string UpdatePackageName
    {
        get; set {
            SetAndNotify(ref field, value);
            ConfigFactory.Root.Update.UpdatePackage = value;
        }
    } = ConfigFactory.Root.Update.UpdatePackage;

    /// <summary>
    /// Gets the OS architecture.
    /// </summary>
    private static string OsArchitecture => RuntimeInformation.OSArchitecture.ToString().ToLower();

    /// <summary>
    /// Gets a value indicating whether the OS is arm.
    /// </summary>
    public static bool IsArm => OsArchitecture.StartsWith("arm");

    /*
    private const string RequestUrl = "repos/MaaAssistantArknights/MaaRelease/releases";
    private const string StableRequestUrl = "repos/MaaAssistantArknights/MaaAssistantArknights/releases/latest";
    private const string MaaReleaseRequestUrlByTag = "repos/MaaAssistantArknights/MaaRelease/releases/tags/";
    private const string InfoRequestUrl = "repos/MaaAssistantArknights/MaaAssistantArknights/releases/tags/";
    */

    private JObject? _latestJson;
    private JObject? _assetsObject;
    private bool _requiresFullPackageConfirmation;

    public static bool HasPendingUpdatePackage()
    {
        return PendingUpdateApplier.HasPendingUpdatePackage();
    }

    public enum CheckUpdateRetT
    {
        /// <summary>
        /// 操作成功
        /// </summary>
        // ReSharper disable once InconsistentNaming
        OK,

        /// <summary>
        /// 未知错误
        /// </summary>
        UnknownError,

        /// <summary>
        /// 无需更新
        /// </summary>
        NoNeedToUpdate,

        /// <summary>
        /// 调试版本无需更新
        /// </summary>
        NoNeedToUpdateDebugVersion,

        /// <summary>
        /// 已经是最新版
        /// </summary>
        AlreadyLatest,

        /// <summary>
        /// 网络错误
        /// </summary>
        NetworkError,

        /// <summary>
        /// 获取信息失败
        /// </summary>
        FailedToGetInfo,

        /// <summary>
        /// 新版正在构建中
        /// </summary>
        NewVersionIsBeingBuilt,

        /// <summary>
        /// 只更新了游戏资源
        /// </summary>
        OnlyGameResourceUpdated,
    }

    /// <summary>
    /// Gets or sets a value indicating whether to show the update.
    /// </summary>
    public bool DoNotShowUpdate
    {
        get; set {
            SetAndNotify(ref field, value);
            ConfigFactory.Root.Update.DoNotShowUpdate = value;
        }
    } = ConfigFactory.Root.Update.DoNotShowUpdate;

    /// <summary>
    /// 如果是在更新后第一次启动，显示ReleaseNote弹窗，否则检查更新并下载更新包。
    /// </summary>
    /// <returns>Task</returns>
    public async Task ShowUpdateOrDownload()
    {
        if (IsFirstBootAfterUpdate)
        {
            IsFirstBootAfterUpdate = false;
            if (!DoNotShowUpdate)
            {
                Instances.WindowManager.ShowWindow(this);
            }
        }
        else
        {
            if (!SettingsViewModel.VersionUpdateSettings.StartupUpdateCheck)
            {
                return;
            }

            if (!IsDebugVersion())
            {
                await VersionUpdateAndAskToRestartAsync();
                await ResourceUpdater.ResourceUpdateAndReloadAsync();
            }
            else
            {
                // await ResourceUpdater.CheckAndDownloadResourceUpdate();
                // 跑个空任务避免 async warning
                await Task.Run(() => { });
            }
        }
    }

    /// <summary>
    /// 检查更新并下载更新包，如果成功则提示重启。
    /// </summary>
    /// <returns>Task</returns>
    public async Task VersionUpdateAndAskToRestartAsync()
    {
        if (SettingsViewModel.VersionUpdateSettings.IsCheckingForUpdates)
        {
            return;
        }

        var ret = await CheckAndDownloadVersionUpdate();
        if (ret == CheckUpdateRetT.OK)
        {
            _ = AskToRestart();
        }

        var toastMessage = ret switch {
            CheckUpdateRetT.NoNeedToUpdate => string.Empty,
            CheckUpdateRetT.NoNeedToUpdateDebugVersion => string.Empty,
            CheckUpdateRetT.AlreadyLatest => string.Empty,
            CheckUpdateRetT.UnknownError => LocalizationHelper.GetString("NewVersionDetectFailedTitle"),
            CheckUpdateRetT.NetworkError => LocalizationHelper.GetString("CheckNetworking"),
            CheckUpdateRetT.FailedToGetInfo => LocalizationHelper.GetString("GetReleaseNoteFailed"),
            CheckUpdateRetT.OK => string.Empty,
            CheckUpdateRetT.NewVersionIsBeingBuilt => string.Empty,
            CheckUpdateRetT.OnlyGameResourceUpdated => string.Empty,
            _ => string.Empty,
        };

        if (toastMessage != string.Empty)
        {
            ToastNotification.ShowDirect(toastMessage);
        }
    }

    /// <summary>
    /// 检查更新，并下载更新包。
    /// </summary>
    /// <returns>操作成功返回 <see langword="true"/>，反之则返回 <see langword="false"/>。</returns>
    public async Task<CheckUpdateRetT> CheckAndDownloadVersionUpdate()
    {
        try
        {
            SettingsViewModel.VersionUpdateSettings.IsCheckingForUpdates = true;

            if (FakeUpdateHelper.IsEnabled)
            {
                return await HandleFakeUpdate();
            }

            var checkRet = await CheckUpdate();

            if (checkRet != CheckUpdateRetT.OK)
            {
                return checkRet;
            }

            return await HandleUpdateFromGithubRelease();
        }
        finally
        {
            SettingsViewModel.VersionUpdateSettings.IsCheckingForUpdates = false;
        }
    }

    private async Task<CheckUpdateRetT> HandleFakeUpdate()
    {
        const double MinimumDetectedNewVersionDisplaySeconds = 0.5d;

        UpdateTag = FakeUpdateHelper.TargetVersion;

        UpdatePackageName = "FakeUpdateApp" + UpdateTag + ".zip";

        SettingsViewModel.VersionUpdateSettings.NewVersionFoundInfo = FakeUpdateHelper.HasPendingFakeUpdate
            ? $"{LocalizationHelper.GetString("NewVersionFoundTitle")}: {UpdateTag}"
            : string.Empty;

        if (!FakeUpdateHelper.HasPendingFakeUpdate)
        {
            return CheckUpdateRetT.AlreadyLatest;
        }

        await Task.Delay(TimeSpan.FromSeconds(MinimumDetectedNewVersionDisplaySeconds));
        await SimulateDownloadAsync();

        return CheckUpdateRetT.OK;
    }

    private static async Task SimulateDownloadAsync()
    {
        const long MinPackageSizeMiB = 20;
        const long MaxPackageSizeMiB = 80;
        const long BytesPerMiB = 1024 * 1024;
        const double GigabitBytesPerSecond = 1_000_000_000d / 8d;
        const double LogUpdateIntervalSeconds = 1d;
        const double MinimumDownloadDisplaySeconds = 0.5d;
        const double MinSpeedFactor = 0.25d;
        const double MaxSpeedFactor = 1.20d;
        const double MaxSpeedFactorStepDelta = 0.22d;

        long totalBytes = Random.Shared.NextInt64(MinPackageSizeMiB * BytesPerMiB, (MaxPackageSizeMiB * BytesPerMiB) + 1);

        OutputDownloadProgress(LocalizationHelper.GetString("NewVersionDownloadPreparing"), downloading: false);

        long downloadedBytes = 0;
        double speedFactor = 1d + ((Random.Shared.NextDouble() - 0.5d) * 0.24d);
        bool hasReportedProgress = false;
        while (downloadedBytes < totalBytes)
        {
            speedFactor = Math.Clamp(
                speedFactor + ((Random.Shared.NextDouble() - 0.5d) * MaxSpeedFactorStepDelta * 2d),
                MinSpeedFactor,
                MaxSpeedFactor);

            double bytesPerSecond = GigabitBytesPerSecond * speedFactor;
            long remainingBytes = totalBytes - downloadedBytes;
            double remainingSeconds = remainingBytes / bytesPerSecond;
            double currentIntervalSeconds;

            if (!hasReportedProgress)
            {
                currentIntervalSeconds = Math.Max(MinimumDownloadDisplaySeconds, Math.Min(LogUpdateIntervalSeconds, remainingSeconds));
            }
            else
            {
                currentIntervalSeconds = Math.Min(LogUpdateIntervalSeconds, remainingSeconds);
            }

            long currentChunk = Math.Min(
                remainingBytes,
                Math.Max(1L, (long)Math.Round(bytesPerSecond * currentIntervalSeconds)));
            long currentValue = downloadedBytes + currentChunk;

            await Task.Delay(TimeSpan.FromSeconds(currentIntervalSeconds));

            OutputDownloadProgress(currentValue, totalBytes, (int)currentChunk, currentIntervalSeconds);

            downloadedBytes = currentValue;
            hasReportedProgress = true;
        }

        await Task.Delay(TimeSpan.FromSeconds(MinimumDownloadDisplaySeconds));
        OutputDownloadProgress(downloading: false, output: LocalizationHelper.GetString("NewVersionDownloadCompletedTitle"));
    }

    private async Task<CheckUpdateRetT> HandleUpdateFromGithubRelease()
    {
        // 保存新版本的信息
        var name = _latestJson?["name"]?.ToString();
        UpdateTag = string.IsNullOrEmpty(name) ? (_latestJson?["tag_name"]?.ToString() ?? string.Empty) : name;
        SettingsViewModel.VersionUpdateSettings.NewVersionFoundInfo = $"{LocalizationHelper.GetString("NewVersionFoundTitle")}: {UpdateTag}";
        var body = _latestJson?["body"]?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(body))
        {
            var curHash = ComparableHash(_curVersion);
            var latestHash = ComparableHash(_latestVersion);

            if (curHash != null && latestHash != null)
            {
                body = $"**Full Changelog**: [{curHash} -> {latestHash}](https://github.com/MaaAssistantArknights/MaaAssistantArknights/compare/{curHash}...{latestHash})";
            }
        }

        UpdateInfo = body;
        UpdateUrl = _latestJson?["html_url"]?.ToString() ?? string.Empty;

        bool otaFound = _assetsObject != null;
        bool goDownload = otaFound && SettingsViewModel.VersionUpdateSettings.AutoDownloadUpdatePackage;

        ShowUpdateInfo(otaFound, LocalizationHelper.GetString("NewVersionFoundButtonGoWebpage"));

        UpdatePackageName = _assetsObject?["name"]?.ToString() ?? string.Empty;

        if (!goDownload || string.IsNullOrWhiteSpace(UpdatePackageName))
        {
            OutputDownloadProgress(string.Empty, downloading: false);
            return CheckUpdateRetT.NoNeedToUpdate;
        }

        if (_assetsObject == null)
        {
            return CheckUpdateRetT.FailedToGetInfo;
        }

        string plannedPackagePath = GetPlannedUpdatePackagePath(UpdatePackageName);
        if (_requiresFullPackageConfirmation && !ConfirmFullPackageUpdate(plannedPackagePath))
        {
            _logger.Information("Full package download canceled by user before download: {PackagePath}", plannedPackagePath);
            OutputDownloadProgress(string.Empty, downloading: false);
            return CheckUpdateRetT.NoNeedToUpdate;
        }

        string? rawUrl = _assetsObject["browser_download_url"]?.ToString();
        if (string.IsNullOrEmpty(rawUrl))
        {
            _logger.Error("No browser_download_url found in assets");
            OutputDownloadProgress(downloading: false, output: LocalizationHelper.GetString("NewVersionDownloadFailedTitle"));
            return CheckUpdateRetT.FailedToGetInfo;
        }

        var downloadUrl = MaaUrls.GetGithubProxyUrl(rawUrl);
        _logger.Information("Downloading update package via GitHub proxy: {CDNUrl}", downloadUrl);

        var downloaded = await DownloadGithubAssets(downloadUrl, _assetsObject);
        if (downloaded)
        {
            OutputDownloadProgress(downloading: false, output: LocalizationHelper.GetString("NewVersionDownloadCompletedTitle"));
        }
        else
        {
            OutputDownloadProgress(downloading: false, output: LocalizationHelper.GetString("NewVersionDownloadFailedTitle"));
            {
                using var toast = new ToastNotification(LocalizationHelper.GetString("NewVersionDownloadFailedTitle"));
                toast.AppendContentText(LocalizationHelper.GetString("NewVersionDownloadFailedDesc"))
                    .AddButton(LocalizationHelper.GetString("NewVersionFoundButtonGoWebpage"), ToastNotification.GetActionTagForOpenWeb(UpdateUrl))
                    .Show();
            }

            return CheckUpdateRetT.NoNeedToUpdate;
        }

        return CheckUpdateRetT.OK;

        string? ComparableHash(string version)
        {
            if (IsStdVersion(version) || IsBetaVersion(version))
            {
                return version;
            }

            if (!SemVersion.TryParse(version, SemVersionStyles.AllowLowerV, out var semVersion) ||
                !IsNightlyVersion(semVersion))
            {
                return null;
            }

            // v4.6.6-1.g{Hash}
            // v4.6.7-beta.2.8.g{Hash}
            var commitHash = semVersion.PrereleaseIdentifiers[^1].ToString();
            if (commitHash.StartsWith('g'))
            {
                commitHash = commitHash.Remove(0, 1);
            }

            return commitHash;
        }
    }

    private void ShowUpdateInfo(bool otaFound, string? text)
    {
        bool goDownload = otaFound && SettingsViewModel.VersionUpdateSettings.AutoDownloadUpdatePackage;

        using var toast = new ToastNotification((otaFound ? LocalizationHelper.GetString("NewVersionFoundTitle") : LocalizationHelper.GetString("NewVersionFoundButNoPackageTitle")) + " : " + UpdateTag);
        if (goDownload)
        {
            OutputDownloadProgress(LocalizationHelper.GetString("NewVersionDownloadPreparing"), false);
            toast.AppendContentText(LocalizationHelper.GetString("NewVersionFoundDescDownloadingWithGlobalSource"));
        }

        if (!otaFound)
        {
            toast.AppendContentText(LocalizationHelper.GetString("NewVersionFoundButNoPackageDesc"));
        }

        int count = 0;
        foreach (var line in UpdateInfo.Split('\n'))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            toast.AppendContentText(line);
            if (++count >= 10)
            {
                break;
            }
        }

        if (!string.IsNullOrEmpty(text))
        {
            toast.AddButton(text, ToastNotification.GetActionTagForOpenWeb(UpdateUrl));
        }

        toast.ShowUpdateVersion();
    }

    public async Task AskToRestart()
    {
        await AskToRestartCore(
            LocalizationHelper.GetString("NewVersionDownloadCompletedDesc"),
            LocalizationHelper.GetString("NewVersionDownloadCompletedTitle"));
    }

    public async Task AskToRestartForImportedPackage()
    {
        await AskToRestartCore(
            LocalizationHelper.GetString("LocalUpdatePackageImportedDesc"),
            LocalizationHelper.GetString("LocalUpdatePackageImportedTitle"));
    }

    public static bool ConfirmFullPackageUpdate(string packagePath)
    {
        string baseDir = Path.GetFullPath(PathsHelper.BaseDir);
        string normalizedPackagePath = Path.IsPathRooted(packagePath)
            ? Path.GetFullPath(packagePath)
            : GetPlannedUpdatePackagePath(packagePath);

        MessageBoxResult result = MessageBoxHelper.Show(
            LocalizationHelper.GetStringFormat("PendingFullUpdateManualConfirmDesc", baseDir, normalizedPackagePath),
            LocalizationHelper.GetString("PendingFullUpdateManualConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            yes: LocalizationHelper.GetString("PendingFullUpdateManualConfirmYes"),
            no: LocalizationHelper.GetString("PendingFullUpdateManualConfirmNo"));

        return result == MessageBoxResult.Yes;
    }

    private async Task AskToRestartCore(string description, string title)
    {
        // 自动安装，或用户点「立即更新/确定」：按启动设置决定是否写入 --skip-startup-auto-run。
        // 选「稍后」不会走到这里，之后手动启动是正常流程，不会带 skip 参数。
        string[] updateRestartArgs = Bootstrapper.GetUpdateRestartArgsIfEnabled();

        if (SettingsViewModel.VersionUpdateSettings.AutoInstallUpdatePackage)
        {
            if (FakeUpdateHelper.HasPendingFakeUpdate)
            {
                await _runningState.UntilIdleAsync(1000);
                _ = FakeUpdateHelper.Updating();
                return;
            }

            await Bootstrapper.RestartAfterIdleAsync(updateRestartArgs);
            return;
        }

        await _runningState.UntilIdleAsync(10000);

        var result = MessageBoxHelper.Show(
            description,
            title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            ok: LocalizationHelper.GetString("Ok"),
            cancel: LocalizationHelper.GetString("ManualRestart"));
        if (result == MessageBoxResult.OK)
        {
            if (FakeUpdateHelper.HasPendingFakeUpdate)
            {
                _ = FakeUpdateHelper.Updating();
                return;
            }

            if (updateRestartArgs.Length > 0)
            {
                Bootstrapper.ShutdownAndRestartWithArgs(updateRestartArgs);
            }
            else
            {
                Bootstrapper.ShutdownAndRestartWithoutArgs();
            }
        }
    }

    /// <summary>
    /// 检查更新。
    /// </summary>
    private async Task<CheckUpdateRetT> CheckUpdate()
    {
        // 调试版不检查更新
        if (IsDebugVersion())
        {
            return CheckUpdateRetT.NoNeedToUpdateDebugVersion;
        }

        try
        {
            // 软件版本更新走本 fork 的 GitHub Releases；游戏资源更新仍走上游 MaaResource
            return await CheckUpdateByGithubRelease();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check update from fork GitHub Releases.");
            return CheckUpdateRetT.FailedToGetInfo;
        }
    }

    /// <summary>
    /// 从本 fork 的 GitHub Releases 检查软件更新。
    /// </summary>
    private async Task<CheckUpdateRetT> CheckUpdateByGithubRelease()
    {
        _requiresFullPackageConfirmation = false;

        var releases = await FetchForkReleasesAsync();
        if (releases is null)
        {
            _logger.Error("Failed to get releases from fork repository.");
            return CheckUpdateRetT.NetworkError;
        }

        string versionType = SettingsViewModel.VersionUpdateSettings.VersionType switch {
            VersionUpdateSettingsUserControlModel.UpdateVersionType.Beta => "beta",
            VersionUpdateSettingsUserControlModel.UpdateVersionType.Nightly => "alpha",
            _ => "stable",
        };

        var release = SelectReleaseForChannel(releases, versionType);
        if (release is null)
        {
            _logger.Warning("No suitable release found for channel {Channel}", versionType);
            return CheckUpdateRetT.FailedToGetInfo;
        }

        var latestVersion = release["tag_name"]?.ToString();
        if (string.IsNullOrEmpty(latestVersion))
        {
            return CheckUpdateRetT.FailedToGetInfo;
        }

        if (!NeedToUpdate(latestVersion))
        {
            return CheckUpdateRetT.AlreadyLatest;
        }

        _latestVersion = latestVersion;
        _latestJson = release;
        _assetsObject = SelectWindowsAsset(release, latestVersion);

        if (_assetsObject == null)
        {
            _logger.Warning("No Windows package found in release {Tag}", latestVersion);
            return CheckUpdateRetT.OK; // 仍提示有新版本，但无可下载包
        }

        // fork 目前只发布完整包，无 OTA
        var assetName = _assetsObject["name"]?.ToString() ?? string.Empty;
        if (!assetName.Contains("ota", StringComparison.OrdinalIgnoreCase) &&
            SettingsViewModel.VersionUpdateSettings.AutoDownloadUpdatePackage)
        {
            _requiresFullPackageConfirmation = true;
            _logger.Information("Using full package from fork release: {Asset}", assetName);
            using var toast = new ToastNotification(LocalizationHelper.GetString("NewVersionNoOtaPackage"));
            toast.Show(30);
            Instances.TaskQueueViewModel.AddLog(LocalizationHelper.GetString("NewVersionNoOtaPackage"), UiLogColor.Warning);
        }

        return CheckUpdateRetT.OK;
    }

    /// <summary>
    /// 优先经加速代理请求 fork Releases API，失败时回退直连。
    /// </summary>
    private async Task<JArray?> FetchForkReleasesAsync()
    {
        var headers = new Dictionary<string, string>
        {
            ["Accept"] = "application/vnd.github+json",
            ["X-GitHub-Api-Version"] = "2022-11-28",
        };

        foreach (var url in new[] { MaaUrls.ForkReleasesApiProxied, MaaUrls.ForkReleasesApi })
        {
            try
            {
                _logger.Information("Fetching fork releases: {Url}", url);
                var body = await Instances.HttpService.GetStringAsync(new Uri(url), extraHeader: headers);
                if (string.IsNullOrEmpty(body))
                {
                    continue;
                }

                // 代理/API 限流时可能返回 { "message": "...", "status": "403" }
                if (body.TrimStart().StartsWith('{'))
                {
                    var obj = JObject.Parse(body);
                    _logger.Warning("Fork releases API returned object instead of array: {Message}", obj["message"]?.ToString());
                    continue;
                }

                return JArray.Parse(body);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to fetch fork releases from {Url}", url);
            }
        }

        return null;
    }

    private static JObject? SelectReleaseForChannel(JArray releases, string versionType)
    {
        JObject? fallbackStable = null;

        foreach (var token in releases)
        {
            if (token is not JObject release)
            {
                continue;
            }

            if (release["draft"]?.ToObject<bool>() == true)
            {
                continue;
            }

            var tag = release["tag_name"]?.ToString() ?? string.Empty;
            var prerelease = release["prerelease"]?.ToObject<bool>() ?? false;

            if (!prerelease && fallbackStable is null)
            {
                fallbackStable = release;
            }

            bool accepted = versionType switch {
                "stable" => !prerelease,
                "beta" => !prerelease || tag.Contains("beta", StringComparison.OrdinalIgnoreCase),
                _ => true, // alpha / nightly：取最新（含预发布）
            };

            if (accepted)
            {
                return release;
            }
        }

        // beta 通道若尚无 beta tag，回退到最新正式版
        return versionType == "beta" ? fallbackStable : null;
    }

    private static JObject? SelectWindowsAsset(JObject release, string latestVersion)
    {
        if (release["assets"] is not JArray assets)
        {
            return null;
        }

        var latestVersionLower = latestVersion.ToLowerInvariant();
        JObject? fullPackage = null;
        JObject? otaPackage = null;

        foreach (var token in assets)
        {
            if (token is not JObject asset)
            {
                continue;
            }

            var name = asset["name"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(name) || !name.Contains("win"))
            {
                continue;
            }

            if (IsArm ^ name.Contains("arm"))
            {
                continue;
            }

            if (name.Contains($"maa-{latestVersionLower}-"))
            {
                fullPackage = asset;
            }

            if (name.Contains("ota", StringComparison.Ordinal))
            {
                otaPackage = asset;
            }
        }

        return otaPackage ?? fullPackage;
    }

    private bool NeedToUpdate(string latestVersion)
    {
        if (IsDebugVersion())
        {
            return false;
        }

        bool curParsed = SemVersion.TryParse(_curVersion, SemVersionStyles.AllowLowerV, out var curVersionObj);
        bool latestPared = SemVersion.TryParse(latestVersion, SemVersionStyles.AllowLowerV, out var latestVersionObj);
        if (curParsed && latestPared && curVersionObj != null && latestVersionObj != null)
        {
            return curVersionObj.CompareSortOrderTo(latestVersionObj) < 0;
        }

        return string.CompareOrdinal(_curVersion, latestVersion) < 0;
    }

    private static string GetPlannedUpdatePackagePath(string packageName)
    {
        return Path.GetFullPath(Path.Combine(PathsHelper.BaseDir, packageName));
    }

    /// <summary>
    /// 获取 GitHub Assets 对象对应的文件
    /// </summary>
    /// <param name="url">下载链接</param>
    /// <param name="assetsObject">Github Assets 对象</param>
    /// <returns>操作成功返回 true，反之则返回 false</returns>
    private static async Task<bool> DownloadGithubAssets(string url, JObject assetsObject)
    {
        try
        {
            return await Instances.HttpService.DownloadFileAsync(
                    new(url),
                    assetsObject["name"]!.ToString(),
                    assetsObject["content_type"]?.ToString())
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void OutputDownloadProgress(long value = 0, long maximum = 1, int len = 0, double ts = 1, string? toolTip = null)
    {
        string progress = $"[{value / 1048576.0:F}MiB/{maximum / 1048576.0:F}MiB ({value * 100.0 / maximum:F}%)";

        double speedInKiBPerSecond = len / ts / 1024.0;

        var speedDisplay = speedInKiBPerSecond >= 1024
            ? $"{speedInKiBPerSecond / 1024.0:F} MiB/s"
            : $"{speedInKiBPerSecond:F} KiB/s";

        OutputDownloadProgress(progress + $" {speedDisplay}", toolTip: toolTip);
    }

    public static void OutputDownloadProgress(string output, bool downloading = true, string? toolTip = null)
    {
        string fullText;
        if (downloading)
        {
            fullText = LocalizationHelper.GetString("NewVersionFoundDescDownloadingWithGlobalSource") + "\n" + output;
        }
        else
        {
            fullText = output;
        }

        Instances.TaskQueueViewModel?.UpdateDownloadLog(fullText, toolTip);
    }

    public bool IsDebugVersion(string? version = null)
    {
        // return false;
        version ??= _curVersion;

        // match case 1: DEBUG_VERSION
        // match case 2: v{Major}.{Minor}.{Patch}-{CommitDistance}-g{CommitHash}
        // match case 3: {CommitHash}
        return Regex.IsMatch(version, @"^(.*DEBUG.*|v\d+(\.\d+){1,3}-\d+-g[0-9a-f]{6,}|[^v][0-9a-f]{6,})$");
    }

    public bool IsStdVersion(string? version = null)
    {
        // 正式版：vX.X.X
        // DevBuild (CI)：yyyy-MM-dd-HH-mm-ss-{CommitHash[..7]}
        // DevBuild (Local)：yyyy-MM-dd-HH-mm-ss-{CommitHash[..7]}-Local
        // Release (Local Commit)：v.{CommitHash[..7]}-Local
        // Release (Local Tag)：{Tag}-Local
        // Debug (Local)：DEBUG_VERSION
        // Script Compiled：c{CommitHash[..7]}
        version ??= _curVersion;

        if (IsDebugVersion(version))
        {
            return false;
        }

        if (version.StartsWith('c') || version.StartsWith("20") || version.Contains("Local"))
        {
            return false;
        }

        if (!SemVersion.TryParse(version, SemVersionStyles.AllowLowerV, out var semVersion))
        {
            return false;
        }

        return !semVersion.IsPrerelease;
    }

    public bool IsBetaVersion(string? version = null)
    {
        version ??= _curVersion;

        if (IsDebugVersion(version))
        {
            return false;
        }

        if (version.StartsWith('c') || version.StartsWith("20") || version.Contains("Local"))
        {
            return false;
        }

        if (!SemVersion.TryParse(version, SemVersionStyles.AllowLowerV, out var semVersion))
        {
            return false;
        }

        return semVersion.IsPrerelease && !IsNightlyVersion(semVersion);
    }

    public static bool IsNightlyVersion(SemVersion version)
    {
        if (!version.IsPrerelease)
        {
            return false;
        }

        // ReSharper disable once CommentTypo
        // v{Major}.{Minor}.{Patch}-{Prerelease}.{CommitDistance}.g{CommitHash}
        // v4.6.7-beta.2.1.g1234567
        // v4.6.8-5.g1234567
        if (version.PrereleaseIdentifiers.Count == 0)
        {
            return false;
        }

        var lastId = version.PrereleaseIdentifiers[^1].ToString();
        return lastId.StartsWith('g') && lastId.Length >= 7;
    }
}
