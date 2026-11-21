// Classifiche dei giochi del blog (codice [gioco:nome] nelle pagine): elenco completo, rimozione di un
// giocatore (per esempio un punteggio falso) e azzeramento della classifica.
angular.module('blogAdmin').controller('GamesController', ["$rootScope", "$scope", "dataService", function ($rootScope, $scope, dataService) {
    $scope.games = [];
    $scope.loaded = false;

    var showError = function (data) {
        toastr.error(typeof data === 'string' && data ? data : $rootScope.lbl.errorInvalidRequest || 'Error');
    };

    $scope.load = function () {
        dataService.getItems('/api/games')
            .success(function (data) {
                $scope.games = data;
                $scope.loaded = true;
                spinOff();
            })
            .error(function (data) { spinOff(); showError(data); });
    };

    $scope.help = function (game) {
        return ($rootScope.lbl.gamesHelp || '{0}').replace('{0}', game.Code);
    };

    $scope.duration = function (seconds) {
        var m = Math.floor(seconds / 60), s = seconds % 60;
        return m + ':' + (s < 10 ? '0' : '') + s;
    };

    $scope.remove = function (game, entry) {
        if (!window.confirm($rootScope.lbl.gamesRemoveConfirm)) return;
        dataService.deleteById('/api/games/' + encodeURIComponent(game.Game), encodeURIComponent(entry.UserName))
            .success(function () { $scope.load(); })
            .error(showError);
    };

    $scope.reset = function (game) {
        if (!window.confirm($rootScope.lbl.gamesResetConfirm)) return;
        dataService.deleteById('/api/games', encodeURIComponent(game.Game))
            .success(function () { $scope.load(); })
            .error(showError);
    };

    $scope.load();

    $(document).ready(function () {
        bindCommon();
    });
}]);
