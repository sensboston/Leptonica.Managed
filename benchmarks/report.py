#!/usr/bin/env python3
"""Charts and tables from benchmark results (benchmarks/run.ps1 output).

Each input is a result folder or zip with <prefix>_sysinfo.json, <prefix>_native.csv and
<prefix>_managed_cpu_1t.csv, _managed_cpu_mt.csv, _managed_gpu_auto.csv.

Writes:
  docs/images/benchmark-overview.png     geometric mean speedup per machine and mode
  docs/images/benchmark-operations.png   speedup of every operation on every machine
  docs/benchmarks.md                     the same numbers as tables

Usage: python benchmarks/report.py results/*.zip [--repo <repository root>]
Needs matplotlib and numpy.
"""
from __future__ import annotations

import argparse
import csv
import io
import json
import math
import zipfile
from dataclasses import dataclass, field
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.colors as mcolors
import numpy as np

MODES = [("cpu_1t", "1 thread"), ("cpu_mt", "multithreaded"), ("gpu_auto", "default (threads + GPU)")]
MODE_COLORS = {"cpu_1t": "#64748b", "cpu_mt": "#2563eb", "gpu_auto": "#059669"}


@dataclass
class Case:
    name: str
    function: str
    category: str
    median_ms: float
    gpu_used: bool = False


@dataclass
class Machine:
    cpu: str
    gpu: str
    os: str
    native: dict[str, Case] = field(default_factory=dict)
    managed: dict[str, dict[str, Case]] = field(default_factory=dict)

    @property
    def label(self) -> str:
        return f"{self.cpu}\n{self.gpu}"

    def speedup(self, mode: str, case: str) -> float | None:
        n, m = self.native.get(case), self.managed[mode].get(case)
        if not n or not m or m.median_ms <= 0:
            return None
        return n.median_ms / m.median_ms

    def geomean(self, mode: str) -> float:
        vals = [s for c in self.native if (s := self.speedup(mode, c))]
        return math.exp(sum(math.log(v) for v in vals) / len(vals))


def read_csv(text: str) -> dict[str, Case]:
    rows = [ln for ln in text.splitlines() if ln.strip() and not ln.startswith("#")]
    out = {}
    for r in csv.DictReader(rows):
        out[r["case"]] = Case(r["case"], r.get("function", ""), r.get("category", ""),
                              float(r["median_ms"]),
                              str(r.get("gpu_used", "")).lower() in ("1", "yes", "true"))
    return out


def short_cpu(name: str) -> str:
    s = name.replace("(R)", "").replace("(TM)", "").replace(" CPU", "").replace("Processor", "")
    s = s.split("@")[0].replace("Intel Core ", "Core ").replace("Intel ", "")
    return " ".join(s.split())


def load(path: Path) -> Machine:
    if path.suffix == ".zip":
        z = zipfile.ZipFile(path)
        files = {Path(n).name: n for n in z.namelist()}
        read = lambda n: z.read(files[n]).decode("utf-8-sig", "replace")
        names = files.keys()
    else:
        read = lambda n: (path / n).read_text("utf-8-sig", "replace")
        names = [p.name for p in path.iterdir()]
    sysinfo = next(n for n in names if n.endswith("_sysinfo.json"))
    prefix = sysinfo[: -len("_sysinfo.json")]
    info = json.loads(read(sysinfo))
    native = read_csv(read(f"{prefix}_native.csv"))
    managed = {m: read_csv(read(f"{prefix}_managed_{m}.csv")) for m, _ in MODES}
    for c in native.values():
        c.category = managed["cpu_1t"][c.name].category if c.name in managed["cpu_1t"] else ""
    used = [c for c in managed["gpu_auto"].values() if c.gpu_used]
    gpus = [g.get("name", "") for g in info.get("gpus") or [] if g.get("name")]
    gpu = gpus[0].replace("NVIDIA GeForce ", "").replace("NVIDIA ", "").replace("(R)", "") if gpus and used else "no GPU"
    return Machine(short_cpu(info["cpus"][0]["name"]), gpu, info.get("os", ""), native, managed)


def plot_overview(machines: list[Machine], out: Path) -> None:
    fig, ax = plt.subplots(figsize=(10, 4.8), dpi=150)
    x = np.arange(len(machines))
    w = 0.26
    for i, (mode, title) in enumerate(MODES):
        vals = [m.geomean(mode) for m in machines]
        bars = ax.bar(x + (i - 1) * w, vals, w, label=title, color=MODE_COLORS[mode])
        for b, v in zip(bars, vals):
            ax.text(b.get_x() + b.get_width() / 2, v + 0.02, f"{v:.2f}x", ha="center", va="bottom", fontsize=9)
    ax.axhline(1.0, color="#dc2626", ls="--", lw=1.2, label="native C = 1.00x")
    ax.set_xticks(x, [m.label for m in machines], fontsize=9)
    ax.set_ylabel("speedup over native C (geometric mean)")
    ax.set_title(f"Leptonica.Managed vs. native Leptonica 1.87.0, {len(machines[0].native)} operations",
                 fontsize=12, fontweight="bold")
    ax.set_ylim(0, max(m.geomean("gpu_auto") for m in machines) * 1.25)
    ax.spines[["top", "right"]].set_visible(False)
    ax.grid(axis="y", alpha=0.3)
    ax.legend(loc="upper right", fontsize=9, frameon=False)
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)


def ordered_cases(machines: list[Machine]) -> list[str]:
    cases = list(machines[0].native)
    cats = list(dict.fromkeys(machines[0].native[c].category for c in cases))
    return sorted(cases, key=lambda c: (cats.index(machines[0].native[c].category), cases.index(c)))


def plot_operations(machines: list[Machine], out: Path) -> None:
    cases = ordered_cases(machines)
    cols = [(m, mode) for m in machines for mode in ("cpu_1t", "gpu_auto")]
    data = np.array([[cm.speedup(mode, c) or np.nan for cm, mode in cols] for c in cases])
    fig, ax = plt.subplots(figsize=(2.0 + 1.25 * len(cols), 0.28 * len(cases) + 1.8), dpi=130)
    norm = mcolors.TwoSlopeNorm(vmin=math.log2(0.25), vcenter=0.0, vmax=math.log2(8))
    cmap = mcolors.LinearSegmentedColormap.from_list("s", ["#dc2626", "#fbbf24", "#ffffff", "#34d399", "#065f46"])
    ax.imshow(np.log2(data), cmap=cmap, norm=norm, aspect="auto")
    for i, c in enumerate(cases):
        for j, (m, mode) in enumerate(cols):
            v = data[i, j]
            if np.isnan(v):
                continue
            g = "G " if m.managed[mode].get(c) and m.managed[mode][c].gpu_used else ""
            ax.text(j, i, f"{g}{v:.2f}x", ha="center", va="center", fontsize=7,
                    color="white" if abs(math.log2(v)) > 1.6 else "black")
    ax.set_yticks(range(len(cases)), [f"{machines[0].native[c].category:<12}{c:<20}" for c in cases],
                  fontsize=7, family="monospace")
    ax.set_xticks(range(len(cols)), [f"{m.cpu}\n{'1 thread' if mode == 'cpu_1t' else 'default'}"
                                     for m, mode in cols], fontsize=7)
    ax.xaxis.tick_top()
    for k in range(1, len(machines)):
        ax.axvline(2 * k - 0.5, color="black", lw=1)
    ax.set_title("speedup over native C per operation (G: ran on the GPU)", fontsize=10, pad=28)
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)


def write_markdown(machines: list[Machine], out: Path) -> None:
    L = ["# Benchmarks", "",
         "Leptonica.Managed compared with the native Leptonica 1.87.0 library on the same machine and",
         "the same inputs. The numbers are speedups `T(native) / T(managed)` of the median time of",
         "each operation; above 1.00 the managed code is faster. Generated by `benchmarks/report.py`.", "",
         "- **native**: the C library, Release build (MSVC x64, codecs from vcpkg), one thread.",
         "- **1 thread**: Leptonica.Managed with one thread and no GPU, the same work as the C library.",
         "- **multithreaded**: all CPU cores, no GPU.",
         "- **default**: what an application gets without any configuration: all cores, and the GPU",
         "  for the operations that have GPU kernels when a GPU is present.", "",
         "![overview](images/benchmark-overview.png)", "", "## Machines", "",
         "| CPU | GPU | OS | " + " | ".join(t for _, t in MODES) + " |",
         "|---|---|---|" + "---:|" * len(MODES)]
    for m in machines:
        L.append(f"| {m.cpu} | {m.gpu} | {m.os} | " + " | ".join(f"{m.geomean(k):.2f}x" for k, _ in MODES) + " |")
    L += ["", "## Operations", "", "![operations](images/benchmark-operations.png)", ""]
    head = "| operation | C function | " + " | ".join(f"{m.cpu}: native ms / 1 thread / default" for m in machines) + " |"
    L += [head, "|---|---|" + "---|" * len(machines)]
    for c in ordered_cases(machines):
        cells = []
        for m in machines:
            s1, sd = m.speedup("cpu_1t", c), m.speedup("gpu_auto", c)
            g = " (GPU)" if m.managed["gpu_auto"].get(c) and m.managed["gpu_auto"][c].gpu_used else ""
            cells.append(f"{m.native[c].median_ms:.3f} / {s1:.2f}x / {sd:.2f}x{g}" if s1 and sd else "")
        L.append(f"| `{c}` | `{machines[0].native[c].function}` | " + " | ".join(cells) + " |")
    out.write_text("\n".join(L) + "\n", encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("inputs", nargs="+", type=Path)
    ap.add_argument("--repo", type=Path, default=Path(__file__).resolve().parent.parent)
    a = ap.parse_args()
    machines = [load(p) for p in a.inputs]
    images = a.repo / "docs" / "images"
    images.mkdir(parents=True, exist_ok=True)
    plot_overview(machines, images / "benchmark-overview.png")
    plot_operations(machines, images / "benchmark-operations.png")
    write_markdown(machines, a.repo / "docs" / "benchmarks.md")
    for m in machines:
        print(f"{m.cpu} + {m.gpu}: " + ", ".join(f"{t} {m.geomean(k):.2f}x" for k, t in MODES))


if __name__ == "__main__":
    main()
