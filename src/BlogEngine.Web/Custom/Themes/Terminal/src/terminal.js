// Tema Terminal: pulsante modalità chiara/scura (ricordata nel browser) e menu su schermi piccoli.
(function () {
    var root = document.documentElement;

    var toggle = document.getElementById('themeToggle');
    if (toggle) {
        var label = function () {
            var light = root.getAttribute('data-theme') === 'light';
            toggle.textContent = '[ ' + toggle.getAttribute(light ? 'data-label-dark' : 'data-label-light') + ' ]';
            toggle.setAttribute('aria-pressed', light ? 'true' : 'false');
        };
        label();
        toggle.addEventListener('click', function () {
            var mode = root.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
            root.setAttribute('data-theme', mode);
            try { localStorage.setItem('terminal-theme', mode); } catch (e) { }
            label();
        });
    }

    var navToggle = document.getElementById('navToggle');
    var navList = document.getElementById('navList');
    if (navToggle && navList) {
        navToggle.addEventListener('click', function () {
            var open = navList.classList.toggle('open');
            navToggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
    }
})();
