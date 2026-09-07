#!/usr/bin/env python3
"""Turn benchmark JSON reports into slide-ready charts.

Usage: plot_bench.py [--out charts] [--labels "Blade 18,ROG Strix,Steam Deck"] [--png] report1.json report2.json ...

Reports are grouped by device (one report per device; --labels renames them in file order).
Written as SVG (add --png for 2x PNGs too, e.g. for Google Slides, which does not import SVG). Dark
background, horizontal bars, values at the bar ends:

  summary/objects_at_30fps        largest count that held a stable 30 fps, per step, one bar per device
  summary/fps_at_1m               average fps at one million objects, per step, one bar per device
  summary/frametime_vs_count      average frame time against object count, one line per step per device
  steps/<step>/fps_at_1m          a row per device with average and 1% low fps at one million objects
  steps/<step>/objects_at_30fps   a row per device with the 30 fps count
  steps/<step>/frametime_vs_count frame time against object count for this step, one line per device
"""
import argparse
import json
import os
import re
import sys

try:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
except ImportError:
    sys.exit("matplotlib is required: pip install matplotlib")

HEADLINE_COUNT = 1_000_000
TARGET_MS = 33.333

BACKGROUND = "#2b2b2b"
TEXT = "#f2f2f2"
MUTED = "#b8b8b8"
GRID = "#3d3d3d"
SERIES_COLORS = {"avg": "#2196f3", "low1": "#f57c00"}
# Each metric owns a colour family so "fps" and "objects" charts never look like the same benchmark;
# devices are shades within the family.
METRIC_COLORS = {
    "fps": ["#2196f3", "#a5d6ff", "#0d47a1", "#64b5f6", "#e3f2fd", "#1565c0"],
    "objects": ["#26a69a", "#b2ece6", "#00594f", "#66d1c4", "#e0f7f4", "#00897b"],
}
DEVICE_COLORS = ["#2196f3", "#f57c00", "#ffb300", "#66bb6a", "#ab47bc", "#26c6da"]   # line chart series

plt.rcParams.update({
    "figure.facecolor": BACKGROUND, "axes.facecolor": BACKGROUND, "savefig.facecolor": BACKGROUND,
    "text.color": TEXT, "axes.labelcolor": TEXT, "xtick.color": MUTED, "ytick.color": TEXT,
    "font.family": "DejaVu Sans", "font.size": 11, "axes.titleweight": "bold", "axes.titlesize": 16,
})


# ---------------------------------------------------------------------------------------------------
# Data access
# ---------------------------------------------------------------------------------------------------
def load_reports(paths, labels):
    reports = []
    for index, path in enumerate(paths):
        with open(path, encoding="utf-8") as handle:
            report = json.load(handle)
        report["_label"] = labels[index] if index < len(labels) else report["device"].get("model", f"device {index + 1}")
        reports.append(report)
    return reports


def device_label(report):
    return f'{report["_label"]}\n{report["device"].get("resolution", "?")}'


def backend_order(reports):
    order = []
    for report in reports:
        for row in report["sweep"]:
            if row["backend"] not in order:
                order.append(row["backend"])
    return order


def step_slug(backend):
    """'3 · DOTS / ECS' -> 'step3_dots_ecs', used as the per-step folder name."""
    words = re.sub(r"[^a-z0-9]+", "_", backend.lower()).strip("_")
    match = re.match(r"(\d+)_(.*)", words)
    return f"step{match.group(1)}_{match.group(2)}" if match else words


def sweep_row(report, backend, count):
    """The sweep row for a count when it was measured (ok or aborted); skipped and timed-out rows carry no timings."""
    for row in report["sweep"]:
        if row["backend"] == backend and row["count"] == count and row["status"] in ("ok", "aborted"):
            return row
    return None


def count_at_target(report, backend):
    for row in report["countAtTarget"]:
        if row["backend"] == backend:
            return row
    return None


def fps(ms):
    return 1000.0 / ms if ms else None


def footnote(reports):
    parts = [f'{r["_label"]}: {r["device"].get("gpu", "?")}, {r["device"].get("resolution", "?")}, build {r["device"].get("gitHash", "?")}' for r in reports]
    return "Resolution is native to each target, not equalised.  " + "  |  ".join(parts)


# ---------------------------------------------------------------------------------------------------
# Drawing primitives
# ---------------------------------------------------------------------------------------------------
def new_figure(rows, title, direction, legend=None):
    """Dark figure sized to the number of bar rows: bold title, 'higher/lower is better' line, dot legend."""
    height = max(3.8, 2.1 + rows * 0.42)
    fig, ax = plt.subplots(figsize=(13, height))
    inch = 1.0 / height   # figure fraction per inch, so the header stacks the same on every chart height
    fig.suptitle(title, y=1.0 - 0.15 * inch, va="top", fontsize=17, fontweight="bold", color=TEXT)
    fig.text(0.5, 1.0 - 0.55 * inch, direction, ha="center", va="top", fontsize=11, color=MUTED, style="italic")
    if legend:
        handles = [plt.Line2D([], [], marker="o", linestyle="", markersize=10, color=color, label=label) for label, color in legend]
        fig.legend(handles=handles, loc="upper center", bbox_to_anchor=(0.5, 1.0 - 0.8 * inch), ncol=len(legend), frameon=False, fontsize=12)
    fig.subplots_adjust(top=1.0 - 1.35 * inch)
    for spine in ax.spines.values():
        spine.set_visible(False)
    ax.tick_params(axis="both", length=0)
    ax.set_xticks([])
    return fig, ax


def draw_bars(ax, rows):
    """rows: list of (group_label, [(value, color, text, hatched), ...]) drawn top to bottom as horizontal bars."""
    bar_height = 0.8
    gap = 0.6
    y = 0.0
    ticks, labels = [], []
    max_value = max((bar[0] or 0) for _, bars in rows for bar in bars) or 1.0
    for group_label, bars in rows:
        group_top = y
        for value, color, text, hatched in bars:
            ax.barh(y, value or 0, height=bar_height, color=color, alpha=0.45 if hatched else 1.0, hatch="///" if hatched else None, edgecolor=BACKGROUND)
            if text:
                ax.text((value or 0) + max_value * 0.008, y, text, va="center", ha="left", fontsize=11, fontweight="bold", color=TEXT)
            y += 1.0
        ticks.append((group_top + y - 1.0) / 2)
        labels.append(group_label)
        y += gap
    ax.set_yticks(ticks)
    ax.set_yticklabels(labels, fontsize=12, fontweight="bold")
    ax.set_ylim(y - gap - 0.5, -0.5)
    longest_text = max((len(line) for _, bars in rows for bar in bars if bar[2] for line in bar[2].split("\n")), default=8)
    ax.set_xlim(0, max_value * (1.04 + 0.0135 * longest_text))
    longest = max(len(line) for label in labels for line in label.split("\n"))
    ax.figure.subplots_adjust(left=min(0.42, 0.03 + longest * 0.0105), right=0.98, bottom=0.08)


PNG_TOO = False   # set from --png


def save(fig, out, folder, name, reports, top=0.9):
    """Writes <out>/<folder>/<name>.svg, plus .png when --png is given."""
    fig.text(0.01, 0.01, footnote(reports), fontsize=7.5, color=MUTED)
    if top is not None:
        fig.tight_layout(rect=(0, 0.04, 1, top))
    directory = os.path.join(out, folder)
    os.makedirs(directory, exist_ok=True)
    fig.savefig(os.path.join(directory, f"{name}.svg"))
    if PNG_TOO:
        fig.savefig(os.path.join(directory, f"{name}.png"), dpi=200)
    plt.close(fig)
    print(f"wrote {folder}/{name}.svg" + (" / .png" if PNG_TOO else ""))


def fmt_count(value):
    return f"{value:,.0f} objects" if value else "n/a"


def fmt_fps(value, aborted=False):
    if value is None:
        return "not measured"
    return f"{value:.1f} fps" + (" (aborted)" if aborted else "")


def metric_color(metric, device_index):
    family = METRIC_COLORS[metric]
    return family[device_index % len(family)]


# ---------------------------------------------------------------------------------------------------
# Charts
# ---------------------------------------------------------------------------------------------------
def chart_objects_at_30fps(reports, backends, out):
    legend = [(r["_label"], metric_color("objects", i)) for i, r in enumerate(reports)]
    rows = []
    for backend in backends:
        bars = []
        for i, report in enumerate(reports):
            row = count_at_target(report, backend)
            value = row["count"] if row else None
            bars.append((value, metric_color("objects", i), fmt_count(value), False))
        rows.append((backend, bars))
    fig, ax = new_figure(len(backends) * len(reports), "Objects rendered at a stable 30 fps", "object count · higher is better", legend)
    draw_bars(ax, rows)
    save(fig, out, "summary", "objects_at_30fps", reports, top=None)


def chart_fps_at_1m(reports, backends, out):
    legend = [(r["_label"], metric_color("fps", i)) for i, r in enumerate(reports)]
    rows = []
    for backend in backends:
        bars = []
        for i, report in enumerate(reports):
            row = sweep_row(report, backend, HEADLINE_COUNT)
            value = fps(row["frameMs"]["avg"]) if row else None
            aborted = row is not None and row["status"] == "aborted"
            bars.append((value, metric_color("fps", i), fmt_fps(value, aborted), aborted))
        rows.append((backend, bars))
    fig, ax = new_figure(len(backends) * len(reports), "Average fps at 1,000,000 objects", "frame rate · higher is better", legend)
    draw_bars(ax, rows)
    save(fig, out, "summary", "fps_at_1m", reports, top=None)


def chart_step_fps_at_1m(reports, backend, out):
    legend = [("Average fps", SERIES_COLORS["avg"]), ("1% low fps", SERIES_COLORS["low1"])]
    rows = []
    for report in reports:
        row = sweep_row(report, backend, HEADLINE_COUNT)
        aborted = row is not None and row["status"] == "aborted"
        bars = []
        for key in ("avg", "low1"):
            value = fps(row["frameMs"][key]) if row else None
            bars.append((value, SERIES_COLORS[key], fmt_fps(value, aborted), aborted))
        rows.append((device_label(report), bars))
    fig, ax = new_figure(len(reports) * 2, f"{backend}  ·  1,000,000 objects", "frame rate · higher is better", legend)
    draw_bars(ax, rows)
    save(fig, out, os.path.join("steps", step_slug(backend)), "fps_at_1m", reports, top=None)


def chart_step_objects_at_30fps(reports, backend, out):
    rows = []
    for i, report in enumerate(reports):
        row = count_at_target(report, backend)
        value = row["count"] if row else None
        detail = f'{fmt_count(value)}\navg {row["avgMs"]:.1f} ms · 1% low {row["low1Ms"]:.1f} ms' if row else "n/a"
        rows.append((device_label(report), [(value, metric_color("objects", i), detail, False)]))
    fig, ax = new_figure(len(reports), f"{backend}  ·  objects at a stable 30 fps", "object count · higher is better")
    draw_bars(ax, rows)
    save(fig, out, os.path.join("steps", step_slug(backend)), "objects_at_30fps", reports, top=None)


def chart_frametime_vs_count(reports, backends, out, folder="summary", title="Average frame time against object count", color_by_device=False):
    """Log-log frame time curves. Summary: one colour per step, line style per device. Per step: one colour per device."""
    fig, ax = plt.subplots(figsize=(13, 7))
    ax.set_title(title, fontsize=17, fontweight="bold", pad=26)
    ax.text(0.5, 1.02, "frame time · lower is better", transform=ax.transAxes, ha="center", fontsize=11, color=MUTED, style="italic")
    styles = ["-", "--", ":", "-."]
    for device_index, report in enumerate(reports):
        measured = report["sweep"] + report.get("search", [])
        for backend_index, backend in enumerate(backends):
            rows = sorted((r for r in measured if r["backend"] == backend and r["status"] == "ok"), key=lambda r: r["count"])
            if not rows:
                continue
            if color_by_device:
                label, color, style = report["_label"], DEVICE_COLORS[device_index % len(DEVICE_COLORS)], "-"
            else:
                label = backend if len(reports) == 1 else f'{backend} — {report["_label"]}'
                color, style = DEVICE_COLORS[backend_index % len(DEVICE_COLORS)], styles[device_index % len(styles)]
            ax.plot([r["count"] for r in rows], [r["frameMs"]["avg"] for r in rows], style,
                    marker="o", markersize=4, linewidth=2, label=label, color=color)
    ax.axhline(TARGET_MS, color=TEXT, linestyle=":", linewidth=1)
    ax.annotate("30 fps", xy=(0.01, TARGET_MS), xycoords=("axes fraction", "data"), xytext=(0, 4), textcoords="offset points", fontsize=10, color=MUTED)
    ax.set_xscale("log")
    ax.set_yscale("log")
    ax.grid(True, which="major", color=GRID, linewidth=0.8)
    ax.set_xlabel("objects")
    ax.set_ylabel("average frame time, ms")
    ax.xaxis.set_major_formatter(matplotlib.ticker.FuncFormatter(lambda v, _: f"{v:,.0f}"))
    ax.xaxis.set_minor_formatter(matplotlib.ticker.NullFormatter())
    ax.yaxis.set_major_locator(matplotlib.ticker.LogLocator(base=10, subs=(1.0, 2.0, 5.0)))
    ax.yaxis.set_major_formatter(matplotlib.ticker.FuncFormatter(lambda v, _: f"{v:g}"))
    ax.yaxis.set_minor_formatter(matplotlib.ticker.NullFormatter())
    for spine in ax.spines.values():
        spine.set_color(GRID)
    ax.legend(fontsize=9, frameon=False)
    save(fig, out, folder, "frametime_vs_count", reports, top=1.0)


# ---------------------------------------------------------------------------------------------------
def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("reports", nargs="+", help="benchmark JSON files, one per device")
    parser.add_argument("--out", default="charts", help="output directory")
    parser.add_argument("--labels", default="", help="comma-separated device names in file order, e.g. 'Blade 18,ROG Strix,Steam Deck'")
    parser.add_argument("--png", action="store_true", help="also write 2x PNGs (Google Slides does not import SVG)")
    args = parser.parse_args()
    global PNG_TOO
    PNG_TOO = args.png
    os.makedirs(args.out, exist_ok=True)
    labels = [label.strip() for label in args.labels.split(",") if label.strip()]
    reports = load_reports(args.reports, labels)
    backends = backend_order(reports)
    chart_objects_at_30fps(reports, backends, args.out)
    chart_fps_at_1m(reports, backends, args.out)
    for backend in backends:
        chart_step_fps_at_1m(reports, backend, args.out)
        chart_step_objects_at_30fps(reports, backend, args.out)
        chart_frametime_vs_count(reports, [backend], args.out, os.path.join("steps", step_slug(backend)), f"{backend}  ·  frame time against object count", color_by_device=True)
    chart_frametime_vs_count(reports, backends, args.out)


if __name__ == "__main__":
    import matplotlib.ticker
    main()
