// Messaggi del modulo di contatto bloccati dal filtro antispam (scheda Messaggi): si rileggono, si recapitano
// quelli validi, si eliminano gli altri. Scheda Regole: controlli automatici attivabili uno per uno e regole
// personalizzate (lista nera e bianca di email, domini, IP, parole, domini dei link).
angular.module('blogAdmin').controller('ContactMessagesController', ["$rootScope", "$scope", "dataService", function ($rootScope, $scope, dataService) {
    var lbl = $rootScope.lbl;
    $scope.tab = 'messages';
    $scope.messages = [];
    $scope.loaded = false;
    $scope.open = {};
    $scope.busy = {};
    $scope.settings = null;
    $scope.linkTest = null;

    // Valori come negli enum del server (ContactRuleField, ContactRuleOperator, ContactRuleAction).
    $scope.fields = [
        { id: 0, key: 'Email' }, { id: 1, key: 'EmailDomain' }, { id: 2, key: 'IP' }, { id: 3, key: 'Name' },
        { id: 4, key: 'Subject' }, { id: 5, key: 'Text' }, { id: 6, key: 'LinkDomain' }
    ].map(function (f) { f.label = lbl['contactField' + f.key] || f.key; return f; });
    $scope.operators = [{ id: 0, key: 'Contains' }, { id: 1, key: 'Equals' }, { id: 2, key: 'Regex' }]
        .map(function (o) { o.label = lbl['contactOperator' + o.key] || o.key; return o; });
    $scope.actions = [{ id: 0, label: lbl.contactRuleBlock }, { id: 1, label: lbl.contactRuleAllow }];

    // Email, dominio, IP e dominio dei link hanno un confronto fisso.
    $scope.hasOperator = function (rule) { return [0, 3, 4, 5].indexOf(rule.Field) >= 0; };

    var showError = function (data) {
        var text = data && data.message ? data.message : (typeof data === 'string' && data ? data : null);
        toastr.error(text || lbl.errorInvalidRequest || 'Error');
    };

    $scope.load = function () {
        dataService.getItems('/api/contactmessages')
            .success(function (data) {
                $scope.messages = data;
                $scope.loaded = true;
                spinOff();
            })
            .error(function (data) { spinOff(); showError(data); });
    };

    $scope.loadSettings = function () {
        dataService.getItems('/api/contactmessages/settings')
            .success(function (data) { $scope.settings = data; })
            .error(showError);
    };

    $scope.show = function (tab) {
        $scope.tab = tab;
        if (tab === 'rules') $scope.loadSettings();
    };

    $scope.blockedCount = function () {
        return $scope.messages.filter(function (m) { return !m.Released; }).length;
    };

    $scope.toggle = function (message) {
        $scope.open[message.Id] = !$scope.open[message.Id];
    };

    $scope.size = function (bytes) {
        return bytes >= 1048576 ? (bytes / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round(bytes / 1024)) + ' KB';
    };

    $scope.release = function (message) {
        if (!window.confirm(lbl.contactReleaseConfirm)) return;
        $scope.busy[message.Id] = true;
        dataService.addItem('/api/contactmessages/' + message.Id + '/release', {})
            .success(function () {
                $scope.busy[message.Id] = false;
                toastr.success(lbl.contactReleased);
                $scope.load();
            })
            .error(function (data) { $scope.busy[message.Id] = false; showError(data); });
    };

    $scope.remove = function (message) {
        var question = (lbl.areYouSure || '{0} {1}?').replace('{0}', (lbl.delete || '').toLowerCase()).replace('{1}', '"' + message.Subject + '"');
        if (!window.confirm(question)) return;
        dataService.deleteById('/api/contactmessages', message.Id)
            .success(function () { $scope.load(); })
            .error(showError);
    };

    $scope.removeBlocked = function () {
        if (!window.confirm(lbl.contactDeleteBlocked + '?')) return;
        dataService.deleteItem('/api/contactmessages', {})
            .success(function () { $scope.load(); })
            .error(showError);
    };

    // Lista nera dal messaggio: email, dominio dell'email o IP del mittente.
    $scope.blacklist = function (message, field) {
        dataService.addItem('/api/contactmessages/' + message.Id + '/blacklist/' + field, {})
            .success(function (data) {
                if (data.added) toastr.success(lbl.contactRuleAdded + ': ' + data.value);
                else toastr.info(lbl.contactRuleExists + ': ' + data.value);
                $scope.settings = null;
            })
            .error(showError);
    };

    // ---- regole ----

    $scope.addRule = function () {
        $scope.settings.Rules.push({ Id: newGuid(), Enabled: true, Field: 1, Operator: 0, Value: '', Action: 0, Note: '', Hits: 0 });
    };

    $scope.removeRule = function (index) {
        $scope.settings.Rules.splice(index, 1);
    };

    $scope.moveRule = function (index, step) {
        var rules = $scope.settings.Rules, target = index + step;
        if (target < 0 || target >= rules.length) return;
        var rule = rules[index];
        rules[index] = rules[target];
        rules[target] = rule;
    };

    $scope.saveSettings = function () {
        dataService.updateItem('/api/contactmessages/settings', $scope.settings)
            .success(function (data) {
                $scope.settings = data;
                toastr.success(lbl.contactRulesSaved);
            })
            .error(showError);
    };

    $scope.testLinks = function () {
        $scope.linkTest = 'loading';
        dataService.getItems('/api/contactmessages/linktest')
            .success(function (data) { $scope.linkTest = data; })
            .error(function (data) { $scope.linkTest = null; showError(data); });
    };

    $scope.linkState = function (state) {
        return { listed: lbl.contactLinkTestListed, refused: lbl.contactLinkTestRefused, notlisted: lbl.contactLinkTestNotListed, timeout: lbl.contactLinkTestTimeout }[state] || state;
    };

    $scope.lastHit = function (rule) {
        return rule.LastHit ? new Date(rule.LastHit).toLocaleString() : '';
    };

    function newGuid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0;
            return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
        });
    }

    $scope.load();

    $(document).ready(function () {
        bindCommon();
    });
}]);
