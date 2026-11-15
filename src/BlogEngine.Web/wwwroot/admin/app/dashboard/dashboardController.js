angular.module('blogAdmin').controller('DashboardController', ["$rootScope", "$scope", "$location", "$log", "$filter", "dataService", function ($rootScope, $scope, $location, $log, $filter, dataService) {
    $scope.vm = {};
    $scope.itemToPurge = {};
    $scope.security = $rootScope.security;
    $scope.focusInput = false;
    $scope.qd = angular.copy(newDraft);

    $scope.postDraft = function () {
        var draft = {
            "Id": "",
            "Title": $scope.qd.title,
            "Author": "",
            "Content": $scope.qd.text,
            "DateCreated": moment().format("YYYY-MM-DD HH:mm"),
            "Slug": "",
            "Categories": "",
            "Tags": "",
            "Comments": "",
            "HasCommentsEnabled": true,
            "IsPublished": false
        }  
        dataService.addItem('api/posts', draft)
        .success(function (data) {
            $scope.qd = angular.copy(newDraft);
            var dft = {
                "IsChecked": false,
                "Id": data.Id,
                "Title": data.Title,
                "Author": data.Author,
                "DateCreated": data.DateCreated,
                "Slug": data.Slug,
                "RelativeLink": data.RelativeLink,
                "Categories": null,
                "Tags": null,
                "Comments": null,
                "IsPublished": false
            };
            $scope.vm.DraftPosts.push(dft);
            toastr.success($rootScope.lbl.postAdded);
        })
        .error(function () { toastr.error($rootScope.lbl.failedAddingNewPost); });
    }

    $scope.openTrash = function () {
        $("#modal-trash").modal();
        return false;
    }

    $scope.openLogFile = function () {
        $("#modal-log-file").modal();
        return false;
    }
    $scope.purgeLog = function () {
        dataService.updateItem('/api/logs/purgelog/file', $scope.itemToPurge)
        .success(function (data) {
            $scope.vm.Logs = [];
            $("#modal-log-file").modal('hide');
            toastr.success($rootScope.lbl.purged);
            return false;
        })
        .error(function (data) {
            toastr.error($rootScope.lbl.errorPurging);
        });
    }

    $scope.purge = function (id) {
        if (id) {
            $scope.itemToPurge = findInArray($scope.vm.Trash, "Id", id);
        }
        dataService.updateItem('/api/trash/purge/' + id, $scope.itemToPurge)
        .success(function (data) {
            for (var i = 0; i < $scope.vm.Trash.length; i++)
            if ($scope.vm.Trash[i].Id === id) {
                $scope.vm.Trash.splice(i, 1);
                break;
            }
            toastr.success($rootScope.lbl.purged);
            return false;
        })
        .error(function (data) {
            toastr.error($rootScope.lbl.errorPurging);
        });
    }
    $scope.purgeAll = function () {
        dataService.updateItem('/api/trash/purgeall/all')
        .success(function (data) {
            $scope.vm.Trash = [];
            $("#modal-trash").modal('hide');
            toastr.success($rootScope.lbl.purged);
            return false;
        })
        .error(function (data) {
            toastr.error($rootScope.lbl.errorPurging);
        });
    }
    $scope.restore = function (id) {
        if (id) {
            $scope.itemToPurge = findInArray($scope.vm.Trash, "Id", id);
        }
        dataService.updateItem('/api/trash/restore/' + id, $scope.itemToPurge)
        .success(function (data) {
            for (var i = 0; i < $scope.vm.Trash.length; i++)
            if ($scope.vm.Trash[i].Id === id) {
                $scope.vm.Trash.splice(i, 1);
                break;
            }
            toastr.success($rootScope.lbl.restored);
            return false;
        })
        .error(function (data) {
            toastr.error($rootScope.lbl.errorRestoring);
        });
    }

    $scope.load = function () {
        if ($rootScope.security.showTabDashboard === false) {
            window.location = "../account/login";
        }
        spinOn();


        dataService.getItems('/api/dashboard')
        .success(function (data) {
            angular.copy(data, $scope.vm);
            spinOff();
        })
        .error(function (data) {
            toastr.success($rootScope.lbl.errorGettingStats);
            spinOff();
        });
    }
    // Statistiche delle visite: grafico a barre disegnato in SVG, senza librerie.
    $scope.chart = { width: 600, height: 150, bars: [], ticks: [], selectedText: "" };

    $scope.selectDay = function (bar) {
        $scope.chart.selectedText = bar.text;
    };

    $scope.loadVisits = function () {
        dataService.getItems('/api/visits', { days: 30 })
        .success(function (data) {
            var lbl = $rootScope.lbl;
            var pageViews = (lbl.pageViews || "").toLowerCase();
            var language = SiteVars.Language || undefined;
            var formatDate = function (date, options) {
                var parts = date.split("-");
                return new Date(+parts[0], +parts[1] - 1, +parts[2]).toLocaleDateString(language, options);
            };
            var visitors = (lbl.visitors || "").toLowerCase();
            $scope.visits = data;
            $scope.totals = [
                angular.extend({ label: lbl.today }, data.Today),
                angular.extend({ label: lbl.visitsLast7Days }, data.Week),
                angular.extend({ label: (lbl.visitsLastDays || "").replace("{0}", data.Days.length) }, data.Period)
            ];

            var sourceLabels = { search: lbl.sourceSearch, social: lbl.sourceSocial, sites: lbl.sourceSites, direct: lbl.sourceDirect };
            var sourceTotal = data.Sources.reduce(function (sum, s) { return sum + s.Count; }, 0);
            $scope.sources = data.Sources.map(function (s) {
                return { label: sourceLabels[s.Name] || s.Name, percent: sourceTotal ? Math.round(100 * s.Count / sourceTotal) : 0 };
            });

            var chart = $scope.chart;
            var count = data.Days.length;
            var slot = chart.width / count;
            var barWidth = Math.max(1, slot * 0.7);
            var max = Math.max(1, Math.max.apply(null, data.Days.map(function (d) { return d.Views; })));
            var plot = chart.height - 2;
            chart.bars = data.Days.map(function (d, i) {
                var viewsHeight = d.Views / max * plot;
                var visitorsHeight = d.Visitors / max * plot;
                return {
                    x: i * slot + (slot - barWidth) / 2,
                    width: barWidth,
                    viewsY: chart.height - viewsHeight,
                    viewsHeight: viewsHeight,
                    visitorsY: chart.height - visitorsHeight,
                    visitorsHeight: visitorsHeight,
                    text: formatDate(d.Date, { weekday: "short", day: "numeric", month: "numeric", year: "numeric" }) + ": " + d.Views + " " + pageViews + ", " + d.Visitors + " " + visitors
                };
            });
            chart.ticks = count ? [data.Days[0], data.Days[Math.floor((count - 1) / 2)], data.Days[count - 1]].map(function (d) { return formatDate(d.Date, { day: "numeric", month: "numeric" }); }) : [];
            chart.selectedText = chart.bars.length ? chart.bars[chart.bars.length - 1].text : "";
        })
        .error(function () {
            $scope.visits = null;
        });
    };

    $scope.load();
    if ($rootScope.security.showTabDashboard !== false) {
        $scope.loadVisits();
    }
    $(document).ready(function () {
        bindCommon();
    });
}]);

var newDraft = {
    show: UserVars.Rights.indexOf("CreateNewPosts") > 0,
    title: "",
    text: ""
}


