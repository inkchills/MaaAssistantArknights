import json
import multiprocessing
import os
import platform
import re
import tarfile
import zipfile
from multiprocessing import Process, queues
from urllib import request
from urllib.error import HTTPError, URLError

from . import downloader
from .asst import Asst
from .utils import Version


class Updater:
    # 软件版本更新走 fork Releases；资源更新仍由 GUI 侧走上游 MaaResource
    Fork_releases_apis = [
        "https://edgeone.gh-proxy.org/"
        "https://api.github.com/repos/inkchills/MaaAssistantArknights/releases",
        "https://api.github.com/repos/inkchills/MaaAssistantArknights/releases",
    ]
    Github_proxy = "https://edgeone.gh-proxy.org/"

    @staticmethod
    def custom_print(s):
        """
        可以被monkey patch的print，在其他GUI上使用可以被替换为任何需要的输出
        """
        print(s)

    @staticmethod
    def _get_cur_version(path, q):
        """
        从MaaCore.dll获取当前版本号
        这里是复用原来的方法
        """
        Asst.load(path=path)
        q.put(Asst().get_version())

    def __init__(self, path, version):
        self.path = path
        self.version = version
        self.latest_json = None
        self.latest_version = None
        self.assets_object = None

        # 使用子线程获取当前版本后关闭，避免占用dll
        q = queues.Queue(1, ctx=multiprocessing)
        p = Process(
            target=self._get_cur_version,
            args=(
                path,
                q,
            ),
        )
        p.start()
        p.join()
        # MAA当前版本 self.cur_version
        self.cur_version = q.get()

    @staticmethod
    def map_version_type(version):
        type_map = {
            Version.Nightly: "alpha",
            Version.Beta: "beta",
            Version.Stable: "stable",
        }
        return type_map.get(version, "stable")

    def get_latest_version(self):
        """
        从 fork GitHub Releases 获取最新版本，返回 (tag, release_object)
        """
        version_type = self.map_version_type(self.version)
        releases = None
        for api in self.Fork_releases_apis:
            req = request.Request(
                api,
                headers={
                    "Accept": "application/vnd.github+json",
                    "User-Agent": "MaaPythonUpdater",
                },
            )
            try:
                with request.urlopen(req) as response_json:
                    data = json.loads(response_json.read().decode("utf-8"))
                if isinstance(data, list):
                    releases = data
                    break
                self.custom_print(data.get("message", data))
            except Exception as e:
                self.custom_print(e)
                continue

        if not releases:
            return False, False

        fallback_stable = None
        for release in releases:
            if release.get("draft"):
                continue
            tag = release.get("tag_name") or ""
            prerelease = bool(release.get("prerelease"))
            if not prerelease and fallback_stable is None:
                fallback_stable = release

            accepted = False
            if version_type == "stable":
                accepted = not prerelease
            elif version_type == "beta":
                accepted = (not prerelease) or ("beta" in tag.lower())
            else:
                accepted = True

            if accepted:
                return tag, release

        if version_type == "beta" and fallback_stable:
            return fallback_stable.get("tag_name"), fallback_stable
        return False, False

    @staticmethod
    def get_download_url(release):
        """
        1.获取系统及架构信息
        2.找到对应的版本
        3.返回加速代理 url 列表 & 文件名
        """
        system_platform = "win-x64"
        system = platform.system()
        if system == "Linux":
            machine = platform.machine()
            if machine == "aarch64":
                system_platform = "linux-aarch64"
            else:
                system_platform = "linux-x86_64"
        elif system == "Windows":
            machine = platform.machine()
            if machine == "AMD64" or machine == "x86_64":
                system_platform = "win-x64"
            else:
                system_platform = "win-arm64"

        assets_list = release.get("assets") or []
        pattern = r"^MAA-.*-" + re.escape(system_platform) + r"\.(zip|tar\.gz)$"
        for assets in assets_list:
            assets_name = assets["name"]
            match = re.match(pattern, assets_name)
            if match:
                github_url = assets["browser_download_url"]
                proxy_url = Updater.Github_proxy + github_url
                return [proxy_url], assets_name
        return False, False

    def update(self):
        """
        主函数
        """
        # 从dll获取MAA的版本
        current_version = self.cur_version
        # 从 fork Releases 获取最新版本
        latest_version, release = self.get_latest_version()
        if not latest_version:  # latest_version为False代表获取失败
            self.custom_print("获取版本信息失败")
        elif (
            current_version == latest_version
        ):  # 通过比较二者是否一致判断是否需要更新（摆烂
            self.custom_print("当前为最新版本，无需更新")
        else:
            self.custom_print(f"检测到最新版本:{latest_version}，正在更新")
            # 开始更新逻辑
            url_list, filename = self.get_download_url(release)
            if not url_list:
                # 如果请求失败则返回False
                # （此返回值可能会在非Windows-x86_64的程序更新alpha版时出现）
                self.custom_print("未找到适用于当前系统的更新包")
                # 直接结束
                return
            # 将路径和文件名拼合成绝对路径
            # 默认在maa主程序/MaaCore.dll所在路径下
            file = os.path.join(self.path, filename)
            # 下载，调用Downloader下载器，使用url_list（镜像url列表）和file（文件保存路径）两个参数
            # Proxy参数没加，因为可能有问题（也可能没问题反正我晚上Clash连不上）
            # 重试3次
            download_finished = False
            max_retry = 3
            for retry_frequency in range(max_retry):
                try:
                    Updater.custom_print(
                        "开始下载"
                        + (
                            f"，第{retry_frequency}次尝试"
                            if retry_frequency > 1
                            else ""
                        )
                    )
                    # 调用downloader方法进行下载
                    download_finished = downloader.file_download(
                        download_url_list=url_list, download_path=file
                    )
                    break  # RNM怎么会有这么蠢的人忘了写break啊淦
                except (HTTPError, URLError) as e:
                    Updater.custom_print(e)

            if not download_finished:
                Updater.custom_print("下载异常，更新失败")
                return
            # 解压下载的文件，
            Updater.custom_print("开始安装更新，请不要关闭")
            file_extension = os.path.splitext(filename)[1]
            unzip = False
            # 根据拓展名选择解压算法
            # .zip(Windows)/.tar.gz(Linux)
            if file_extension == ".zip":
                zfile = zipfile.ZipFile(file, "r")
                zfile.extractall(self.path)
                zfile.close()
                unzip = True
            # .tar.gz拓展名的情况（按照这个方式得到的拓展名是.gz，但是解压的是tar.gz
            elif file_extension == ".gz":
                tfile = tarfile.open(file, "r:gz")
                tfile.extractall(self.path)
                tfile.close()
                unzip = True
            # 删除压缩包
            os.remove(file)
            if unzip:
                Updater.custom_print("更新完成")
            else:
                Updater.custom_print("更新未完成")
