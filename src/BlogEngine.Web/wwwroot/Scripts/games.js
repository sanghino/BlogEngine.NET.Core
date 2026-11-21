// Parti comuni ai giochi del blog (codice [gioco:nome]): chiamate al sito, classifica, invito ad accedere.
// Ogni gioco ha il suo script (blockgame.js, spacegame.js) che usa window.BlogGames.
(function () {
    'use strict';
    if (window.BlogGames) return;

    function el(tag, cls, content) {
        var node = document.createElement(tag);
        if (cls) node.className = cls;
        if (content !== undefined) node.textContent = content;
        return node;
    }

    // GET o POST (con il token antifalsificazione scritto dal sito in BlogEngineRes).
    function request(url, method, data) {
        var options = { method: method, credentials: 'same-origin', headers: {} };
        if (method === 'POST') {
            var body = new FormData();
            Object.keys(data || {}).forEach(function (k) { if (data[k] !== null && data[k] !== undefined) body.append(k, data[k]); });
            options.body = body;
            var res = window.BlogEngineRes || {};
            if (res.antiforgery) options.headers[res.antiforgeryHeader || 'RequestVerificationToken'] = res.antiforgery;
        }
        return fetch(url, options).then(function (r) {
            if (!r.ok) throw new Error(r.status);
            return r.json();
        });
    }

    function labels(root) {
        try { return JSON.parse(root.getAttribute('data-labels') || '{}'); } catch (e) { return {}; }
    }

    // Classifica sotto il gioco: i migliori, il record di chi è collegato, l'invito ad accedere per gli altri.
    function Leaderboard(root, t) {
        this.root = root;
        this.t = t;
        this.api = root.getAttribute('data-api');
        this.loginUrl = root.getAttribute('data-login');
        this.registerUrl = root.getAttribute('data-register');
        var box = el('div', 'block-game-leaderboard');
        box.appendChild(el('h3', 'block-game-leaderboard-title', t('gameLeaderboard')));
        this.message = el('p', 'block-game-message');
        this.message.hidden = true;
        box.appendChild(this.message);
        this.table = el('div', 'block-game-table');
        box.appendChild(this.table);
        this.account = el('p', 'block-game-account');
        box.appendChild(this.account);
        root.appendChild(box);
    }

    Leaderboard.prototype.load = function () {
        var self = this;
        request(this.api + 'scores', 'GET').then(function (d) {
            self.render(d.top || []);
            self.renderAccount(d.loggedIn, d.me);
        }).catch(function () { self.table.textContent = self.t('gameError'); });
    };

    // Gettone di inizio partita (la durata la misura il sito); null se il sito non risponde.
    Leaderboard.prototype.start = function () {
        return request(this.api + 'start', 'POST', {}).then(function (d) { return d.token; }).catch(function () { return null; });
    };

    Leaderboard.prototype.submit = function (token, score, lines) {
        var self = this;
        return request(this.api + 'score', 'POST', { token: token || '', score: score, lines: lines })
            .then(function (d) {
                self.showMessage(d.message, d.status);
                if (d.status === 'LOGIN') self.renderAccount(false, null);
                else self.renderAccount(true, d.me);
                self.render(d.top || []);
            })
            .catch(function () { self.showMessage(self.t('gameError'), 'ERROR'); });
    };

    Leaderboard.prototype.showMessage = function (msg, status) {
        this.message.textContent = msg;
        this.message.className = 'block-game-message block-game-message-' + String(status || '').toLowerCase();
        this.message.hidden = false;
    };

    Leaderboard.prototype.hideMessage = function () { this.message.hidden = true; };

    Leaderboard.prototype.render = function (top) {
        this.table.innerHTML = '';
        if (!top.length) {
            this.table.appendChild(el('p', 'block-game-empty', this.t('gameLeaderboardEmpty')));
            return;
        }
        var table = el('table'), thead = el('thead'), head = el('tr'), body = el('tbody');
        ['#', this.t('gamePlayer'), this.t('gameScore'), this.t('gameLines')].forEach(function (h) { head.appendChild(el('th', null, h)); });
        thead.appendChild(head);
        table.appendChild(thead);
        var lang = document.documentElement.lang || undefined;
        top.forEach(function (row) {
            var tr = el('tr', row.me ? 'block-game-me' : null);
            tr.appendChild(el('td', 'block-game-rank', String(row.rank)));
            var name = el('td', 'block-game-name', row.name);
            var parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(row.date || '');
            if (parts) name.title = new Date(+parts[1], parts[2] - 1, +parts[3]).toLocaleDateString(lang);
            tr.appendChild(name);
            tr.appendChild(el('td', 'block-game-points', Number(row.score).toLocaleString(lang)));
            tr.appendChild(el('td', 'block-game-lines', String(row.lines)));
            body.appendChild(tr);
        });
        table.appendChild(body);
        this.table.appendChild(table);
    };

    Leaderboard.prototype.renderAccount = function (loggedIn, me) {
        this.account.innerHTML = '';
        if (loggedIn) {
            if (me) this.account.textContent = this.t('gameYourBest') + ': ' + Number(me.score).toLocaleString() + ' · ' + this.t('gameYourRank') + ': ' + me.rank;
            return;
        }
        this.account.appendChild(document.createTextNode(this.t('gameLoginToRank') + ' '));
        var login = el('a', null, this.t('gameLogin'));
        login.href = this.loginUrl;
        this.account.appendChild(login);
        if (this.registerUrl) {
            this.account.appendChild(document.createTextNode(' · '));
            var reg = el('a', null, this.t('gameRegister'));
            reg.href = this.registerUrl;
            this.account.appendChild(reg);
        }
    };

    // Avvia i giochi di un tipo presenti nella pagina (anche quelli aggiunti dopo il caricamento).
    function register(game, Game) {
        function init() {
            var nodes = document.querySelectorAll('.block-game[data-game="' + game + '"]:not([data-ready])');
            for (var i = 0; i < nodes.length; i++) {
                nodes[i].setAttribute('data-ready', '1');
                new Game(nodes[i]);
            }
        }
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
        else init();
    }

    // Tenendo premuto un pulsante l'azione si ripete (movimenti); "repeat" falso = una volta per tocco.
    function holdable(button, act, repeat, isPlaying) {
        var timer = null;
        var stop = function () { if (timer) { clearTimeout(timer); timer = null; } };
        button.addEventListener('pointerdown', function (e) {
            e.preventDefault();
            if (!isPlaying()) return;
            act();
            if (!repeat) return;
            var again = function (delay) { timer = setTimeout(function () { if (isPlaying()) act(); again(60); }, delay); };
            again(220);
        });
        ['pointerup', 'pointerleave', 'pointercancel'].forEach(function (ev) { button.addEventListener(ev, stop); });
    }

    window.BlogGames = { el: el, request: request, labels: labels, Leaderboard: Leaderboard, register: register, holdable: holdable };
})();
