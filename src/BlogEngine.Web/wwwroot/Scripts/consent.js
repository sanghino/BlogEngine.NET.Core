// Banner del consenso ai cookie (GDPR). Il sito scrive "spenti" gli script non ancora autorizzati
// (<script type="text/plain" data-consent="statistics|marketing">): qui vengono accesi dopo il consenso.
// La scelta è salvata nel cookie "blogengine_consent" ("v1:statistics,marketing"; "v1:" = solo necessari).
(function () {
    var banner = document.getElementById('be-consent');
    if (!banner) {
        return;
    }

    var reopen = document.getElementById('be-consent-reopen');
    var options = banner.querySelector('.be-consent-options');
    var customize = banner.querySelector('[data-consent-action="customize"]');
    var customizeLabel = customize ? customize.textContent : '';
    var cookieName = banner.getAttribute('data-cookie');
    var categories = ['statistics', 'marketing'];

    function readChoice() {
        var match = document.cookie.match(new RegExp('(?:^|; )' + cookieName + '=([^;]*)'));
        if (!match) {
            return null;
        }

        var value = decodeURIComponent(match[1]);
        if (value.indexOf('v1:') !== 0) {
            return null;
        }

        return value.substring(3).split(',').filter(function (c) { return c; });
    }

    function writeChoice(granted) {
        var days = parseInt(banner.getAttribute('data-days'), 10) || 180;
        var cookie = cookieName + '=' + encodeURIComponent('v1:' + granted.join(',')) +
            '; Max-Age=' + days * 86400 + '; Path=' + (banner.getAttribute('data-path') || '/') + '; SameSite=Lax';
        if (location.protocol === 'https:') {
            cookie += '; Secure';
        }

        document.cookie = cookie;
    }

    // Sostituisce ogni script spento della categoria con uno vero, nello stesso punto e nello stesso ordine.
    function activate(category) {
        var scripts = document.querySelectorAll('script[type="text/plain"][data-consent="' + category + '"]');
        for (var i = 0; i < scripts.length; i++) {
            var old = scripts[i];
            var script = document.createElement('script');
            for (var a = 0; a < old.attributes.length; a++) {
                var attribute = old.attributes[a];
                if (attribute.name !== 'type' && attribute.name !== 'data-consent' && attribute.name !== 'data-type') {
                    script.setAttribute(attribute.name, attribute.value);
                }
            }

            if (old.getAttribute('data-type')) {
                script.type = old.getAttribute('data-type');
            }

            if (!old.src) {
                script.text = old.text;
            }

            old.parentNode.replaceChild(script, old);
        }
    }

    function showOptions(show) {
        options.hidden = !show;
        customize.textContent = show ? customize.getAttribute('data-save-label') : customizeLabel;
        customize.setAttribute('data-consent-action', show ? 'save' : 'customize');
    }

    function close() {
        banner.hidden = true;
        showOptions(false);
        if (reopen) {
            reopen.hidden = false;
        }
    }

    function choose(granted) {
        var previous = readChoice() || [];
        writeChoice(granted);

        // Un consenso revocato non si può "spegnere" a pagina aperta: si ricarica la pagina.
        var revoked = previous.some(function (c) { return granted.indexOf(c) < 0; });
        if (revoked) {
            location.reload();
            return;
        }

        granted.forEach(function (c) {
            if (previous.indexOf(c) < 0) {
                activate(c);
            }
        });
        close();
    }

    banner.addEventListener('click', function (event) {
        var button = event.target.closest('[data-consent-action]');
        if (!button) {
            return;
        }

        switch (button.getAttribute('data-consent-action')) {
            case 'accept':
                choose(categories.slice());
                break;
            case 'reject':
                choose([]);
                break;
            case 'customize':
                showOptions(true);
                break;
            case 'save':
                choose(categories.filter(function (c) {
                    var box = options.querySelector('input[name="' + c + '"]');
                    return box && box.checked;
                }));
                break;
        }
    });

    function open() {
        var current = readChoice() || [];
        categories.forEach(function (c) {
            var box = options.querySelector('input[name="' + c + '"]');
            if (box) {
                box.checked = current.indexOf(c) >= 0;
            }
        });
        banner.hidden = false;
        showOptions(true);
        if (reopen) {
            reopen.hidden = true;
        }
    }

    if (reopen) {
        reopen.addEventListener('click', open);
    }

    // Per i temi: <a href="#" onclick="BlogEngineConsent.open(); return false;">Preferenze cookie</a>
    window.BlogEngineConsent = {
        open: open,
        has: function (category) { return (readChoice() || []).indexOf(category) >= 0; },
    };
})();
