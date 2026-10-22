// Host Stats ("Task Manager") charts.
//
// Highcharts itself is loaded by Shared/Highcharts.razor.js (LoadHighchartsResources). This module creates
// the charts once and then moves them along with series.addPoint(point, redraw, shift), so nothing is
// re-rendered from scratch every second. Charts use Highcharts styled mode; colours come from the CSS in
// FreeCICD.App.UI.HostStats.razor so they follow the FreeCRM light and dark themes.

const WINDOW_MS = 60000;

let dotNet = null;
let devices = [];
let buffers = {};          // key -> [[t, v], ...] covering the last 60 s
let lastT = 0;
let mode = "live";         // "live" or "history"
let selected = null;       // selected device
let cpuView = "overall";   // "overall" or "cores"
let mainChart = null;
let coreCharts = [];
let sparklines = {};       // device id -> chart
let hidden = false;
let visibilityHandler = null;
let railHandler = null;
let coreLabelsAt = 0;

export function init(helper) {
    dotNet = helper;
    hidden = document.hidden;
    if (visibilityHandler == null) {
        visibilityHandler = () => {
            hidden = document.hidden;
            if (dotNet != null) {
                dotNet.invokeMethodAsync("OnVisibilityChanged", !hidden);
            }
        };
        document.addEventListener("visibilitychange", visibilityHandler);
    }
    return !hidden;
}

export function dispose() {
    if (visibilityHandler != null) {
        document.removeEventListener("visibilitychange", visibilityHandler);
        visibilityHandler = null;
    }
    if (railHandler != null) {
        document.removeEventListener("keydown", railHandler);
        railHandler = null;
    }
    destroyMain();
    destroySparklines();
    dotNet = null;
    buffers = {};
}

/// Keyboard support for the device rail (toolbar pattern): arrow keys move focus between tiles,
/// Home / End jump to the first / last, Enter or Space (native button) selects. One tab stop.
export function initRail() {
    if (railHandler != null) {
        return;
    }
    railHandler = (e) => {
        const tile = e.target && e.target.closest ? e.target.closest(".hs-rail .hs-tile") : null;
        if (!tile) {
            return;
        }
        const tiles = Array.from(tile.parentElement.querySelectorAll(".hs-tile"));
        let index = tiles.indexOf(tile);
        switch (e.key) {
            case "ArrowDown":
            case "ArrowRight":
                index = (index + 1) % tiles.length;
                break;
            case "ArrowUp":
            case "ArrowLeft":
                index = (index - 1 + tiles.length) % tiles.length;
                break;
            case "Home":
                index = 0;
                break;
            case "End":
                index = tiles.length - 1;
                break;
            default:
                return;
        }
        e.preventDefault();
        tiles.forEach(t => t.setAttribute("tabindex", "-1"));
        tiles[index].setAttribute("tabindex", "0");
        tiles[index].focus();
    };
    document.addEventListener("keydown", railHandler);
}

/// Highcharts marks the element it renders into as role="img" with an empty label. Charts are therefore
/// rendered into an inner element that is hidden from assistive technology; the accessible name lives on
/// the outer element (or the tile text) that this module controls.
function hideChartHost(host) {
    host.setAttribute("aria-hidden", "true");
    host.removeAttribute("role");
    host.removeAttribute("aria-label");
}

function innerHost(outer, className) {
    outer.innerHTML = "";
    const inner = document.createElement("div");
    inner.className = className;
    inner.style.width = "100%";
    inner.style.height = "100%";
    outer.appendChild(inner);
    return inner;
}

function hasArearange() {
    return typeof (Highcharts) == "object" && Highcharts.seriesTypes && Highcharts.seriesTypes.arearange != undefined;
}

/// Sets the device list (tiles) and (re)creates their sparklines.
export function setDevices(list) {
    devices = list || [];
    destroySparklines();
    for (const device of devices) {
        createSparkline(device);
    }
    if (selected != null) {
        const again = devices.find(d => d.id == selected.id);
        if (again) {
            selected = again;
        }
    }
}

/// Fills the 60 s buffers from samples ([{t, values}]) loaded from the Live endpoint.
export function preload(samples) {
    for (const sample of samples || []) {
        append(sample.t, sample.values);
    }
    refreshAll();
}

/// Adds one sample to every buffer and moves the charts along.
export function push(t, values) {
    if (t <= lastT) {
        return;
    }
    append(t, values);
    if (hidden) {
        return;
    }

    for (const device of devices) {
        const chart = sparklines[device.id];
        if (chart) {
            addToChart(chart, device.keys.slice(0, sparkKeyCount(device)), t, values);
        }
    }

    if (mode == "live" && selected != null) {
        if (mainChart) {
            addToChart(mainChart, selected.keys, t, values);
            updateScaleLabel();
        }
        coreCharts.forEach((chart, i) => addToChart(chart, ["core" + i], t, values));
        if (coreCharts.length) {
            updateCoreLabels();
        }
    }
}

export function showLive(device, view) {
    mode = "live";
    selected = device;
    cpuView = view || "overall";
    destroyMain();

    const container = document.getElementById("hs-main-chart");
    const coresContainer = document.getElementById("hs-cores");
    if (!container || !device) {
        return;
    }

    if (device.kind == "cpu" && cpuView == "cores" && coresContainer) {
        buildCoreGrid(coresContainer, device);
    } else {
        mainChart = Highcharts.chart(container, liveOptions(device));
        updateScaleLabel();
    }
}

function liveOptions(device) {
    const now = lastT || Date.now();
    return {
        chart: {
            styledMode: true,
            animation: false,
            className: "hs-chart hs-kind-" + device.kind,
            spacing: [2, 2, 2, 2],
            reflow: true,
        },
        title: { text: null },
        credits: { enabled: false },
        exporting: { enabled: false },
        legend: { enabled: device.keys.length > 1, align: "right", verticalAlign: "top", floating: true, y: -2 },
        accessibility: {
            description: device.title + ", last 60 seconds. The values are listed in the table under the chart.",
        },
        xAxis: {
            type: "datetime",
            min: now - WINDOW_MS,
            max: now,
            tickInterval: 6000,
            labels: { enabled: false },
            tickLength: 0,
            gridLineWidth: 1,
            lineWidth: 0,
            crosshair: true,
        },
        yAxis: {
            min: 0,
            max: device.percent ? 100 : null,
            softMax: device.percent ? 100 : 1,
            tickInterval: device.percent ? 10 : undefined,
            tickAmount: device.percent ? undefined : 11,
            title: { text: null },
            labels: { enabled: false },
            gridLineWidth: 1,
            endOnTick: !device.percent,
        },
        tooltip: {
            animation: false,
            shared: true,
            xDateFormat: "%H:%M:%S",
            valueDecimals: device.percent ? 1 : 0,
            formatter: function () {
                return tooltipText(this, device);
            },
        },
        plotOptions: {
            series: {
                animation: false,
                marker: { enabled: false, states: { hover: { enabled: true } } },
                states: { hover: { lineWidthPlus: 0 } },
                connectNulls: false,
            },
        },
        series: device.keys.map((key, i) => ({
            type: device.keys.length > 1 && i > 0 ? "line" : "area",
            name: device.seriesNames[i] || device.title,
            className: "hs-series-" + i,
            data: (buffers[key] || []).slice(),
        })),
    };
}

function buildCoreGrid(container, device) {
    container.innerHTML = "";
    const cores = device.cores || 0;
    const columns = cores <= 4 ? 2 : cores <= 9 ? 3 : cores <= 16 ? 4 : cores <= 36 ? 6 : 8;
    container.style.gridTemplateColumns = "repeat(" + columns + ", minmax(0, 1fr))";
    for (let i = 0; i < cores; i++) {
        const cell = document.createElement("div");
        cell.className = "hs-core-cell";
        cell.setAttribute("role", "img");
        cell.setAttribute("aria-label", "CPU " + i + ", last 60 seconds");
        const label = document.createElement("div");
        label.className = "hs-core-label";
        label.textContent = "CPU " + i;
        const host = document.createElement("div");
        host.className = "hs-core-chart";
        cell.appendChild(label);
        cell.appendChild(host);
        container.appendChild(cell);
        coreCharts.push(Highcharts.chart(host, sparkOptions({ kind: "cpu", percent: true, title: "CPU " + i }, ["core" + i], true)));
        hideChartHost(host);
    }
    coreLabelsAt = 0;
    updateCoreLabels();
}

/// Accessible names for the per-core charts, refreshed at most every 5 seconds.
function updateCoreLabels() {
    const now = Date.now();
    if (now - coreLabelsAt < 5000) {
        return;
    }
    coreLabelsAt = now;
    document.querySelectorAll("#hs-cores .hs-core-cell").forEach((cell, i) => {
        const buffer = buffers["core" + i] || [];
        const last = buffer.length ? buffer[buffer.length - 1][1] : null;
        cell.setAttribute("aria-label", "CPU " + i + ", last 60 seconds, now " + formatValue(last, "%"));
    });
}

function updateScaleLabel() {
    const label = document.getElementById("hs-scale-max");
    if (!label || !selected) {
        return;
    }
    if (selected.percent) {
        label.textContent = "100%";
    } else if (mainChart && mainChart.yAxis && mainChart.yAxis[0]) {
        label.textContent = formatValue(mainChart.yAxis[0].max || 0, selected.unit);
    }
}

/// Shows history: per series an arearange min/max band plus an avg line, boot markers as plot lines.
export function showHistory(device, history) {
    mode = "history";
    selected = device;
    destroyMain();

    const container = document.getElementById("hs-main-chart");
    if (!container || !history) {
        return;
    }

    const bucketMs = (history.bucketSeconds || 60) * 1000;
    const series = [];
    const banded = hasArearange();
    const wanted = device.historyKeys && device.historyKeys.length ? device.historyKeys : [];
    const all = history.series || [];
    const list = wanted.length == 0 ? all : wanted.map(k => all.find(s => s.key == k)).filter(s => s);

    list.forEach((s, i) => {
        const scale = device.historyScale || 1;
        const avg = withGaps(s.avg.map(p => [p[0], p[1] * scale]), bucketMs);
        const band = withGaps(s.band.map(p => [p[0], p[1] * scale, p[2] * scale]), bucketMs);
        if (banded) {
            series.push({
                type: "arearange",
                name: (device.seriesNames[i] || s.name) + " min / max",
                className: "hs-band hs-series-" + i,
                data: band,
                linkedTo: ":next",
                enableMouseTracking: false,
            });
        }
        series.push({
            type: "line",
            name: (device.seriesNames[i] || s.name) + " average",
            className: "hs-avg hs-series-" + i,
            data: avg,
            minPointLength: 0,
        });
        if (!banded) {
            // Fallback when highcharts-more is missing: show the max as a dashed line.
            series.push({
                type: "line",
                name: (device.seriesNames[i] || s.name) + " max",
                className: "hs-max hs-series-" + i,
                data: band.map(p => [p[0], p[2] == null ? null : p[2]]),
            });
        }
    });

    const plotLines = (history.bootMarkers || []).map(b => ({
        value: b,
        className: "hs-boot-line",
        zIndex: 5,
        label: { text: "Reboot", rotation: 0, y: 12, x: 4, className: "hs-boot-label" },
    }));

    if (history.syntheticBefore && history.containsSyntheticData) {
        plotLines.push({
            value: history.syntheticBefore,
            className: "hs-synthetic-line",
            zIndex: 4,
            label: { text: "← synthetic demo data", rotation: 0, align: "right", x: -4, y: 28, className: "hs-synthetic-label" },
        });
    }

    mainChart = Highcharts.chart(container, {
        chart: {
            styledMode: true,
            animation: false,
            className: "hs-chart hs-history hs-kind-" + device.kind,
            zooming: { type: "x" },
            spacing: [8, 4, 4, 4],
        },
        title: { text: null },
        credits: { enabled: false },
        exporting: { enabled: false },
        legend: { enabled: true, align: "right", verticalAlign: "top", floating: false },
        accessibility: {
            description: device.title + " history from " + new Date(history.from).toLocaleString() + " to " + new Date(history.to).toLocaleString()
                + ", " + history.resolution + " resolution. Averages and peaks are listed in the table under the chart.",
        },
        time: { useUTC: false },
        xAxis: {
            type: "datetime",
            min: history.from,
            max: history.to,
            plotLines: plotLines,
            crosshair: true,
            gridLineWidth: 1,
        },
        yAxis: {
            min: 0,
            max: device.percent ? 100 : null,
            title: { text: null },
            labels: {
                formatter: function () {
                    return formatValue(this.value, device.unit, true);
                },
            },
            gridLineWidth: 1,
        },
        tooltip: {
            animation: false,
            shared: true,
            xDateFormat: "%a %e %b %Y, %H:%M",
            formatter: function () {
                return tooltipText(this, device);
            },
        },
        plotOptions: {
            series: {
                animation: false,
                marker: { enabled: false, states: { hover: { enabled: true } } },
                states: { hover: { lineWidthPlus: 0 } },
            },
        },
        series: series,
    });

    const label = document.getElementById("hs-scale-max");
    if (label) {
        label.textContent = "";
    }
}

/// Inserts a null between points further apart than three buckets so outages show as gaps.
function withGaps(points, bucketMs) {
    const output = [];
    for (let i = 0; i < points.length; i++) {
        if (i > 0 && points[i][0] - points[i - 1][0] > bucketMs * 3) {
            const gap = [points[i - 1][0] + bucketMs, null];
            if (points[i].length == 3) {
                gap.push(null);
            }
            output.push(gap);
        }
        output.push(points[i]);
    }
    return output;
}

function sparkKeyCount(device) {
    return device.kind == "net" ? 2 : 1;
}

function createSparkline(device) {
    const el = document.getElementById("hs-spark-" + device.id);
    if (!el) {
        return;
    }
    const host = innerHost(el, "hs-spark-inner");
    const options = sparkOptions(device, device.keys.slice(0, sparkKeyCount(device)), false);
    // The inner host's percentage height is not always resolved when the chart is created; size it from the tile.
    options.chart.height = el.clientHeight || 42;
    sparklines[device.id] = Highcharts.chart(host, options);
    hideChartHost(host);
    el.setAttribute("aria-hidden", "true");
}

function sparkOptions(device, keys, withTooltip) {
    const now = lastT || Date.now();
    return {
        chart: {
            styledMode: true,
            animation: false,
            // The tile host div carries .hs-spark (its fixed size); the chart itself gets its own class.
            className: (withTooltip ? "hs-chart hs-mini hs-kind-" : "hs-chart hs-sparkline hs-kind-") + device.kind,
            spacing: [1, 1, 1, 1],
            margin: [1, 1, 1, 1],
        },
        title: { text: null },
        credits: { enabled: false },
        exporting: { enabled: false },
        legend: { enabled: false },
        accessibility: { enabled: false },
        tooltip: withTooltip ? {
            animation: false,
            xDateFormat: "%H:%M:%S",
            formatter: function () {
                return tooltipText(this, device);
            },
        } : { enabled: false },
        xAxis: { type: "datetime", visible: true, min: now - WINDOW_MS, max: now, labels: { enabled: false }, tickInterval: withTooltip ? 12000 : 30000, gridLineWidth: withTooltip ? 1 : 0, lineWidth: 0, tickLength: 0 },
        yAxis: { visible: true, min: 0, max: device.percent ? 100 : null, softMax: device.percent ? 100 : 1, title: { text: null }, labels: { enabled: false }, gridLineWidth: withTooltip ? 1 : 0, tickAmount: withTooltip ? 5 : undefined },
        plotOptions: {
            series: {
                animation: false,
                marker: { enabled: false },
                enableMouseTracking: withTooltip,
                states: { hover: { enabled: withTooltip, lineWidthPlus: 0 } },
            },
        },
        series: keys.map((key, i) => ({
            type: i == 0 ? "area" : "line",
            className: "hs-series-" + i,
            data: (buffers[key] || []).slice(),
        })),
    };
}

function append(t, values) {
    lastT = Math.max(lastT, t);
    for (const key in values) {
        let buffer = buffers[key];
        if (!buffer) {
            buffer = [];
            buffers[key] = buffer;
        }
        buffer.push([t, values[key]]);
        while (buffer.length > 0 && buffer[0][0] < t - WINDOW_MS - 1000) {
            buffer.shift();
        }
    }
}

/// Adds the newest point of each key to the chart's series, shifting out points older than 60 s,
/// and slides the fixed 60 s window. One redraw per chart.
function addToChart(chart, keys, t, values) {
    if (!chart || !chart.series) {
        return;
    }
    const axis = chart.xAxis[0];
    axis.setExtremes(t - WINDOW_MS, t, false, false);
    keys.forEach((key, i) => {
        const series = chart.series[i];
        if (!series) {
            return;
        }
        const value = values[key] === undefined ? null : values[key];
        const first = series.xData && series.xData.length ? series.xData[0] : t;
        const shift = first < t - WINDOW_MS - 1000;
        const last = i == keys.length - 1;
        series.addPoint([t, value], last, shift, false);
    });
}

/// Rebuilds every chart's data from the buffers (after a preload or a backfill).
export function refreshAll() {
    if (hidden || typeof (Highcharts) != "object") {
        return;
    }
    for (const device of devices) {
        const chart = sparklines[device.id];
        if (chart) {
            setChartData(chart, device.keys.slice(0, sparkKeyCount(device)));
        }
    }
    if (mode == "live" && selected) {
        if (mainChart) {
            setChartData(mainChart, selected.keys);
            updateScaleLabel();
        }
        coreCharts.forEach((chart, i) => setChartData(chart, ["core" + i]));
    }
}

function setChartData(chart, keys) {
    const now = lastT || Date.now();
    chart.xAxis[0].setExtremes(now - WINDOW_MS, now, false, false);
    keys.forEach((key, i) => {
        if (chart.series[i]) {
            chart.series[i].setData((buffers[key] || []).slice(), false, false, false);
        }
    });
    chart.redraw(false);
}

function destroyMain() {
    if (mainChart) {
        mainChart.destroy();
        mainChart = null;
    }
    coreCharts.forEach(c => c.destroy());
    coreCharts = [];
    const coresContainer = document.getElementById("hs-cores");
    if (coresContainer) {
        coresContainer.innerHTML = "";
    }
}

function destroySparklines() {
    for (const id in sparklines) {
        try {
            sparklines[id].destroy();
        } catch { }
    }
    sparklines = {};
}

function tooltipText(context, device) {
    const points = context.points || [context.point];
    const time = Highcharts.dateFormat(mode == "history" ? "%a %e %b %Y, %H:%M" : "%H:%M:%S", context.x);
    let html = "<span class='hs-tip-time'>" + time + "</span>";
    for (const p of points) {
        if (!p || !p.series) {
            continue;
        }
        let value;
        if (p.point && p.point.low !== undefined && p.point.high !== undefined) {
            value = formatValue(p.point.low, device.unit) + " – " + formatValue(p.point.high, device.unit);
        } else {
            value = formatValue(p.y, device.unit);
        }
        html += "<br/><span class='hs-tip-name'>" + p.series.name + ":</span> <b>" + value + "</b>";
    }
    return html;
}

/// Formats a value for its unit: "%" (percent), "B/s" (bytes per second), "bps" (bits per second).
function formatValue(value, unit, short) {
    if (value == null || isNaN(value)) {
        return "—";
    }
    switch (unit) {
        case "%":
            return (short ? Math.round(value) : value.toFixed(value < 10 ? 1 : 0)) + "%";
        case "bps":
            return scaled(value, 1000, ["bps", "Kbps", "Mbps", "Gbps", "Tbps"]);
        case "B/s":
            return scaled(value, 1024, ["B/s", "KB/s", "MB/s", "GB/s", "TB/s"]);
        default:
            return Math.round(value).toLocaleString();
    }
}

function scaled(value, step, units) {
    let i = 0;
    while (Math.abs(value) >= step && i < units.length - 1) {
        value /= step;
        i++;
    }
    const digits = value >= 100 || i == 0 ? 0 : 1;
    return value.toFixed(digits) + " " + units[i];
}
