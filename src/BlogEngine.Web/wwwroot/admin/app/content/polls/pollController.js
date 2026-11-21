// Sondaggi del widget Poll: elenco con stato e risultati, creazione e modifica, chiusura e riapertura.
angular.module('blogAdmin').controller('PollsController', ["$rootScope", "$scope", "dataService", function ($rootScope, $scope, dataService) {
    $scope.items = [];
    $scope.poll = {};
    $scope.loaded = false;
    $scope.focusInput = false;
    $scope.publicUrl = SiteVars.RelativeWebRoot + 'polls';

    // I campi data e ora di AngularJS vogliono oggetti Date; il sito usa testi "2026-11-30T18:00" nell'ora del blog.
    var toDate = function (text) {
        var m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(text || '');
        return m ? new Date(+m[1], m[2] - 1, +m[3], +m[4], +m[5]) : null;
    };
    var toText = function (date) {
        if (!(date instanceof Date) || isNaN(date.getTime())) return '';
        var two = function (n) { return (n < 10 ? '0' : '') + n; };
        return date.getFullYear() + '-' + two(date.getMonth() + 1) + '-' + two(date.getDate()) + 'T' + two(date.getHours()) + ':' + two(date.getMinutes());
    };

    var showError = function (data) {
        toastr.error(typeof data === 'string' && data ? data : $rootScope.lbl.errorInvalidRequest || 'Error');
    };

    $scope.load = function () {
        dataService.getItems('/api/polls')
            .success(function (data) {
                angular.forEach(data, function (item) {
                    var old = $scope.items.filter(function (i) { return i.Id === item.Id; })[0];
                    item.open = old ? old.open : false;
                });
                $scope.items = data;
                $scope.loaded = true;
                spinOff();
            })
            .error(function (data) { spinOff(); showError(data); });
    };

    $scope.statusLabel = function (status) {
        return $rootScope.lbl['pollStatus' + status.charAt(0).toUpperCase() + status.slice(1)] || status;
    };

    $scope.addNew = function () {
        $scope.poll = {
            Id: null, Question: '', Description: '', MultipleChoice: false, MaxChoices: 0,
            Options: [{ Text: '' }, { Text: '' }], Starts: null, Ends: null, RegisteredOnly: false, Featured: false,
            ResultsVisibility: 'AfterVote', Voters: 0
        };
        $("#modal-poll").modal();
        $scope.focusInput = true;
    };

    $scope.edit = function (item) {
        dataService.getItems('/api/polls/' + encodeURIComponent(item.Id))
            .success(function (data) {
                data.Starts = toDate(data.Starts);
                data.Ends = toDate(data.Ends);
                $scope.poll = data;
                $("#modal-poll").modal();
                $scope.focusInput = true;
            })
            .error(showError);
    };

    $scope.addOption = function () {
        $scope.poll.Options.push({ Text: '' });
    };

    $scope.removeOption = function (index) {
        $scope.poll.Options.splice(index, 1);
    };

    $scope.moveOption = function (index, step) {
        var target = index + step;
        if (target < 0 || target >= $scope.poll.Options.length) return;
        var options = $scope.poll.Options;
        var moved = options.splice(index, 1)[0];
        options.splice(target, 0, moved);
    };

    $scope.save = function () {
        var poll = angular.copy($scope.poll);
        poll.MaxChoices = parseInt(poll.MaxChoices, 10) || 0;
        poll.Starts = toText($scope.poll.Starts);
        poll.Ends = toText($scope.poll.Ends);
        var request = poll.Id
            ? dataService.updateItem('/api/polls/' + poll.Id, poll)
            : dataService.addItem('/api/polls', poll);
        request
            .success(function () {
                toastr.success($rootScope.lbl.pollSaved);
                $("#modal-poll").modal('hide');
                $scope.focusInput = false;
                $scope.load();
            })
            .error(showError);
    };

    $scope.setClosed = function (item, closed) {
        dataService.updateItem('/api/polls/' + item.Id + '/' + (closed ? 'close' : 'reopen'), {})
            .success(function () { $scope.load(); })
            .error(showError);
    };

    $scope.remove = function (item) {
        if (!window.confirm($rootScope.lbl.pollDeleteConfirm)) return;
        dataService.deleteById('/api/polls', item.Id)
            .success(function () { $scope.load(); })
            .error(showError);
    };

    $scope.load();

    $(document).ready(function () {
        bindCommon();
    });
}]);
