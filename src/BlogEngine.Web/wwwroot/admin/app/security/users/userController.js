angular.module('blogAdmin').controller('UsersController', ["$rootScope", "$scope", "$filter", "dataService", function ($rootScope, $scope, $filter, dataService) {
    $scope.items = [];
    $scope.roles = [];
    $scope.editItem = {};
    $scope.isNewItem = false;
    $scope.focusInput = false;
    $scope.security = $rootScope.security;

    $scope.load = function () {
        if (!$scope.security.canManageUsers == true) {
            window.location.replace("../account/login");
        }
        dataService.getItems('/api/users', { take: 0, skip: 0, filter: "1 == 1", order: "UserName" })
        .success(function (data) {
            angular.copy(data, $scope.items);
            gridInit($scope, $filter);
            spinOff();
        })
        .error(function () {
            toastr.error($rootScope.lbl.errorLoadingUsers);
        });
    }

    $scope.loadEditForm = function (id) {
        $scope.loadRoles(id);
        if (!id) {
            $scope.editItem = {};
            $scope.isNewItem = true;
            $("#modal-user-edit").modal();
            $scope.focusInput = true;
            return false;
        }
        else {
            $scope.isNewItem = false;
        }
        spinOn();
        dataService.getItems('/api/users?id=' + id)
        .success(function (data) {
            angular.copy(data, $scope.editItem);
            $("#modal-user-edit").modal();
            $scope.focusInput = true;
            spinOff();
        })
        .error(function () {
            toastr.error($rootScope.lbl.errorLoadingUser);
            spinOff();
        });
    }

    $scope.loadRoles = function (id) {
        spinOn();
        if (!id) {
            id = "fakeusername";
        }
        dataService.getItems('/api/roles/getuserroles/' + id)
            .success(function (data) {
                angular.copy(data, $scope.roles);
                gridInit($scope, $filter);
                spinOff();
            })
            .error(function () {
                toastr.error($rootScope.lbl.errorLoadingRoles);
                spinOff();
            });
    }

    $scope.saveUser = function () {
        if (!$('#form').valid()) {
            return false;
        }
        save(false);
    }

    // allowSharedEmail: l'amministratore ha confermato di usare un'email che appartiene già a un altro utente.
    function save(allowSharedEmail) {
        spinOn();
        $scope.editItem.roles = $scope.roles;
        $scope.editItem.AllowSharedEmail = allowSharedEmail;
        var request = $scope.isNewItem
            ? dataService.addItem("/api/users", $scope.editItem)
            : dataService.updateItem("/api/users/update/item", $scope.editItem);

        request
        .success(function (data) {
            toastr.success($scope.isNewItem ? $rootScope.lbl.userAdded : $rootScope.lbl.userUpdatedShort);
            if (data && data.Warning) {
                toastr.warning(data.Warning);
            }
            $scope.load();
            spinOff();
            $("#modal-user-edit").modal('hide');
            $scope.focusInput = false;
        })
        .error(function (data, status) {
            spinOff();
            // Email già usata da un altro utente: un amministratore può confermare e usarla lo stesso.
            if (status === 409 && data && data.CanShareEmail && !allowSharedEmail) {
                if (window.confirm(data.Message)) {
                    save(true);
                }
                return;
            }
            // Il motivo (nome con spazi, password corta, email già usata...) e la finestra resta aperta per correggere.
            toastr.error(serverMessage(data, $scope.isNewItem ? $rootScope.lbl.errorAddingNewUser : $rootScope.lbl.updateFailed));
        });
    }

    $scope.processChecked = function (action) {
        processChecked("/api/users/processchecked/", action, $scope, dataService);
    }

    $scope.load();

    $(document).ready(function () {
        $('#form').validate({
            rules: {
                txtEmail: { required: true, email: true },
                txtUserName: { required: true },
                txtPassword: { required: true, minlength: 8 },
                txtConfirmPassword: { required: true, equalTo: '#txtPassword' }
            }
        });
    });

    $(document).ready(function () {
        bindCommon();
    });
}]);