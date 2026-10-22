var dotNetHelper;

export function LoadHighchartsResources() {
    // This function checks to see if the Highcharts libraries have been loaded.
    // If so, this immediately invokes the OnHighchartsLoaded method in the Blazor
    // component. If not, the libraries are loaded and then the method is invoked.

    // The libraries are vendored under wwwroot/lib/highcharts (Highcharts 11.4.8, as in FreeCRM).
    // highcharts-more adds the arearange series used for min/max bands.
    var helper = dotNetHelper;

    if (typeof (Highcharts) == "object") {
        helper.invokeMethodAsync("OnHighchartsLoaded");
    } else if (window.highchartsResourcesLoading) {
        // Another component is already loading the libraries.
        waitForHighcharts(() => {
            helper.invokeMethodAsync("OnHighchartsLoaded");
        });
    } else {
        window.highchartsResourcesLoading = true;

        // AMD Workaround: Monaco's vs/loader.js creates a global AMD environment.
        // Highcharts detects AMD and registers as a module instead of setting window.Highcharts.
        // We temporarily hide the AMD loader so Highcharts falls back to browser globals.
        var tempDefine = window.define;
        window.define = undefined;

        LoadCssResource("lib/highcharts/highcharts.css", "highcharts-light", () => {
            LoadScriptResource("lib/highcharts/highcharts.js", () => {
                LoadScriptResource("lib/highcharts/highcharts-more.js", () => {
                    LoadScriptResource("lib/highcharts/modules/exporting.js", () => {
                        LoadScriptResource("lib/highcharts/modules/export-data.js", () => {
                            LoadScriptResource("lib/highcharts/modules/accessibility.js", () => {
                                // Restore AMD loader now that Highcharts is loaded
                                window.define = tempDefine;

                                // Wait for Highcharts to be actually available before notifying Blazor
                                waitForHighcharts(() => {
                                    helper.invokeMethodAsync("OnHighchartsLoaded");
                                });
                            });
                        });
                    });
                });
            });
        });
    }
}

// Polls for Highcharts availability; handles race condition where script.onload
// fires before the library has finished initializing its global object.
function waitForHighcharts(callback, attempts = 0) {
    if (typeof (Highcharts) == "object") {
        callback();
    } else if (attempts < 100) {
        // Retry up to 100 times (10 seconds max with 100ms intervals)
        setTimeout(() => waitForHighcharts(callback, attempts + 1), 100);
    } else {
        console.error("Highcharts failed to load after multiple attempts");
    }
}

function FileNameFromUrl(url) {
    var output = url;
    if (url != undefined && url != null && url != "") {
        var lastSlash = url.lastIndexOf("/");
        if (lastSlash > -1) {
            output = url.substring(lastSlash + 1);
        }
    }
    return output;
}

function LoadCssResource(url, existingClass, callback) {
    // Only load if the existingClass to test for is not found
    var exists = false;

    // Check to see if this script is already loaded.
    var scriptName = FileNameFromUrl(url);
    if (scriptName != undefined && scriptName != null && scriptName != "") {
        // Check to see if this script is already loaded.
        $("script").each(function () {
            var source = $(this).attr("src");
            if (source != undefined && source != null && source != "") {
                var sourceScriptName = FileNameFromUrl(source);
                if (sourceScriptName != undefined && sourceScriptName != null && sourceScriptName != "") {
                    if (scriptName.toLowerCase() == sourceScriptName.toLowerCase()) {
                        exists = true;
                    }
                }
            }
        });
    }

    if (exists) {
        if (callback != undefined && callback != null) {
            callback();
        }
    } else {
        var head = document.getElementsByTagName('head')[0];
        var link = document.createElement("link");
        link.href = url;
        link.type = "text/css";
        link.rel = "stylesheet";
        if (callback != undefined && callback != null) {
            link.onload = callback;
        }
        head.appendChild(link);
    }
}

function LoadScriptResource(url, callback) {
    var head = document.getElementsByTagName('head')[0];
    var script = document.createElement('script');
    script.type = 'text/javascript';
    script.src = url;
    if (callback != undefined && callback != null) {
        script.onload = callback;
    }
    head.appendChild(script);
}

export function SetDotNetHelper(value) {
    dotNetHelper = value;
}

export function RenderChart_Column(elementId, chartTitle, chartSubtitle, yAxisText, seriesCategories, seriesData) {
    //console.log("RenderChart_Column", elementId, chartTitle, chartSubtitle, yAxisText, seriesCategories, seriesData);

    Highcharts.chart(elementId, {
        chart: {
            type: 'column',
            styledMode: true
        },
        credits: { enabled: false },
        title: { text: chartTitle },
        subtitle: {
            text: chartSubtitle,
            x: -20,
            useHTML: true,
        },
        xAxis: {
            categories: seriesCategories,
            crosshair: true,
        },
        yAxis: {
            min: 0,
            title: {
                text: yAxisText
            }
        },
        legend: { enabled: false },
        series: seriesData,
        plotOptions: {
            series: {
                cursor: 'pointer',
                point: {
                    events: {
                        click: function (event) {
                            dotNetHelper.invokeMethodAsync("ChartItemClicked", this.index);
                        }
                    }
                }
            }
        }
    });
}

export function RenderChart_Pie(elementId, chartTitle, chartSubtitle, seriesData) {
    //console.log("RenderChart_Pie", elementId, chartTitle, chartSubtitle, seriesData);

    // Convert the seriesData to a simple javascript object.
    var chartData = [];
    for (var x = 0; x < seriesData.length; x++) {
        chartData.push([seriesData[x].name, seriesData[x].data]);
    }

    Highcharts.chart(elementId, {
        chart: {
            plotBackgroundColor: null,
            plotBorderWidth: null,
            plotShadow: false,
            type: 'pie',
            styledMode: true
        },
        credits: { enabled: false },
        title: {
            text: chartTitle,
        },
        subtitle: {
            text: chartSubtitle,
            x: -20,
            useHTML: true,
        },
        tooltip: {
            formatter: function () {
                var tooltip = seriesData[this.point.index].tooltip;
                return tooltip;
            },
            useHTML: true
        },
        accessibility: {
            point: {
                valueSuffix: ''
            }
        },
        plotOptions: {
            pie: {
                allowPointSelect: true,
                cursor: 'pointer',
                dataLabels: {
                    enabled: true,
                    format: '<b>{point.name}</b>'
                }
            }
        },
        series: [{
            name: 'Tickets',
            colorByPoint: true,
            data: chartData,
            point: {
                events: {
                    click: function (event) {
                        dotNetHelper.invokeMethodAsync("ChartItemClicked", this.index);
                    }
                }
            }
        }]
    });
}