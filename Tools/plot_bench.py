#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["matplotlib"]
# ///
"""Turn benchmark JSON reports into slide-ready charts.

Usage: plot_bench.py [--out charts] [--labels "Blade 18,ROG Strix,Steam Deck"] [--png] report1.json report2.json ...

Reports are grouped by device (one report per device; --labels renames them in file order).
Written as SVG (add --png for 2x PNGs too, e.g. for Google Slides, which does not import SVG). Dark
background, horizontal bars, values at the bar ends:

  summary/objects_at_30fps        largest count that held a stable 30 fps, per step, one bar per device
  summary/fps_at_1m               average fps at one million objects, per step, one bar per device
  summary/frametime_vs_count      average frame time against object count, one line per step per device
  summary/draw_calls_at_1m        fewest and most draw calls in a frame at one million objects, per step, log scale
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
SERIES_COLORS = {"avg": "#2196f3", "low1": "#f57c00"}   # per-step avg vs 1% low bars; metric, not device
# Devices keep one colour everywhere, matching the "three machines" slide: teal high-end, grey-teal
# mid-range, coral for the Steam Deck (the optimisation target, so the warm one).
DEVICE_COLORS = ["#2ba89b", "#9fc4bf", "#e0665c", "#66bb6a", "#ab47bc", "#ffb300"]
STEP_COLORS = ["#2196f3", "#f57c00", "#ffb300", "#66bb6a", "#ab47bc", "#26c6da"]   # summary frame time curves, one per step

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
        report["_label"] = labels[index] if index < len(labels) else device_name(report, index)
        reports.append(report)
    return reports


def device_name(report, index):
    """Device name from the report; reports written before the runner labelled the Deck say "PC" and are recognised by their APU."""
    device = report["device"]
    if "Custom APU 0405" in device.get("cpu", ""):
        return "Steam Deck"
    return device.get("model", f"device {index + 1}")


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


def draw_call_stats(row):
    """Draw call range of a sweep row: {min, avg, max} in reports from 16 Sept 2026 on, a single last-frame value before."""
    value = row.get("drawCalls") if row else None
    if value is None:
        return None
    if isinstance(value, dict):
        return value if value.get("max", -1) >= 0 else None
    return {"min": value, "avg": value, "max": value} if value >= 0 else None


def footnote(reports):
    """One line per device so three long GPU names never overrun the figure; the first line states the resolution caveat."""
    parts = [f'{r["_label"]}: {r["device"].get("gpu", "?")}, {r["device"].get("resolution", "?")}, build {r["device"].get("gitHash", "?")}' for r in reports]
    return "\n".join(["Resolution is native to each target, not equalised."] + parts)


# ---------------------------------------------------------------------------------------------------
# Drawing primitives
# ---------------------------------------------------------------------------------------------------
def new_figure(rows, title, direction, legend=None, row_height=0.42):
    """Dark figure sized to the number of bar rows: bold title, 'higher/lower is better' line, dot legend.
    Per-step charts pass a larger row_height so a few bars still fill a slide and read from the back row."""
    header = (1.1 if legend else 0.2) if BARE else 2.1   # inches above the bars: title + direction + legend, or just the legend
    height = max(3.0 if BARE else 3.8, header + rows * row_height)
    fig, ax = plt.subplots(figsize=(10 if BARE and rows <= 6 else 13, height))   # per-step charts narrower so they fill more of the slide
    inch = 1.0 / height   # figure fraction per inch, so the header stacks the same on every chart height
    legend_y = 1.0 - 0.15 * inch
    if not BARE:
        fig.suptitle(title, y=1.0 - 0.15 * inch, va="top", fontsize=17, fontweight="bold", color=TEXT)
        fig.text(0.5, 1.0 - 0.55 * inch, direction, ha="center", va="top", fontsize=11, color=MUTED, style="italic")
        legend_y = 1.0 - 0.8 * inch
    if legend:
        handles = [plt.Line2D([], [], marker="o", linestyle="", markersize=fs(10), color=color, label=label) for label, color in legend]
        fig.legend(handles=handles, loc="upper center", bbox_to_anchor=(0.5, legend_y), ncol=len(legend), frameon=False, fontsize=fs(12))
    fig.subplots_adjust(top=1.0 - (header - 0.1) * inch)
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
                ax.text((value or 0) + max_value * 0.008, y, text, va="center", ha="left", fontsize=fs(11), fontweight="bold", color=TEXT)
            y += 1.0
        ticks.append((group_top + y - 1.0) / 2)
        labels.append(group_label)
        y += gap
    ax.set_yticks(ticks)
    ax.set_yticklabels(labels, fontsize=fs(12), fontweight="bold")
    ax.set_ylim(y - gap - 0.5, -0.5)
    scale = fs(12) / 12   # text takes proportionally more room when the type is scaled up
    longest = max(len(line) for label in labels for line in label.split("\n"))
    left = min(0.55, 0.03 + longest * 0.0105 * scale)
    ax.figure.subplots_adjust(left=left, right=0.98, bottom=0.08)
    longest_text = max((len(line) for _, bars in rows for bar in bars if bar[2] for line in bar[2].split("\n")), default=8)
    axis_fraction = (0.98 - left) / 0.6   # the value margin is a share of the axis width, which the label column eats into
    ax.set_xlim(0, max_value * (1.04 + 0.0135 * longest_text * scale / axis_fraction))


def draw_log_bars(ax, rows):
    """Like draw_bars on a log x axis with power-of-ten grid lines, for values spanning a million down to one."""
    bar_height = 0.8
    gap = 0.6
    floor = 0.7   # bars start just below 1 so a single draw call is still a visible sliver
    y = 0.0
    ticks, labels = [], []
    max_value = max((bar[0] or 1) for _, bars in rows for bar in bars)
    for group_label, bars in rows:
        group_top = y
        for value, color, text, hatched in bars:
            value = max(value or 1, 1)
            ax.barh(y, value - floor, left=floor, height=bar_height, color=color, edgecolor=BACKGROUND)
            if text:
                ax.text(value * 1.12, y, text, va="center", ha="left", fontsize=fs(11), fontweight="bold", color=TEXT)
            y += 1.0
        ticks.append((group_top + y - 1.0) / 2)
        labels.append(group_label)
        y += gap
    ax.set_yticks(ticks)
    ax.set_yticklabels(labels, fontsize=fs(12), fontweight="bold")
    ax.set_ylim(y - gap - 0.5, -0.5)
    scale = fs(12) / 12
    longest = max(len(line) for label in labels for line in label.split("\n"))
    left = min(0.55, 0.03 + longest * 0.0105 * scale)
    ax.figure.subplots_adjust(left=left, right=0.98, bottom=0.1)
    longest_text = max((len(bar[2]) for _, bars in rows for bar in bars if bar[2]), default=8)
    axis_fraction = (0.98 - left) / 0.6
    label_share = min(0.5, 0.019 * longest_text * scale / axis_fraction)   # axis fraction the longest value label needs; bold digits run wide
    import math
    decades = math.log10(max_value / floor)
    ax.set_xscale("log")
    ax.set_xlim(floor, max_value * 10 ** (decades * label_share / (1 - label_share) + 0.05))
    top = int(math.floor(math.log10(max_value)))
    powers = [10 ** k for k in range(0, top + 1, 2 if top >= 5 else 1)]   # up to the last power of ten at or below the largest bar; every other decade when there are many
    ax.set_xticks(powers)
    ax.set_xticklabels([f"{p:,.0f}".replace(",000,000", "M").replace(",000", "k") for p in powers], fontsize=fs(10), color=MUTED)
    ax.xaxis.set_minor_locator(matplotlib.ticker.NullLocator())
    ax.grid(True, axis="x", which="major", color=GRID, linewidth=0.8)
    ax.set_axisbelow(True)
    ax.set_autoscalex_on(False)


PNG_TOO = False   # set from --png
BARE = False      # set from --bare: no title, direction line or footnote; the slide carries those
BARE_SCALE = 1.7  # bare charts sit inside a slide at roughly 60% width, so type is scaled up to stay legible from the back row


def fs(size):
    """Font size in points, scaled up in bare mode."""
    return size * BARE_SCALE if BARE else size


def save(fig, out, folder, name, reports, top=0.9):
    """Writes <out>/<folder>/<name>.svg, plus .png when --png is given."""
    footer = 0.02
    if not BARE:
        note = footnote(reports)
        fig.text(0.01, 0.01, note, fontsize=7.5, color=MUTED, va="bottom")
        footer = (0.1 + 0.135 * note.count("\n") + 0.135) / fig.get_figheight()   # figure fraction the footnote lines occupy
    if top is not None:
        fig.tight_layout(rect=(0, footer, 1, top))
    else:
        fig.subplots_adjust(bottom=max(fig.subplotpars.bottom, footer + 0.02))
    directory = os.path.join(out, folder)
    os.makedirs(directory, exist_ok=True)
    fig.savefig(os.path.join(directory, f"{name}.svg"))
    if PNG_TOO:
        fig.savefig(os.path.join(directory, f"{name}.png"), dpi=200)
    plt.close(fig)
    print(f"wrote {folder}/{name}.svg" + (" / .png" if PNG_TOO else ""))


def fmt_count(value):
    return f"{value:,.0f}" if value else "n/a"


def fmt_fps(value):
    return f"{value:.1f}" if value is not None else "not measured"


def device_color(device_index):
    return DEVICE_COLORS[device_index % len(DEVICE_COLORS)]


# ---------------------------------------------------------------------------------------------------
# Charts
# ---------------------------------------------------------------------------------------------------
def chart_objects_at_30fps(reports, backends, out):
    legend = [(r["_label"], device_color(i)) for i, r in enumerate(reports)]
    rows = []
    for backend in backends:
        bars = []
        for i, report in enumerate(reports):
            row = count_at_target(report, backend)
            value = row["count"] if row else None
            bars.append((value, device_color(i), fmt_count(value), False))
        rows.append((backend, bars))
    fig, ax = new_figure(len(backends) * len(reports), "Objects rendered at a stable 30 FPS", "object count · higher is better", legend)
    draw_bars(ax, rows)
    save(fig, out, "summary", "objects_at_30fps", reports, top=None)


def chart_fps_at_1m(reports, backends, out):
    legend = [(r["_label"], device_color(i)) for i, r in enumerate(reports)]
    rows = []
    for backend in backends:
        bars = []
        for i, report in enumerate(reports):
            row = sweep_row(report, backend, HEADLINE_COUNT)
            value = fps(row["frameMs"]["avg"]) if row else None
            bars.append((value, device_color(i), fmt_fps(value), False))
        rows.append((backend, bars))
    fig, ax = new_figure(len(backends) * len(reports), "Average FPS at 1,000,000 objects", "FPS · higher is better", legend)
    draw_bars(ax, rows)
    save(fig, out, "summary", "fps_at_1m", reports, top=None)


def chart_draw_calls_at_1m(reports, backends, out, source=None):
    """Fewest and most draw calls in one frame at one million objects. Draw calls depend on the technique, not
    the hardware, so one report supplies the numbers: --draw-calls-from, else the first report that measured a step."""
    sources = [source] if source else reports
    per_step = {b: next((s for s in (draw_call_stats(sweep_row(r, b, HEADLINE_COUNT)) for r in sources) if s), None) for b in backends}
    ranged = any(s and s["max"] - s["min"] > max(2, 0.02 * s["min"]) for s in per_step.values())   # in the player the count is stable to ±1; one bar then
    rows = []
    for backend in backends:
        stats = per_step[backend]
        if ranged:
            bars = [(stats[key] if stats else None, color, fmt_count(stats[key] if stats else None), False)
                    for key, color in (("min", SERIES_COLORS["avg"]), ("max", SERIES_COLORS["low1"]))]
        else:
            value = round(stats["avg"]) if stats else None
            bars = [(value, DEVICE_COLORS[2], fmt_count(value), False)]
        rows.append((backend, bars))
    legend = [("fewest in a frame", SERIES_COLORS["avg"]), ("most in a frame", SERIES_COLORS["low1"])] if ranged else None
    fig, ax = new_figure(len(rows[0][1]) * len(backends), "Draw calls at 1,000,000 objects", "draw calls per frame · lower is better", legend, row_height=0.75 if not ranged else 0.42)
    draw_log_bars(ax, rows)
    save(fig, out, "summary", "draw_calls_at_1m", reports, top=None)


def chart_step_fps_at_1m(reports, backend, out):
    legend = [("Average FPS", SERIES_COLORS["avg"]), ("1% low FPS", SERIES_COLORS["low1"])]
    rows = []
    for report in reports:
        row = sweep_row(report, backend, HEADLINE_COUNT)
        bars = []
        for key in ("avg", "low1"):
            value = fps(row["frameMs"][key]) if row else None
            bars.append((value, SERIES_COLORS[key], fmt_fps(value), False))
        rows.append((device_label(report), bars))
    fig, ax = new_figure(len(reports) * 2, f"{backend}  ·  1,000,000 objects", "FPS · higher is better", legend, row_height=0.75)
    draw_bars(ax, rows)
    save(fig, out, os.path.join("steps", step_slug(backend)), "fps_at_1m", reports, top=None)


def chart_step_objects_at_30fps(reports, backend, out):
    rows = []
    for i, report in enumerate(reports):
        row = count_at_target(report, backend)
        value = row["count"] if row else None
        rows.append((device_label(report), [(value, device_color(i), fmt_count(value), False)]))
    fig, ax = new_figure(len(reports), f"{backend}  ·  objects at a stable 30 FPS", "object count · higher is better", row_height=1.3)
    draw_bars(ax, rows)
    save(fig, out, os.path.join("steps", step_slug(backend)), "objects_at_30fps", reports, top=None)


def chart_frametime_vs_count(reports, backends, out, folder="summary", title="Average frame time against object count", color_by_device=False):
    """Log-log frame time curves. Summary: one colour per step, line style per device. Per step: one colour per device."""
    fig, ax = plt.subplots(figsize=(13, 6.5 if BARE else 7))
    if not BARE:
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
                label, color, style = report["_label"], device_color(device_index), "-"
            else:
                label = backend if len(reports) == 1 else f'{backend} — {report["_label"]}'
                color, style = STEP_COLORS[backend_index % len(STEP_COLORS)], styles[device_index % len(styles)]
            ax.plot([r["count"] for r in rows], [r["frameMs"]["avg"] for r in rows], style,
                    marker="o", markersize=4, linewidth=2, label=label, color=color)
    ax.axhline(TARGET_MS, color=TEXT, linestyle=":", linewidth=1)
    ax.annotate("30 FPS", xy=(0.01, TARGET_MS), xycoords=("axes fraction", "data"), xytext=(0, 4), textcoords="offset points", fontsize=fs(10), color=MUTED)
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
    if color_by_device or len(reports) == 1:
        ax.legend(fontsize=fs(9), frameon=False)
    else:
        summary_legends(ax, reports, backends, styles)
    save(fig, out, folder, "frametime_vs_count", reports, top=1.0)


def summary_legends(ax, reports, backends, styles):
    """Two compact legends instead of one per line: steps by colour (upper left), devices by line style (lower right)."""
    step_handles = [plt.Line2D([], [], color=STEP_COLORS[i % len(STEP_COLORS)], linewidth=3, label=backend) for i, backend in enumerate(backends)]
    device_handles = [plt.Line2D([], [], color=TEXT, linestyle=styles[i % len(styles)], linewidth=2, label=report["_label"]) for i, report in enumerate(reports)]
    steps = ax.legend(handles=step_handles, loc="upper left", fontsize=fs(9), frameon=False)
    ax.add_artist(steps)
    ax.legend(handles=device_handles, loc="lower right", fontsize=fs(9), frameon=False)


# ---------------------------------------------------------------------------------------------------
def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("reports", nargs="+", help="benchmark JSON files, one per device")
    parser.add_argument("--out", default="charts", help="output directory")
    parser.add_argument("--labels", default="", help="comma-separated device names in file order, e.g. 'Blade 18,ROG Strix,Steam Deck'")
    parser.add_argument("--png", action="store_true", help="also write 2x PNGs (Google Slides does not import SVG)")
    parser.add_argument("--bare", action="store_true", help="bars, labels and legend only: no title, direction line or footnote (the slide carries them)")
    parser.add_argument("--draw-calls-from", default=None, help="report whose per-frame draw call counts feed summary/draw_calls_at_1m (the count is technique-bound, so any device will do)")
    args = parser.parse_args()
    global PNG_TOO, BARE
    PNG_TOO = args.png
    BARE = args.bare
    if BARE:
        plt.rcParams.update({"font.size": fs(11), "axes.labelsize": fs(11), "xtick.labelsize": fs(10), "ytick.labelsize": fs(10)})
    os.makedirs(args.out, exist_ok=True)
    labels = [label.strip() for label in args.labels.split(",") if label.strip()]
    reports = load_reports(args.reports, labels)
    backends = backend_order(reports)
    chart_objects_at_30fps(reports, backends, args.out)
    chart_fps_at_1m(reports, backends, args.out)
    draw_call_source = load_reports([args.draw_calls_from], [])[0] if args.draw_calls_from else None
    chart_draw_calls_at_1m(reports, backends, args.out, draw_call_source)
    for backend in backends:
        chart_step_fps_at_1m(reports, backend, args.out)
        chart_step_objects_at_30fps(reports, backend, args.out)
        chart_frametime_vs_count(reports, [backend], args.out, os.path.join("steps", step_slug(backend)), f"{backend}  ·  frame time against object count", color_by_device=True)
    chart_frametime_vs_count(reports, backends, args.out)


if __name__ == "__main__":
    import matplotlib.ticker
    main()
