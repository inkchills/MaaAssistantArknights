// <copyright file="GithubProxySelector.cs" company="MaaAssistantArknights">
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
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MaaWpfGui.Services.Web;

/// <summary>
/// 对多个 GitHub 加速代理并行测速，缓存并优先使用最快可用地址。
/// 注意：本类型的静态初始化不得依赖 <c>MaaUrls</c> / <c>ConfigFactory</c>，
/// 否则会在配置加载前触发 LocalizationHelper 的 Lazy 重入并导致启动崩溃。
/// </summary>
public static class GithubProxySelector
{
    private static readonly ILogger _logger = Log.ForContext(typeof(GithubProxySelector));

    /// <summary>
    /// GitHub 加速代理列表（资源 / 软件更新前会测速，优先使用最快的）。
    /// </summary>
    public static readonly string[] AllProxies =
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
    public const string DefaultProxy = "https://edgeone.gh-proxy.org/";

    /// <summary>
    /// 用于测速的轻量资源（上游 MaaResource version.json）。
    /// </summary>
    private const string ProbeTarget =
        "https://raw.githubusercontent.com/MaaAssistantArknights/MaaResource/main/resource/version.json";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private static readonly object LockObj = new();
    private static string _currentProxy = DefaultProxy;
    private static IReadOnlyList<string> _rankedProxies = AllProxies;
    private static DateTimeOffset _lastProbeUtc = DateTimeOffset.MinValue;
    private static Task? _inflightProbe;

    /// <summary>
    /// 当前选中的最快代理（末尾带 /）。
    /// </summary>
    public static string CurrentProxy
    {
        get
        {
            lock (LockObj)
            {
                return _currentProxy;
            }
        }
    }

    /// <summary>
    /// 按测速结果排序的代理列表（不可用的排在后面，仍保留以便回退）。
    /// </summary>
    public static IReadOnlyList<string> RankedProxies
    {
        get
        {
            lock (LockObj)
            {
                return _rankedProxies;
            }
        }
    }

    /// <summary>
    /// 去掉已知代理前缀，还原原始 GitHub / raw 链接。
    /// </summary>
    public static string StripProxy(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        foreach (var proxy in AllProxies)
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
    public static string ApplyProxy(string url, string proxy)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var stripped = StripProxy(url);
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
    /// 确保已完成测速并选好最快代理。短时间内重复调用会复用缓存。
    /// </summary>
    public static async Task EnsureFastestAsync(bool force = false)
    {
        Task probeTask;
        lock (LockObj)
        {
            if (!force && _inflightProbe == null && DateTimeOffset.UtcNow - _lastProbeUtc < CacheTtl)
            {
                return;
            }

            if (_inflightProbe != null)
            {
                probeTask = _inflightProbe;
            }
            else
            {
                probeTask = ProbeAllAsync();
                _inflightProbe = probeTask;
            }
        }

        try
        {
            await probeTask.ConfigureAwait(false);
        }
        finally
        {
            lock (LockObj)
            {
                if (ReferenceEquals(_inflightProbe, probeTask))
                {
                    _inflightProbe = null;
                }
            }
        }
    }

    /// <summary>
    /// 生成按测速排序的代理 URL 列表（含直连作为最终回退）。
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetOrderedProxyUrlsAsync(string originalUrl, bool includeDirect = true)
    {
        await EnsureFastestAsync().ConfigureAwait(false);

        var stripped = StripProxy(originalUrl);
        var urls = new List<string>();
        foreach (var proxy in RankedProxies)
        {
            urls.Add(ApplyProxy(stripped, proxy));
        }

        if (includeDirect && !urls.Contains(stripped, StringComparer.OrdinalIgnoreCase))
        {
            urls.Add(stripped);
        }

        return urls;
    }

    private static async Task ProbeAllAsync()
    {
        _logger.Information("Probing GitHub proxies for fastest endpoint...");

        using var client = new HttpClient
        {
            Timeout = ProbeTimeout,
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MaaWpfGui-GithubProxySelector");

        var tasks = AllProxies.Select(proxy => ProbeOneAsync(client, proxy)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var ranked = results
            .OrderBy(r => r.LatencyMs < 0 ? double.MaxValue : r.LatencyMs)
            .ThenBy(r => Array.IndexOf(AllProxies, r.Proxy))
            .Select(r => r.Proxy)
            .ToArray();

        var best = results
            .Where(r => r.LatencyMs >= 0)
            .OrderBy(r => r.LatencyMs)
            .FirstOrDefault();

        lock (LockObj)
        {
            _rankedProxies = ranked;
            _currentProxy = best.Proxy ?? DefaultProxy;
            _lastProbeUtc = DateTimeOffset.UtcNow;
        }

        foreach (var r in results.OrderBy(x => x.LatencyMs < 0 ? double.MaxValue : x.LatencyMs))
        {
            if (r.LatencyMs < 0)
            {
                _logger.Warning("GitHub proxy probe failed: {Proxy} ({Error})", r.Proxy, r.Error ?? "unknown");
            }
            else
            {
                _logger.Information("GitHub proxy probe ok: {Proxy} = {Latency:F0} ms", r.Proxy, r.LatencyMs);
            }
        }

        _logger.Information("Selected GitHub proxy: {Proxy}", CurrentProxy);
    }

    private static async Task<ProbeResult> ProbeOneAsync(HttpClient client, string proxy)
    {
        var url = ApplyProxy(ProbeTarget, proxy);
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(ProbeTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token)
                .ConfigureAwait(false);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return new ProbeResult(proxy, -1, $"HTTP {(int)response.StatusCode}");
            }

            // 读一小段内容，避免只碰到握手很快但实际不可用的节点
            var buffer = new byte[256];
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            _ = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false);
            sw.Stop();
            return new ProbeResult(proxy, sw.Elapsed.TotalMilliseconds, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult(proxy, -1, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private readonly record struct ProbeResult(string Proxy, double LatencyMs, string? Error);
}
