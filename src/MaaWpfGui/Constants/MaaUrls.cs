// <copyright file="MaaUrls.cs" company="MaaAssistantArknights">
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

using System;
using MaaWpfGui.Configuration.Factory;
using MaaWpfGui.Services.Web;

namespace MaaWpfGui.Constants;

public static class MaaUrls
{
    public const string MaaPlus = "https://maa.plus";

    public const string Bilibili = "https://space.bilibili.com/3493274731940507";

    public const string BilibiliVideo = "https://www.bilibili.com/video/";

    /// <summary>
    /// 上游主仓库（文档 / Issue 等外链仍指向这里）。
    /// </summary>
    public const string GitHub = "https://github.com/MaaAssistantArknights/MaaAssistantArknights";

    /// <summary>
    /// 本 fork 仓库：软件版本更新从此处的 Releases 拉取。
    /// </summary>
    public const string ForkGitHub = "https://github.com/inkchills/MaaAssistantArknights";

    public const string ForkGitHubOwner = "inkchills";

    public const string ForkGitHubRepo = "MaaAssistantArknights";

    /// <summary>
    /// 游戏资源仍走上游 MaaResource 仓库。
    /// </summary>
    public const string ResourceRepository = "https://github.com/MaaAssistantArknights/MaaResource";

    public const string GitHubIssues = "https://github.com/MaaAssistantArknights/MaaAssistantArknights/issues";

    public const string Telegram = "https://t.me/+Mgc2Zngr-hs3ZjU1";

    public const string Discord = "https://discord.gg/23DfZ9uA4V";

    public const string PrtsPlus = "https://prts.plus";

    public const string PrtsPlusCopilotGet = "https://prts.maa.plus/copilot/get/";

    public const string PrtsPlusCopilotRating = "https://prts.maa.plus/copilot/rating";

    public const string PrtsPlusCopilotSetGet = "https://prts.maa.plus/set/get?id=";

    public const string MapPrts = "https://map.ark-nights.com/areas?coord_override=maa";

    public const string MaaApi = "https://api.maa.plus/MaaAssistantArknights/api/";
    public const string MaaApi2 = "https://api2.maa.plus/MaaAssistantArknights/api/";

    public const string QqGroups = "https://api.maa.plus/MaaAssistantArknights/api/qqgroup/index.html";

    public const string QqChannel = "https://pd.qq.com/s/4j1ju9z47";

    public const string GoogleAdbDownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
    public const string AdbMaaMirrorDownloadUrl = "https://api.maa.plus/MaaAssistantArknights/api/binaries/adb-windows.zip";
    public const string AdbMaaMirror2DownloadUrl = "https://api2.maa.plus/MaaAssistantArknights/api/binaries/adb-windows.zip";
    public const string GoogleAdbFilename = "adb-windows.zip";

    private static string Language => ConfigFactory.Root.Gui.Localization;

    private const string MaaDocs = "https://docs.maa.plus";

    // 常见问题
    public static string HelpUri => $"{MaaDocs}/{Language}/manual/faq.html";

    // YostarEN resolution info
    public static string YostarENResolution => $"{MaaDocs}/{Language}/";

    // 外服适配教程
    public static string OverseasAdaptation => $"{MaaDocs}/{Language}/develop/overseas-client-adaptation.html";

    // 基建排班协议文档
    public static string CustomInfrastGenerator => $"{MaaDocs}/{Language}/protocol/base-scheduling-schema.html";

    // 远程控制协议文档
    public static readonly string RemoteControlDocument = $"{MaaDocs}/{Language}/protocol/remote-control-schema.html";

    public static string NewIssueUri => Language switch {
        "zh-cn" => $"{GitHubIssues}/new?assignees=&labels=bug&template=cn-bug-report.yaml",
        "zh-tw" => $"{GitHubIssues}/new?assignees=&labels=bug&template=cn-bug-report.yaml",
        _ => $"{GitHubIssues}/new?assignees=&labels=bug&template=en-bug-report.yaml",
    };

    // GitHub 加速代理列表（资源 / 软件更新前会测速，优先使用最快的）
    public static readonly string[] GithubProxies =
    [
        "https://ghfast.top/",
        "https://v6.gh-proxy.org/",
        "https://hk.gh-proxy.org/",
        "https://cdn.gh-proxy.org/",
        "https://edgeone.gh-proxy.org/",
        "https://gh.inkchills.cn/",
    ];

    /// <summary>
    /// 默认代理（测速完成前使用）。
    /// </summary>
    public const string DefaultGithubProxy = "https://edgeone.gh-proxy.org/";

    /// <summary>
    /// 当前选中的最快 GitHub 加速代理。
    /// </summary>
    public static string GithubProxy => GithubProxySelector.CurrentProxy;

    /// <summary>
    /// 去掉已知代理前缀，还原原始 GitHub / raw 链接。
    /// </summary>
    public static string StripGithubProxy(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        foreach (var proxy in GithubProxies)
        {
            if (url.StartsWith(proxy, StringComparison.OrdinalIgnoreCase))
            {
                return url[proxy.Length..];
            }
        }

        return url;
    }

    /// <summary>
    /// 将 GitHub / raw.githubusercontent.com 链接套上指定代理。
    /// </summary>
    public static string ApplyGithubProxy(string url, string proxy)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var stripped = StripGithubProxy(url);
        if (!(stripped.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
              stripped.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            return stripped;
        }

        if (string.IsNullOrWhiteSpace(proxy))
        {
            return stripped;
        }

        return proxy.EndsWith('/') ? proxy + stripped : proxy + "/" + stripped;
    }

    /// <summary>
    /// 将 GitHub / raw.githubusercontent.com 链接转换为当前最快加速代理地址。
    /// </summary>
    public static string GetGithubProxyUrl(string url)
    {
        return ApplyGithubProxy(url, GithubProxy);
    }

    /// <summary>
    /// 本 fork 的 GitHub Releases API（直连）。
    /// </summary>
    public static string ForkReleasesApi =>
        $"https://api.github.com/repos/{ForkGitHubOwner}/{ForkGitHubRepo}/releases";

    /// <summary>
    /// 本 fork 的 GitHub Releases API（经当前最快加速代理）。
    /// </summary>
    public static string ForkReleasesApiProxied => GetGithubProxyUrl(ForkReleasesApi);

    // 资源更新：始终使用上游 MaaResource（经当前最快加速代理）
    public static string GithubResourceUpdate => GetGithubProxyUrl($"{ResourceRepository}/archive/refs/heads/main.zip");

    public static string GithubResourceVersionJson => GetGithubProxyUrl("https://raw.githubusercontent.com/MaaAssistantArknights/MaaResource/main/resource/version.json");

    // 企鹅物流
    public const string PenguinIoDomain = "https://penguin-stats.io";
    public static readonly string[] PenguinBackupDomains =
    [
        /*"https://penguin-stats.alvorna.com",*/
        "https://penguin-stats.cn"
    ];
}
