// Gioco "Blocchi" (codice [gioco:blocchi] nelle pagine): blocchi da 3, 4 e 5 quadretti che cadono, righe che
// spariscono quando sono complete e, ogni tanto, una bomba che fa saltare i blocchi intorno. Si gioca senza
// login; a fine partita il punteggio va in classifica solo per gli utenti collegati (/game/blocchi/score).
// I colori si cambiano dal tema con le variabili CSS --game-* sull'elemento .block-game.
// Classifica e chiamate al sito sono in games.js (window.BlogGames), caricato prima.
(function () {
    'use strict';
    var G = window.BlogGames;
    if (!G) return;
    var el = G.el;

    var COLS = 10, ROWS = 20;

    // Forme (coordinate dei quadretti). Da 3 e 4 quadretti fin dall'inizio, da 5 dal livello 3.
    var SHAPES = [
        { cells: [[0, 0], [1, 0], [2, 0]], color: 1, min: 1 },
        { cells: [[0, 0], [0, 1], [1, 1]], color: 2, min: 1 },
        { cells: [[0, 0], [1, 0], [2, 0], [3, 0]], color: 3, min: 1 },
        { cells: [[0, 0], [1, 0], [0, 1], [1, 1]], color: 4, min: 1 },
        { cells: [[0, 0], [1, 0], [2, 0], [1, 1]], color: 5, min: 1 },
        { cells: [[0, 0], [0, 1], [1, 1], [2, 1]], color: 6, min: 1 },
        { cells: [[2, 0], [0, 1], [1, 1], [2, 1]], color: 1, min: 1 },
        { cells: [[1, 0], [2, 0], [0, 1], [1, 1]], color: 2, min: 1 },
        { cells: [[0, 0], [1, 0], [1, 1], [2, 1]], color: 3, min: 1 },
        { cells: [[0, 0], [1, 0], [0, 1], [1, 1], [0, 2]], color: 4, min: 3 },
        { cells: [[0, 0], [2, 0], [0, 1], [1, 1], [2, 1]], color: 5, min: 3 },
        { cells: [[1, 0], [0, 1], [1, 1], [2, 1], [1, 2]], color: 6, min: 3 },
        { cells: [[0, 0], [1, 0], [2, 0], [3, 0], [4, 0]], color: 1, min: 4 }
    ];
    var BOMB = { cells: [[0, 0]], color: 7, bomb: true };
    var LINE_POINTS = [0, 100, 300, 500, 800, 1200];

    function Game(root) {
        this.root = root;
        this.labels = G.labels(root);
        this.state = 'idle';
        this.build();
        this.board.load();
    }

    Game.prototype.t = function (key) { return (this.labels && this.labels[key]) || key; };

    Game.prototype.build = function () {
        var self = this, root = this.root;
        root.innerHTML = '';
        root.setAttribute('tabindex', '0');

        var play = el('div', 'block-game-play');
        var stage = el('div', 'block-game-stage');
        this.canvas = el('canvas', 'block-game-board');
        this.canvas.setAttribute('role', 'img');
        this.canvas.setAttribute('aria-label', this.t('gameName'));
        stage.appendChild(this.canvas);

        this.overlay = el('div', 'block-game-overlay');
        this.overlayTitle = el('p', 'block-game-overlay-title', this.t('gameName'));
        this.overlayText = el('p', 'block-game-overlay-text');
        this.startButton = el('button', 'block-game-start btn', this.t('gameStart'));
        this.startButton.type = 'button';
        this.startButton.addEventListener('click', function () { self.overlayAction(); });
        this.overlay.appendChild(this.overlayTitle);
        this.overlay.appendChild(this.overlayText);
        this.overlay.appendChild(this.startButton);
        stage.appendChild(this.overlay);
        play.appendChild(stage);

        var side = el('div', 'block-game-side');
        var stats = el('dl', 'block-game-stats');
        this.scoreValue = this.stat(stats, 'gameScore');
        this.linesValue = this.stat(stats, 'gameLines');
        this.levelValue = this.stat(stats, 'gameLevel');
        side.appendChild(stats);
        var nextBox = el('div', 'block-game-next');
        nextBox.appendChild(el('span', 'block-game-label', this.t('gameNext')));
        this.nextCanvas = el('canvas', 'block-game-next-canvas');
        nextBox.appendChild(this.nextCanvas);
        side.appendChild(nextBox);
        this.pauseButton = el('button', 'block-game-pause btn', this.t('gamePause'));
        this.pauseButton.type = 'button';
        this.pauseButton.disabled = true;
        this.pauseButton.addEventListener('click', function () { self.togglePause(); });
        side.appendChild(this.pauseButton);
        play.appendChild(side);
        root.appendChild(play);

        // Pulsanti per il telefono (e per chi preferisce il mouse).
        var pad = el('div', 'block-game-pad');
        [['left', '◀', 'gameLeft'], ['rotate', '⟳', 'gameRotate'], ['right', '▶', 'gameRight'], ['down', '▼', 'gameDown'], ['drop', '⤓', 'gameDrop']]
            .forEach(function (b) {
                var button = el('button', 'block-game-key block-game-key-' + b[0], b[1]);
                button.type = 'button';
                button.title = self.t(b[2]);
                button.setAttribute('aria-label', self.t(b[2]));
                G.holdable(button, function () { self.act(b[0]); }, b[0] === 'left' || b[0] === 'right' || b[0] === 'down', function () { return self.state === 'playing'; });
                pad.appendChild(button);
            });
        root.appendChild(pad);

        var help = el('p', 'block-game-help', this.t('gameHelp'));
        if (window.matchMedia && window.matchMedia('(pointer: coarse)').matches) help.textContent = this.t('gameHelpTouch');
        root.appendChild(help);

        this.board = new G.Leaderboard(root, function (k) { return self.t(k); });

        root.addEventListener('keydown', function (e) { self.key(e); });
        this.canvas.addEventListener('click', function () { if (self.state === 'playing') self.rotate(); });
        document.addEventListener('visibilitychange', function () { if (document.hidden && self.state === 'playing') self.togglePause(); });
        window.addEventListener('resize', function () { self.resize(); self.draw(); });

        this.reset();
        this.resize();
        this.draw();
        this.showOverlay(this.t('gameName'), '', this.t('gameStart'));
    };

    Game.prototype.stat = function (list, key) {
        list.appendChild(el('dt', null, this.t(key)));
        var dd = el('dd', null, '0');
        list.appendChild(dd);
        return dd;
    };

    Game.prototype.colors = function () {
        var style = getComputedStyle(this.root);
        var get = function (name, fallback) { var v = style.getPropertyValue(name).trim(); return v || fallback; };
        return {
            board: get('--game-board', '#1b1f27'),
            grid: get('--game-grid', 'rgba(255,255,255,0.06)'),
            ghost: get('--game-ghost', 'rgba(255,255,255,0.18)'),
            pieces: [null,
                get('--game-c1', '#4fb3d9'), get('--game-c2', '#f0b44c'), get('--game-c3', '#7cc576'),
                get('--game-c4', '#e46d6d'), get('--game-c5', '#a78bdb'), get('--game-c6', '#5bc4b0'),
                get('--game-bomb', '#ff6b2c')]
        };
    };

    Game.prototype.resize = function () {
        // Spazio per il campo: la larghezza del gioco meno la colonna dei punteggi.
        var side = this.root.querySelector('.block-game-side');
        var available = (this.root.clientWidth || 340) - (side ? side.offsetWidth : 130) - 16;
        var width = Math.min(Math.max(available, 120), 320);
        // Il campo deve stare tutto nella finestra (con un po' di spazio per i pulsanti sul telefono).
        var height = Math.max(240, (window.innerHeight || 800) - 170);
        this.cell = Math.max(12, Math.floor(Math.min(width / COLS, height / ROWS)));
        var ratio = window.devicePixelRatio || 1;
        [[this.canvas, COLS, ROWS], [this.nextCanvas, 5, 5]].forEach(function (c) {
            var size = c[0] === this.nextCanvas ? Math.max(10, Math.floor(this.cell * 0.7)) : this.cell;
            c[0].width = c[1] * size * ratio;
            c[0].height = c[2] * size * ratio;
            c[0].style.width = c[1] * size + 'px';
            c[0].style.height = c[2] * size + 'px';
            c[0].getContext('2d').setTransform(ratio, 0, 0, ratio, 0, 0);
            c[0]._cell = size;
        }, this);
        this.palette = this.colors();
    };

    Game.prototype.reset = function () {
        this.grid = [];
        for (var r = 0; r < ROWS; r++) { this.grid.push(new Array(COLS).fill(0)); }
        this.score = 0;
        this.lines = 0;
        this.pieces = 0;
        this.piece = null;
        this.next = null;
        this.updateStats();
    };

    Game.prototype.level = function () { return 1 + Math.floor(this.lines / 10); };

    Game.prototype.randomShape = function () {
        // Una bomba ogni tanto, mai nei primi pezzi né due di fila.
        if (this.pieces > 8 && !(this.next && this.next.bomb) && Math.random() < 1 / 28) return BOMB;
        var level = this.level();
        var pool = SHAPES.filter(function (s) { return s.min <= level; });
        return pool[Math.floor(Math.random() * pool.length)];
    };

    Game.prototype.spawn = function () {
        var shape = this.next || this.randomShape();
        this.next = this.randomShape();
        var cells = shape.cells.map(function (c) { return [c[0], c[1]]; });
        var w = Math.max.apply(null, cells.map(function (c) { return c[0]; })) + 1;
        this.piece = { cells: cells, color: shape.color, bomb: !!shape.bomb, x: Math.floor((COLS - w) / 2), y: 0 };
        this.pieces++;
        if (this.collides(this.piece.cells, this.piece.x, this.piece.y)) {
            this.gameOver();
        }
    };

    Game.prototype.collides = function (cells, x, y) {
        for (var i = 0; i < cells.length; i++) {
            var cx = cells[i][0] + x, cy = cells[i][1] + y;
            if (cx < 0 || cx >= COLS || cy >= ROWS) return true;
            if (cy >= 0 && this.grid[cy][cx]) return true;
        }
        return false;
    };

    Game.prototype.move = function (dx, dy) {
        if (!this.piece || this.collides(this.piece.cells, this.piece.x + dx, this.piece.y + dy)) return false;
        this.piece.x += dx;
        this.piece.y += dy;
        return true;
    };

    Game.prototype.rotate = function () {
        var p = this.piece;
        if (!p || p.bomb || p.cells.length === 4 && this.isSquare(p.cells)) return;
        var h = Math.max.apply(null, p.cells.map(function (c) { return c[1]; }));
        var rotated = p.cells.map(function (c) { return [h - c[1], c[0]]; });
        var kicks = [0, -1, 1, -2, 2];
        for (var i = 0; i < kicks.length; i++) {
            if (!this.collides(rotated, p.x + kicks[i], p.y)) {
                p.cells = rotated;
                p.x += kicks[i];
                this.draw();
                return;
            }
        }
    };

    Game.prototype.isSquare = function (cells) {
        var xs = cells.map(function (c) { return c[0]; }), ys = cells.map(function (c) { return c[1]; });
        return Math.max.apply(null, xs) === 1 && Math.max.apply(null, ys) === 1;
    };

    Game.prototype.dropDistance = function () {
        var d = 0;
        while (!this.collides(this.piece.cells, this.piece.x, this.piece.y + d + 1)) d++;
        return d;
    };

    Game.prototype.act = function (action) {
        if (this.state !== 'playing' || !this.piece) return;
        if (action === 'left') this.move(-1, 0);
        else if (action === 'right') this.move(1, 0);
        else if (action === 'rotate') this.rotate();
        else if (action === 'down') { if (this.move(0, 1)) { this.score += 1; this.lastFall = performance.now(); } else this.lock(); }
        else if (action === 'drop') { var d = this.dropDistance(); this.piece.y += d; this.score += 2 * d; this.lock(); }
        this.updateStats();
        this.draw();
    };

    Game.prototype.lock = function () {
        var p = this.piece, self = this;
        p.cells.forEach(function (c) {
            var y = c[1] + p.y;
            if (y >= 0) self.grid[y][c[0] + p.x] = p.color;
        });
        if (p.bomb) {
            // La bomba toglie i blocchi nel quadrato 3x3 intorno a sé (bomba compresa).
            var bx = p.cells[0][0] + p.x, by = p.cells[0][1] + p.y, destroyed = 0;
            for (var y = by - 1; y <= by + 1; y++) {
                for (var x = bx - 1; x <= bx + 1; x++) {
                    if (y >= 0 && y < ROWS && x >= 0 && x < COLS && this.grid[y][x]) {
                        if (!(x === bx && y === by)) destroyed++;
                        this.grid[y][x] = 0;
                    }
                }
            }
            this.score += 20 * destroyed;
            this.flash = { rows: [], bomb: [bx, by], until: performance.now() + 180 };
        }
        var full = [];
        for (var r = 0; r < ROWS; r++) {
            if (this.grid[r].every(function (v) { return v; })) full.push(r);
        }
        if (full.length) {
            this.score += LINE_POINTS[Math.min(full.length, 5)] * this.level();
            full.forEach(function (r) {
                self.grid.splice(r, 1);
                self.grid.unshift(new Array(COLS).fill(0));
            });
            this.lines += full.length;
            this.flash = { rows: full, until: performance.now() + 180 };
        }
        this.piece = null;
        this.updateStats();
        this.spawn();
        this.lastFall = performance.now();
    };

    Game.prototype.interval = function () { return Math.max(90, 800 * Math.pow(0.85, this.level() - 1)); };

    Game.prototype.loop = function (now) {
        if (this.state !== 'playing') return;
        if (now - this.lastFall >= this.interval()) {
            if (!this.move(0, 1)) this.lock();
            this.lastFall = now;
        }
        this.draw();
        var self = this;
        this.frame = requestAnimationFrame(function (t) { self.loop(t); });
    };

    Game.prototype.key = function (e) {
        var map = { ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'rotate', ArrowDown: 'down', ' ': 'drop', Spacebar: 'drop' };
        if (e.key === 'p' || e.key === 'P' || e.key === 'Escape') {
            if (this.state === 'playing' || this.state === 'paused') { e.preventDefault(); this.togglePause(); }
            return;
        }
        if (e.key === 'Enter' && (this.state === 'idle' || this.state === 'over')) { e.preventDefault(); this.overlayAction(); return; }
        if (this.state === 'playing' && map[e.key]) {
            e.preventDefault();
            this.act(map[e.key]);
        }
    };

    Game.prototype.overlayAction = function () {
        if (this.state === 'paused') this.togglePause();
        else this.start();
    };

    Game.prototype.start = function () {
        var self = this;
        this.reset();
        this.token = null;
        this.board.hideMessage();
        this.board.start().then(function (token) { self.token = token; });
        this.state = 'playing';
        this.hideOverlay();
        this.pauseButton.disabled = false;
        this.pauseButton.textContent = this.t('gamePause');
        this.root.focus({ preventScroll: true });
        this.spawn();
        this.lastFall = performance.now();
        this.frame = requestAnimationFrame(function (t) { self.loop(t); });
    };

    Game.prototype.togglePause = function () {
        var self = this;
        if (this.state === 'playing') {
            this.state = 'paused';
            cancelAnimationFrame(this.frame);
            this.pauseButton.textContent = this.t('gameResume');
            this.showOverlay(this.t('gamePause'), '', this.t('gameResume'));
        } else if (this.state === 'paused') {
            this.state = 'playing';
            this.pauseButton.textContent = this.t('gamePause');
            this.hideOverlay();
            this.root.focus({ preventScroll: true });
            this.lastFall = performance.now();
            this.frame = requestAnimationFrame(function (t) { self.loop(t); });
        }
    };

    Game.prototype.gameOver = function () {
        var self = this;
        this.state = 'over';
        this.piece = null;
        cancelAnimationFrame(this.frame);
        this.pauseButton.disabled = true;
        this.draw();
        this.showOverlay(this.t('gameOver'), this.t('gameScore') + ': ' + this.score.toLocaleString(), this.t('gamePlayAgain'));
        this.board.submit(this.token, this.score, this.lines);
    };

    Game.prototype.showOverlay = function (title, body, button) {
        this.overlayTitle.textContent = title;
        this.overlayText.textContent = body;
        this.overlayText.hidden = !body;
        this.startButton.textContent = button;
        this.overlay.hidden = false;
    };

    Game.prototype.hideOverlay = function () { this.overlay.hidden = true; };

    Game.prototype.updateStats = function () {
        this.scoreValue.textContent = this.score.toLocaleString();
        this.linesValue.textContent = this.lines;
        this.levelValue.textContent = this.level();
    };

    Game.prototype.drawCell = function (ctx, x, y, size, color, bomb) {
        var pad = Math.max(1, Math.round(size * 0.06));
        ctx.fillStyle = color;
        if (bomb) {
            ctx.beginPath();
            ctx.arc(x * size + size / 2, y * size + size / 2, size / 2 - pad, 0, Math.PI * 2);
            ctx.fill();
            ctx.fillStyle = 'rgba(255,255,255,0.85)';
            ctx.font = 'bold ' + Math.round(size * 0.6) + 'px sans-serif';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText('✸', x * size + size / 2, y * size + size / 2 + 1);
            return;
        }
        var r = Math.max(2, Math.round(size * 0.18));
        var px = x * size + pad, py = y * size + pad, w = size - 2 * pad;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(px, py, w, w, r); else ctx.rect(px, py, w, w);
        ctx.fill();
        ctx.fillStyle = 'rgba(255,255,255,0.18)';
        ctx.fillRect(px + r / 2, py + r / 2, w - r, Math.max(2, w * 0.18));
    };

    Game.prototype.draw = function () {
        var ctx = this.canvas.getContext('2d'), size = this.canvas._cell, pal = this.palette, self = this;
        if (!size || !pal) return;
        ctx.fillStyle = pal.board;
        ctx.fillRect(0, 0, COLS * size, ROWS * size);
        ctx.strokeStyle = pal.grid;
        ctx.lineWidth = 1;
        for (var gx = 1; gx < COLS; gx++) { ctx.beginPath(); ctx.moveTo(gx * size + 0.5, 0); ctx.lineTo(gx * size + 0.5, ROWS * size); ctx.stroke(); }
        for (var gy = 1; gy < ROWS; gy++) { ctx.beginPath(); ctx.moveTo(0, gy * size + 0.5); ctx.lineTo(COLS * size, gy * size + 0.5); ctx.stroke(); }

        for (var r = 0; r < ROWS; r++) {
            for (var c = 0; c < COLS; c++) {
                if (this.grid[r][c]) this.drawCell(ctx, c, r, size, pal.pieces[this.grid[r][c]], this.grid[r][c] === 7);
            }
        }

        if (this.piece) {
            var p = this.piece, d = this.dropDistance();
            ctx.fillStyle = pal.ghost;
            p.cells.forEach(function (cell) {
                var gy2 = cell[1] + p.y + d;
                if (gy2 >= 0 && d > 0) ctx.fillRect((cell[0] + p.x) * size + 2, gy2 * size + 2, size - 4, size - 4);
            });
            p.cells.forEach(function (cell) {
                if (cell[1] + p.y >= 0) self.drawCell(ctx, cell[0] + p.x, cell[1] + p.y, size, pal.pieces[p.color], p.bomb);
            });
        }

        if (this.flash && performance.now() < this.flash.until) {
            ctx.fillStyle = 'rgba(255,255,255,0.35)';
            if (this.flash.bomb) ctx.fillRect((this.flash.bomb[0] - 1) * size, (this.flash.bomb[1] - 1) * size, 3 * size, 3 * size);
            else this.flash.rows.forEach(function (row) { ctx.fillRect(0, row * size, COLS * size, size); });
        }

        var nctx = this.nextCanvas.getContext('2d'), ns = this.nextCanvas._cell;
        nctx.clearRect(0, 0, 5 * ns, 5 * ns);
        if (this.next && this.state !== 'idle') {
            var cells = this.next.cells;
            var w = Math.max.apply(null, cells.map(function (q) { return q[0]; })) + 1;
            var h = Math.max.apply(null, cells.map(function (q) { return q[1]; })) + 1;
            var ox = (5 - w) / 2, oy = (5 - h) / 2;
            cells.forEach(function (q) { self.drawCell(nctx, q[0] + ox, q[1] + oy, ns, pal.pieces[self.next.color], !!self.next.bomb); });
        }
    };

    G.register('blocchi', Game);
})();
