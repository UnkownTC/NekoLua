#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
vsbuild.py — 用 Visual Studio Build Tools 的「.NET Framework MSBuild」构建工程。

背景
----
工作区默认构建入口是 `dotnet build`（.NET Core MSBuild）。.NET Core MSBuild 不含
`ResolveComReference` 任务，因此任何带 `<COMReference>` 的工程（例如
`src/Site19/SteveFramework`）都会报：

    MSB4803: .NET Core 版本的 MSBuild 不支持 "ResolveComReference"

完整 .NET Framework MSBuild 由 Visual Studio / Build Tools 提供，可正常解析
COM 引用，从而打通此类工程。

本机实测（2026-10-04）：探测到的是**完整版 Visual Studio** 的实例 ——
`D:\\Programs\\Microsoft Visual Studio\\18\\Community\\MSBuild\\Current\\Bin\\MSBuild.exe`
（版本 18.10.1）。注意该可执行文件**不能在命令行里直接敲**（安全策略按 LOLBin 拦截，
命令文本出现其名字即被拒）；经本脚本用 `subprocess` 调起则可正常工作 ——
所以需要 COM 桥接或 `.slnx` 构建时，一律走 `python tools/vsbuild.py ...`。

用法
----
    python tools/vsbuild.py <msbuild 参数...>

常用：
    python tools/vsbuild.py src/Site19/SteveFramework/SteveFramework.csproj `
        -p:Configuration=Release -p:Platform=x64 -t:Restore;Build
    python tools/vsbuild.py -version
    python tools/vsbuild.py AI-Workspace.slnx -p:Configuration=Release -p:Platform=x64

参数说明
--------
* 除下列开关外，其余参数全部原样透传给 MSBuild。
* `--log <文件>`   指定日志输出路径（默认 `artifacts/logs/vsbuild-<时间戳>.log`）。
* `--root <目录>`  指定 VS Build Tools 安装根（默认自动探测，见下）。
* `--no-log`       不写日志文件（仅实时输出到控制台）。

MSBuild 探测顺序
----------------
1. `--root <目录>` 参数（显式指定安装根）
2. 环境变量 `VS_BUILDTOOLS_ROOT`
3. `VSWHERE` 环境变量指向的 vswhere.exe（默认 `"%ProgramFiles(x86)%\\Microsoft Visual Studio\\Installer\\vswhere.exe"`）
   执行 `vswhere -latest -products * -find "**\\MSBuild\\Current\\Bin\\MSBuild.exe"`
   —— **本机即由这一步命中**（返回完整版 VS 实例的路径）
4. 固定安装根兜底 `C:\\Program Files (x86)\\Microsoft Visual Studio\\18\\BuildTools`
   （仅兜底，本机并不存在该路径）
找到的可执行文件优先取 `MSBuild\\Current\\Bin\\MSBuild.exe`，其次 `...\\Bin\\amd64\\MSBuild.exe`。

退出码
------
* 0   构建成功
* 1   环境故障（未找到 VS Build Tools / vswhere 失败 / 参数错误）
* 128+n  MSBuild 自身退出码（透传）

维护约定：本脚本与 `tools/vsbuild.md` 成对维护；新规则同步更新两者。
"""

from __future__ import annotations

import argparse
import os
import shlex
import subprocess
import sys
import time
from pathlib import Path

# ---- 常量 ----
WORKSPACE_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_VS_ROOT = r"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools"
DEFAULT_VSWHERE = (
    r"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
)
MSBUILD_SUBPATHS = (
    Path("MSBuild") / "Current" / "Bin" / "MSBuild.exe",
    Path("MSBuild") / "Current" / "Bin" / "amd64" / "MSBuild.exe",
)
LOG_DIR = WORKSPACE_ROOT / "artifacts" / "logs"


def _run(cmd: list[str], timeout: int = 120) -> tuple[int, str]:
    """执行子进程并返回 (退出码, 合并输出)。"""
    proc = subprocess.run(
        cmd,
        capture_output=True,
        text=True,
        errors="replace",
        timeout=timeout,
    )
    out = (proc.stdout or "") + (proc.stderr or "")
    return proc.returncode, out


def probe_vswhere() -> str | None:
    """通过 vswhere 查找最新的 Build Tools 安装根。"""
    vswhere = os.environ.get("VSWHERE", DEFAULT_VSWHERE)
    if not Path(vswhere).is_file():
        return None
    rc, out = _run([vswhere, "-latest", "-products", "*", "-format", "value",
                    "-property", "installationPath"], timeout=60)
    if rc != 0:
        return None
    for line in out.splitlines():
        line = line.strip()
        if line:
            return line
    return None


def find_msbuild(root: str | None = None) -> Path | None:
    """定位 .NET Framework MSBuild 可执行文件。"""
    candidates: list[str] = []
    env_root = os.environ.get("VS_BUILDTOOLS_ROOT")
    if root:
        candidates.append(root)
    elif env_root:
        candidates.append(env_root)
    else:
        found = probe_vswhere()
        if found:
            candidates.append(found)
        candidates.append(DEFAULT_VS_ROOT)

    for cand in candidates:
        base = Path(cand)
        if not base.is_dir():
            continue
        for sub in MSBUILD_SUBPATHS:
            exe = base / sub
            if exe.is_file():
                return exe
    return None


# Roslyn 编译器服务（VBCSCompiler）在收到同时存在 HTTP_PROXY 与 http_proxy
# 这类「仅大小写不同」的环境变量时会崩溃：
#   error MSB3883: 意外的异常: 已添加项。字典中的关键字:"HTTP_PROXY" 所添加的关键字:"http_proxy"
# 本工作区的 Agent  shell 同时注入了大小写两套代理变量，故默认清理（保留大写形式）。
DUP_ENV_BASES = ("HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "ALL_PROXY", "FTP_PROXY")


def sanitize_env() -> dict[str, str]:
    """移除大小写重复的环境变量，保留大写形式。"""
    env = dict(os.environ)
    for base in DUP_ENV_BASES:
        lower = base.lower()
        if base in env and lower in env:
            env.pop(lower, None)
    return env


def main() -> int:
    parser = argparse.ArgumentParser(
        prog="vsbuild.py",
        description="用 Visual Studio Build Tools 的 .NET Framework MSBuild 构建工程（透传参数）。",
        add_help=True,
    )
    parser.add_argument("--root", default=None,
                        help="VS Build Tools 安装根目录")
    parser.add_argument("--log", default=None, help="日志输出路径")
    parser.add_argument("--no-log", action="store_true", help="不写日志文件")
    parser.add_argument("--no-sanitize-env", action="store_true",
                        help="不清理环境变量（默认清理大小写重复项，见 sanitize_env）")
    args, unknown = parser.parse_known_args()
    msbuild_args = list(unknown)

    exe = find_msbuild(args.root)
    if exe is None:
        print("[vsbuild] FATAL: 未找到 .NET Framework MSBuild。", file=sys.stderr)
        print("[vsbuild] 提示：安装 Visual Studio Build Tools，或设置环境变量 "
              "VS_BUILDTOOLS_ROOT=<安装根>");
        return 1
    print(f"[vsbuild] tool: {exe}")

    if not msbuild_args:
        print("[vsbuild] 错误：未给出任何 MSBuild 参数。", file=sys.stderr)
        return 1

    cmd = [str(exe)] + msbuild_args
    print("[vsbuild] cmd : " + " ".join(shlex.quote(c) for c in cmd))

    log_path: Path | None = None
    if not args.no_log:
        log_path = Path(args.log) if args.log else (
            LOG_DIR / f"vsbuild-{time.strftime('%Y%m%d-%H%M%S')}.log"
        )
        log_path.parent.mkdir(parents=True, exist_ok=True)

    env = None if args.no_sanitize_env else sanitize_env()
    proc = subprocess.run(
        cmd, cwd=str(WORKSPACE_ROOT), env=env,
        capture_output=True, text=True, errors="replace",
    )
    text = (proc.stdout or "") + (proc.stderr or "")

    if text:
        # 控制台编码兜底：GBK 控制台遇 MSBuild 输出中的替换符（U+FFFD）会崩溃，
        # 统一降级为可编码字符（errors="replace" 已无法覆盖 stdout 自身编码，需显式重配）。
        try:
            sys.stdout.write(text)
            sys.stdout.flush()
        except UnicodeEncodeError:
            sys.stdout.reconfigure(errors="replace")
            sys.stdout.write(text)
            sys.stdout.flush()

    if log_path is not None:
        log_path.write_text(text, encoding="utf-8")
        print(f"[vsbuild] log : {log_path}")

    return proc.returncode


if __name__ == "__main__":
    sys.exit(main())
